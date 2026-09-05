using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Application.Maintenance;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.MaintenanceCampaigns.Commands;

public record CompleteCampaignOutcome(
    bool Success,
    string? ErrorCode = null,
    string? ErrorMessage = null,
    Guid? CampaignId = null,
    string? Status = null,
    DateTime? EndDate = null,
    DateTime? NextMaintenanceDueDate = null,
    Guid? ReviewerId = null,
    int ResultsCount = 0);

/// <summary>
/// [MC-3] POST api/v1/maintenance/campaigns/{id}/complete (extracted verbatim from
/// MaintenanceCampaignsController.Complete). Guard order verbatim: NOT_FOUND →
/// CAMPAIGN_ALREADY_COMPLETED → ── Completeness gate: mọi CẶP ÁP DỤNG (item × snapshot × param)
/// phải có kết quả. [MC-7c] KHÔNG còn S×I toàn phần: item không khai báo vị trí (universal) → đếm
/// mọi snapshot; item khai báo vị trí → chỉ đếm snapshot nằm trong danh sách vị trí của item.
/// [MC-9] Với hạng mục CÓ tiêu chuẩn: số dòng = snapshots_applicable × paramCount; KHÔNG có tiêu
/// chuẩn: ×1. [A3] Công thức đếm nằm ở MaintenanceChecklistRules.CountExpectedResults (nguồn sự
/// thật duy nhất) ── → EndDate ??= UtcNow; Status → Completed; ReviewerId ??= user;
/// NextMaintenanceDueDate = (EndDate ?? UtcNow) + min(CycleMonths) của MỌI item trong version đã
/// pin (người dùng chốt: cảnh báo SỚM theo hạng mục lặp lại thường xuyên nhất — lý do an toàn hạ
/// tầng). Manual logging — verbatim 2-step save (data, then log).
/// </summary>
public record CompleteCampaignCommand(Guid Id, Guid CurrentUserId) : IRequest<CompleteCampaignOutcome>;

public class CompleteCampaignCommandHandler : IRequestHandler<CompleteCampaignCommand, CompleteCampaignOutcome>
{
    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _companyScope;
    private readonly IActionLogService _actionLogService;

    public CompleteCampaignCommandHandler(
        IApplicationDbContext context, ICompanyScopeService companyScope, IActionLogService actionLogService)
    {
        _context = context;
        _companyScope = companyScope;
        _actionLogService = actionLogService;
    }

    public async Task<CompleteCampaignOutcome> Handle(CompleteCampaignCommand request, CancellationToken cancellationToken)
    {
        var c = await CampaignAccess.GetVisibleCampaignAsync(_context, _companyScope, request.Id);
        if (c == null)
            return new CompleteCampaignOutcome(false, "NOT_FOUND");
        if (c.Status == MaintenanceCampaignStatus.Completed)
            return new CompleteCampaignOutcome(false, "CAMPAIGN_ALREADY_COMPLETED");

        var snapshots = c.DeviceSnapshots.ToList();
        var items = await _context.MaintenanceChecklistItems.AsNoTracking()
            .Include(i => i.Positions)
            .Include(i => i.StandardParams)
            .Where(i => i.TemplateVersionId == c.TemplateVersionId)
            .ToListAsync(cancellationToken);
        var resultCount = c.Results.Count;
        var expected = MaintenanceChecklistRules.CountExpectedResults(items, snapshots);
        if (expected > 0 && resultCount < expected)
            return new CompleteCampaignOutcome(false, "CAMPAIGN_RESULTS_INCOMPLETE",
                $"Cần ghi đủ kết quả checklist trước khi hoàn thành ({resultCount}/{expected} bản ghi).");

        var endDate = c.EndDate ?? DateTime.UtcNow;
        var prevDue = c.SystemInfo?.NextMaintenanceDueDate;

        c.EndDate = endDate;
        c.Status = MaintenanceCampaignStatus.Completed;
        c.ReviewerId ??= request.CurrentUserId;

        // ── NextMaintenanceDueDate = EndDate + min(CycleMonths) over ALL items of the pinned version.
        // User-confirmed (MC-3): warn EARLY — the most frequent checklist item drives the next due date.
        DateTime? due = items.Count > 0
            ? endDate.AddMonths(items.Min(i => i.CycleMonths))
            : null;

        if (c.SystemInfo == null)
            c.SystemInfo = await _context.SystemInfos.FindAsync(new object?[] { c.SystemInfoId }, cancellationToken);
        if (c.SystemInfo != null) c.SystemInfo.NextMaintenanceDueDate = due;

        await _context.SaveChangesAsync(cancellationToken);

        _actionLogService.Log(CampaignAccess.BuildLog(
            ActionType.Complete, c, request.CurrentUserId,
            $"Hoàn thành đợt bảo dưỡng cho hệ thống \"{c.SystemInfo?.Name ?? c.SystemInfoId.ToString()}\"", new
            {
                changes = new
                {
                    status = new { old = MaintenanceCampaignStatus.InProgress.ToString(), @new = MaintenanceCampaignStatus.Completed.ToString() },
                    endDate = new { old = (DateTime?)null, @new = endDate },
                    nextMaintenanceDueDate = new { old = prevDue, @new = due },
                    reviewerId = new { old = (Guid?)null, @new = c.ReviewerId }
                }
            }));
        await _context.SaveChangesAsync(cancellationToken);

        return new CompleteCampaignOutcome(true,
            CampaignId: c.Id,
            Status: c.Status.ToString(),
            EndDate: c.EndDate,
            NextMaintenanceDueDate: due,
            ReviewerId: c.ReviewerId,
            ResultsCount: resultCount);
    }
}
