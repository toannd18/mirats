using aspire_react.Server.Application.AssetMaintenances.Queries;
using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.AssetMaintenances.Commands;

public record CreateMaintenanceResult(
    bool Success,
    string Message,
    string? ErrorCode = null,
    Guid? MaintenanceId = null,
    Guid? CompanyId = null,
    string? LogMeta = null,
    string? Note = null);

/// <summary>
/// [Subtask B] POST api/v1/assets/{assetId}/maintenances + POST api/v1/maintenances — ONE command.
/// Audit verdict (approved scope note): both routes funnel into the SAME private CreateCoreAsync
/// with zero divergence (the aggregated route only repacks its DTO field-by-field into the same
/// request record) → a single command preserves parity exactly; two commands would duplicate.
/// Guards verbatim in order: TITLE_REQUIRED → COMPLETION_BEFORE_START → INVALID_COST →
/// INVALID_SUPPLIER → asset NOT_FOUND → scope FORBIDDEN (controller maps to Forbid() 403) →
/// assignee rules (MAX_5_ASSIGNEES / INVALID_ASSIGNEE / ASSIGNEE_COMPANY_MISMATCH).
/// CompanyId server-set (= Asset.CompanyId ?? Guid.Empty, locked afterwards); snapshot taken
/// once via the shared MaintenanceSnapshot builder (subtask A); DateTimeKind Unspecified
/// throughout (timestamp without time zone). ILoggableCommand only (no output-cache).
/// Thin Log(entry) → enriched behavior log is the approved 2a delta (RemoteIp/UserAgent/
/// ActionSource added; API view DTO does not expose them).
/// </summary>
public record CreateMaintenanceCommand(
    Guid AssetId,
    AssetMaintenanceType Type,
    string Title,
    string? Notes,
    Guid? SupplierId,
    DateTime StartDate,
    DateTime? CompletionDate,
    decimal? Cost,
    bool IsWarranty,
    Guid[]? AssigneeUserIds,
    Guid CurrentUserId)
    : IRequest<CreateMaintenanceResult>, ILoggableCommand<CreateMaintenanceResult>
{
    public ActionLogEntry? BuildLogEntry(CreateMaintenanceResult response)
    {
        if (!response.Success)
            return null;

        return new ActionLogEntry
        {
            ItemType = ItemType.AssetMaintenance,
            ItemId = response.MaintenanceId!.Value,
            ActionType = ActionType.Create,
            CreatedBy = CurrentUserId,
            CompanyId = response.CompanyId,
            LogMeta = response.LogMeta,
            Note = response.Note
        };
    }
}

public class CreateMaintenanceCommandHandler : IRequestHandler<CreateMaintenanceCommand, CreateMaintenanceResult>
{
    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _companyScope;

    public CreateMaintenanceCommandHandler(IApplicationDbContext context, ICompanyScopeService companyScope)
    {
        _context = context;
        _companyScope = companyScope;
    }

