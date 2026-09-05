using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Application.Maintenance;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.MaintenanceCampaigns.Commands;

public record UpsertCampaignResultRecord(
    Guid Id, Guid DeviceSnapshotId, Guid ChecklistItemId, Guid? StandardParamId,
    string? MeasuredValue, bool IsPass, string? Notes);

// ─── UPSERT RESULT (BUG-D concurrent-safe retry-merge) ───

public record UpsertCampaignResultOutcome(
    bool Success,
    string? ErrorCode = null,
    bool IsConflict = false,
    bool IsCreate = false,
    UpsertCampaignResultRecord? Record = null);

/// <summary>
/// [MC-9] Upsert 1 kết quả (DeviceSnapshot × ChecklistItem × StandardParam?). Patch-aware: field
/// không gửi không ghi đè. [MC-10] IsPass tự tính. Extracted verbatim from
/// MaintenanceCampaignsController.UpsertResult including the [BUG-D fix] concurrent-safe loop:
/// race INSERT-vs-INSERT (nhiều request cùng đọc existing=null rồi cùng Add) → request thua vi phạm
/// partial unique index (23505) → trước đây raw 500. Upsert semantics = merge: catch unique
/// violation, detach bản ghi fail, retry — vòng kế tiếp re-read sẽ thấy row (đã được request khác
/// insert) và chuyển sang nhánh Update. Bounded 3 attempts; vượt → 409 RESULT_CONCURRENT_WRITE
/// (không 500). NOTE: chỉ nhánh Create mới có thể đụng 23505; nhánh Update là UPDATE ... WHERE Id
/// (last-write-wins, không đụng unique index) → nếu 23505 khi !isNew thì là lỗi khác, để bubble lên
/// thay vì retry vô hạn. Manual logging — không ILoggableCommand (BUG-D retry loop tự quản SaveChanges).
/// </summary>
public record UpsertCampaignResultCommand(
    Guid CampaignId,
    Guid DeviceSnapshotId,
    Guid ChecklistItemId,
    string? MeasuredValue,
    bool? IsPass,
    string? Notes,
    Guid? StandardParamId,
    Guid CurrentUserId)
    : IRequest<UpsertCampaignResultOutcome>;

public class UpsertCampaignResultCommandHandler : IRequestHandler<UpsertCampaignResultCommand, UpsertCampaignResultOutcome>
{
    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _companyScope;
    private readonly IActionLogService _actionLogService;

    public UpsertCampaignResultCommandHandler(
        IApplicationDbContext context, ICompanyScopeService companyScope, IActionLogService actionLogService)
    {
        _context = context;
        _companyScope = companyScope;
        _actionLogService = actionLogService;
    }

    public async Task<UpsertCampaignResultOutcome> Handle(UpsertCampaignResultCommand request, CancellationToken cancellationToken)
    {
        var id = request.CampaignId;
        var dto = request;
        var c = await CampaignAccess.GetVisibleCampaignAsync(_context, _companyScope, id);
        if (c == null)
            return new UpsertCampaignResultOutcome(false, "NOT_FOUND");
        if (c.Status == MaintenanceCampaignStatus.Completed)
            return new UpsertCampaignResultOutcome(false, "CAMPAIGN_COMPLETED");
        if (dto.DeviceSnapshotId == Guid.Empty || dto.ChecklistItemId == Guid.Empty)
            return new UpsertCampaignResultOutcome(false, "RESULT_TARGET_REQUIRED");

        // Snapshot must belong to THIS campaign.
        if (!c.DeviceSnapshots.Any(s => s.Id == dto.DeviceSnapshotId))
            return new UpsertCampaignResultOutcome(false, "INVALID_DEVICE_SNAPSHOT");
        // Item must belong to the pinned template version.
        if (!await _context.MaintenanceChecklistItems.AnyAsync(i => i.Id == dto.ChecklistItemId && i.TemplateVersionId == c.TemplateVersionId, cancellationToken))
            return new UpsertCampaignResultOutcome(false, "INVALID_CHECKLIST_ITEM");

        // [MC-7c] Chặn cặp ngoài phạm vi: Item khai báo vị trí áp dụng, nhưng thiết bị (snapshot) không ở
        // vị trí đó → 400 INVALID_ITEM_POSITION (không cho tạo result thừa ngay từ upsert).
        var snapshotPosId = c.DeviceSnapshots.FirstOrDefault(s => s.Id == dto.DeviceSnapshotId)?.SystemPositionId;
        if (!await CampaignAccess.IsApplicablePairAsync(_context, dto.ChecklistItemId, snapshotPosId))
            return new UpsertCampaignResultOutcome(false, "INVALID_ITEM_POSITION");

        // [MC-9] Validate StandardParamId: nếu gửi thì phải thuộc ChecklistItem đó; nếu không gửi thì phải là
        // hạng mục KHÔNG có tiêu chuẩn nào (để không lẫn lộn).
        var paramCount = await _context.MaintenanceStandardParams.CountAsync(p => p.ChecklistItemId == dto.ChecklistItemId, cancellationToken);
        if (paramCount == 0 && dto.StandardParamId.HasValue)
            return new UpsertCampaignResultOutcome(false, "STANDARD_PARAM_NOT_APPLICABLE");
        if (paramCount > 0 && !dto.StandardParamId.HasValue)
            return new UpsertCampaignResultOutcome(false, "STANDARD_PARAM_REQUIRED");
        if (dto.StandardParamId.HasValue)
        {
            var belongs = await _context.MaintenanceStandardParams.AnyAsync(p => p.Id == dto.StandardParamId.Value && p.ChecklistItemId == dto.ChecklistItemId, cancellationToken);
            if (!belongs)
                return new UpsertCampaignResultOutcome(false, "INVALID_STANDARD_PARAM");
        }

        // ── Upsert + [BUG-D fix] concurrent-safe: see type doc. ──
        const int maxUpsertAttempts = 3;
        MaintenanceChecklistResult existing = null!;
        var isNew = false;
        string? oldMeasuredValue = null;
        bool? oldIsPass = null;
        string? oldNotes = null;
        for (var attempt = 1; ; attempt++)
        {
            existing = await _context.MaintenanceChecklistResults
                .FirstOrDefaultAsync(r => r.CampaignId == id && r.DeviceSnapshotId == dto.DeviceSnapshotId && r.ChecklistItemId == dto.ChecklistItemId && r.StandardParamId == dto.StandardParamId, cancellationToken)
                ?? null!;

            isNew = false;
            oldMeasuredValue = null;
            oldIsPass = null;
            oldNotes = null;
            if (existing == null)
            {
                isNew = true;
                existing = new MaintenanceChecklistResult
                {
                    CampaignId = id,
                    DeviceSnapshotId = dto.DeviceSnapshotId,
                    ChecklistItemId = dto.ChecklistItemId,
                    StandardParamId = dto.StandardParamId,
                    MeasuredValue = dto.MeasuredValue,
                    IsPass = dto.IsPass ?? false,
                    Notes = dto.Notes
                };
                _context.MaintenanceChecklistResults.Add(existing);
            }
            else
            {
                // Patch semantics (Task F/M1): absent field NEVER overwrites existing data.
                oldMeasuredValue = existing.MeasuredValue;
                oldIsPass = existing.IsPass;
                oldNotes = existing.Notes;
                if (dto.MeasuredValue is not null) existing.MeasuredValue = dto.MeasuredValue;
                if (dto.IsPass.HasValue) existing.IsPass = dto.IsPass.Value;
                if (dto.Notes is not null) existing.Notes = dto.Notes;
            }

            // [MC-10] Dòng gắn StandardParam → IsPass TỰ ĐỘNG = so sánh(MeasuredValue, Threshold) theo Operator.
            // Không tin client gửi isPass (máy quyết định thay — đúng thiết kế đã chốt hướng a).
            if (dto.StandardParamId.HasValue)
            {
                var param = await _context.MaintenanceStandardParams.AsNoTracking()
                    .FirstAsync(p => p.Id == dto.StandardParamId.Value, cancellationToken);
                existing.IsPass = MaintenanceChecklistRules.TryParseMeasured(existing.MeasuredValue, out var mv)
                    ? MaintenanceChecklistRules.EvaluateThreshold(param.ThresholdOperator, param.ThresholdValue, mv)
                    : false; // chưa có giá trị đo → chưa xác định (UI hiện "Chưa xác định" dựa trên MeasuredValue rỗng)
            }

            try
            {
                await _context.SaveChangesAsync(cancellationToken);
                break; // ghi thành công (Create hoặc Update)
            }
            catch (DbUpdateException ex) when (isNew
                && ex.InnerException is Npgsql.PostgresException pg
                && pg.SqlState == Npgsql.PostgresErrorCodes.UniqueViolation)
            {
                // [BUG-D] Request khác vừa insert cùng key → detach bản ghi fail, retry:
                // vòng kế tiếp re-read sẽ thấy row và merge (nhánh Update).
                _context.Entry(existing).State = EntityState.Detached;
                if (attempt >= maxUpsertAttempts)
                    return new UpsertCampaignResultOutcome(false, "RESULT_CONCURRENT_WRITE", IsConflict: true);
            }
        }

        // [BUG-B fix] ActionLog cho ghi/xóa kết quả checklist — cùng format LogCampaignAction
        // (ItemType.MaintenanceCampaign + TargetSystemInfo) như Create/Complete ở trên.
        // Auto-IsPass là giá trị máy tính (post-MC-10), đo được trong LogMeta để truy vết.
        _actionLogService.Log(CampaignAccess.BuildLog(
            isNew ? ActionType.Create : ActionType.Update, c, request.CurrentUserId,
            isNew
                ? $"Ghi kết quả checklist đợt \"{(c.SystemInfo?.Name ?? c.SystemInfoId.ToString())}\""
                : $"Cập nhật kết quả checklist đợt \"{(c.SystemInfo?.Name ?? c.SystemInfoId.ToString())}\"",
            new
            {
                campaignId = c.Id,
                deviceSnapshotId = dto.DeviceSnapshotId,
                checklistItemId = dto.ChecklistItemId,
                standardParamId = dto.StandardParamId,
                changes = new
                {
                    measuredValue = new { old = oldMeasuredValue, @new = existing.MeasuredValue },
                    isPass = new { old = oldIsPass, @new = existing.IsPass },
                    notes = new { old = oldNotes, @new = existing.Notes }
                }
            }));
        await _context.SaveChangesAsync(cancellationToken);

        return new UpsertCampaignResultOutcome(true, IsCreate: isNew, Record: new UpsertCampaignResultRecord(
            existing.Id, existing.DeviceSnapshotId, existing.ChecklistItemId, existing.StandardParamId,
            existing.MeasuredValue, existing.IsPass, existing.Notes));
    }
}