    public async Task<CreateMaintenanceResult> Handle(CreateMaintenanceCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
            return new CreateMaintenanceResult(false, "Tiêu đề (Title) là bắt buộc.", "TITLE_REQUIRED");
        if (request.CompletionDate.HasValue && request.CompletionDate.Value < request.StartDate)
            return new CreateMaintenanceResult(false, "Ngày hoàn thành không được trước ngày bắt đầu.", "COMPLETION_BEFORE_START");
        if (request.Cost.HasValue && request.Cost.Value < 0)
            return new CreateMaintenanceResult(false, "Chi phí không được âm.", "INVALID_COST");
        if (request.SupplierId.HasValue && !await _context.Suppliers.AnyAsync(s => s.Id == request.SupplierId.Value, cancellationToken))
            return new CreateMaintenanceResult(false, "Nhà cung cấp không hợp lệ.", "INVALID_SUPPLIER");

        var asset = await _context.Assets
            .Include(a => a.SystemPosition).ThenInclude(sp => sp.SystemInfo)
            .Include(a => a.Location)
            .Include(a => a.CurrentAssignment)
            .FirstOrDefaultAsync(a => a.Id == request.AssetId, cancellationToken);
        if (asset == null)
            return new CreateMaintenanceResult(false, "Asset not found.", "NOT_FOUND");

        // Company scope (defense in depth — do not trust the client-side asset filter).
        var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        if (userCompanyId.HasValue && asset.CompanyId.HasValue && asset.CompanyId.Value != userCompanyId.Value)
            return new CreateMaintenanceResult(false, "Forbidden.", "FORBIDDEN");

        // Assignees (optional on create): max 5 + same-company rule, validated against the server-set
        // maintenance company (= Asset.CompanyId ?? Guid.Empty, so floater records allow any user).
        var assigneeError = await ValidateAssigneesAsync(request.AssigneeUserIds, asset.CompanyId ?? Guid.Empty);
        if (assigneeError != null)
            return assigneeError;

        var snap = await MaintenanceSnapshot.BuildAsync(_context, asset, cancellationToken);

        var m = new AssetMaintenance
        {
            AssetId = request.AssetId,
            Type = request.Type,
            Title = request.Title.Trim(),
            Notes = request.Notes,
            SupplierId = request.SupplierId,
            CompanyId = asset.CompanyId ?? Guid.Empty,
            StartDate = DateTime.SpecifyKind(request.StartDate, DateTimeKind.Unspecified),
            CompletionDate = request.CompletionDate.HasValue ? DateTime.SpecifyKind(request.CompletionDate.Value, DateTimeKind.Unspecified) : null,
            Cost = request.Cost,
            IsWarranty = request.IsWarranty,
            SnapshotSystemInfoId = snap.SysInfoId,
            SnapshotSystemInfoName = snap.SysInfoName,
            SnapshotSystemPositionId = snap.PosId,
            SnapshotSystemPositionName = snap.PosName,
            SnapshotLocationId = snap.LocId,
            SnapshotLocationName = snap.LocName,
            SnapshotAssignedUserId = snap.UserId,
            SnapshotAssignedUserName = snap.UserName,
            SnapshotDepartmentId = snap.DeptId,
            SnapshotDepartmentName = snap.DeptName,
            CreatedById = request.CurrentUserId,
            CreatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified),
            UpdatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified)
        };
        _context.AssetMaintenances.Add(m);
        if (request.AssigneeUserIds != null)
        {
            foreach (var uid in request.AssigneeUserIds.Distinct())
            {
                _context.AssetMaintenanceAssignees.Add(new AssetMaintenanceAssignee
                {
                    MaintenanceId = m.Id,
                    UserId = uid,
                    AssignedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified)
                });
            }
        }

        await _context.SaveChangesAsync(cancellationToken);

        return new CreateMaintenanceResult(true, "Đã tạo bảo trì.",
            MaintenanceId: m.Id, CompanyId: m.CompanyId,
            Note: $"Tạo bảo trì \"{m.Title}\"");
    }

    /// <summary>
    /// Ported from ValidateAssigneesAsync (controller helper returned IActionResult — handler returns
    /// the failure result instead). Rules verbatim: distinct, max 5, users must exist, same-company
    /// as the record (superuser scope-null and floater Guid.Empty skip). Null when valid.
    /// </summary>
    private async Task<CreateMaintenanceResult?> ValidateAssigneesAsync(Guid[]? assigneeUserIds, Guid maintenanceCompanyId)
    {
        if (assigneeUserIds == null || assigneeUserIds.Length == 0) return null;

        var distinct = assigneeUserIds.Distinct().ToArray();
        if (distinct.Length > 5)
            return new CreateMaintenanceResult(false, "Tối đa 5 người phụ trách cho một bản ghi bảo trì.", "MAX_5_ASSIGNEES");

        var users = await _context.Users.AsNoTracking()
            .Where(u => distinct.Contains(u.Id))
            .Select(u => new { u.Id, u.CompanyId })
            .ToListAsync();
        if (users.Count != distinct.Length)
            return new CreateMaintenanceResult(false, "Có người phụ trách không tồn tại trong hệ thống.", "INVALID_ASSIGNEE");

        var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        if (userCompanyId.HasValue && maintenanceCompanyId != Guid.Empty
            && users.Any(u => u.CompanyId != maintenanceCompanyId))
            return new CreateMaintenanceResult(false, "Người phụ trách phải thuộc cùng công ty với bản ghi bảo trì.", "ASSIGNEE_COMPANY_MISMATCH");

        return null;
    }
}