// ─── DELETE RESULT ───

public record DeleteCampaignResultCommand(
    Guid CampaignId, Guid DeviceSnapshotId, Guid ChecklistItemId, Guid? StandardParamId, Guid CurrentUserId)
    : IRequest<DeleteCampaignResultOutcome>;

public record DeleteCampaignResultOutcome(bool Success, string? ErrorCode = null);

public class DeleteCampaignResultCommandHandler : IRequestHandler<DeleteCampaignResultCommand, DeleteCampaignResultOutcome>
{
    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _companyScope;
    private readonly IActionLogService _actionLogService;

    public DeleteCampaignResultCommandHandler(
        IApplicationDbContext context, ICompanyScopeService companyScope, IActionLogService actionLogService)
    {
        _context = context;
        _companyScope = companyScope;
        _actionLogService = actionLogService;
    }

    public async Task<DeleteCampaignResultOutcome> Handle(DeleteCampaignResultCommand request, CancellationToken cancellationToken)
    {
        var id = request.CampaignId;
        var dto = request;
        var c = await CampaignAccess.GetVisibleCampaignAsync(_context, _companyScope, id);
        if (c == null)
            return new DeleteCampaignResultOutcome(false, "NOT_FOUND");
        if (c.Status == MaintenanceCampaignStatus.Completed)
            return new DeleteCampaignResultOutcome(false, "CAMPAIGN_COMPLETED");

        var result = await _context.MaintenanceChecklistResults
            .FirstOrDefaultAsync(r => r.CampaignId == id && r.DeviceSnapshotId == dto.DeviceSnapshotId && r.ChecklistItemId == dto.ChecklistItemId && r.StandardParamId == dto.StandardParamId, cancellationToken);
        if (result == null)
            return new DeleteCampaignResultOutcome(false, "RESULT_NOT_FOUND");

        _context.MaintenanceChecklistResults.Remove(result);
        await _context.SaveChangesAsync(cancellationToken);

        // [BUG-B fix] ActionLog cho xóa kết quả checklist — đủ dữ liệu truy vết bản ghi đã xóa.
        _actionLogService.Log(CampaignAccess.BuildLog(
            ActionType.Delete, c, request.CurrentUserId,
            $"Xóa kết quả checklist đợt \"{(c.SystemInfo?.Name ?? c.SystemInfoId.ToString())}\"",
            new
            {
                campaignId = c.Id,
                deviceSnapshotId = dto.DeviceSnapshotId,
                checklistItemId = dto.ChecklistItemId,
                standardParamId = dto.StandardParamId,
                deleted = new { measuredValue = result.MeasuredValue, isPass = result.IsPass, notes = result.Notes }
            }));
        await _context.SaveChangesAsync(cancellationToken);

        return new DeleteCampaignResultOutcome(true);
    }
}
