using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.AssetMaintenances.Commands;

public record UpdateMaintenanceResult(
    bool Success,
    string Message,
    string? ErrorCode = null,
    Guid? MaintenanceId = null,
    Guid? CompanyId = null,
    string? Note = null);

/// <summary>
/// [Subtask C] PUT api/v1/maintenances/{id} (extracted verbatim from
/// AssetMaintenancesController.Update). Guard order verbatim: NOT_FOUND → scope 404 (S1,
/// hide-existence — verbatim trap (a): reads use 403, Update uses 404, DO NOT unify) →
/// MAINTENANCE_CLOSED (closed record immutable, rejects ALL fields) → FIELD_LOCKED (StartDate
/// only — the sole explicit lock: supplied AND different → reject) → COMPLETION_BEFORE_START →
/// INVALID_COST → INVALID_SUPPLIER → whitelist assign → assignees replace-all → thin Update log.
/// Lock map (audited): StartDate = explicit FIELD_LOCKED; AssetId (route-only), CompanyId +
/// Snapshot* (absent from DTO entirely) = structural locks; closed state locks everything.
/// Assignment semantics — [BUG-N FIX 2026-09-05] PATCH-SAFE (Task M1/M2 pattern, behavior change
/// approved): Title/Notes/Type/IsWarranty were already conditional; SupplierId / CompletionDate /
/// Cost now ALSO assigned only when SENT (`is not null` / HasValue) — an absent field no longer
/// clears the stored value (previously direct-assign → absent → null, wiping supplier/cost/
/// completion data). Explicit nulls in the JSON still clear (deserialization sends null →
/// indistinguishable from absent in this DTO shape — known JSON-patch limitation, same as the
/// patch DTOs elsewhere). ILoggableCommand only.
/// </summary>
public record UpdateMaintenanceCommand(
    Guid Id,
    string? Title,
    string? Notes,
    AssetMaintenanceType? Type,
    Guid? SupplierId,
    DateTime? CompletionDate,
    decimal? Cost,
    bool? IsWarranty,
    Guid[]? AssigneeUserIds,
    DateTime? StartDate,
    Guid CurrentUserId)
    : IRequest<UpdateMaintenanceResult>, ILoggableCommand<UpdateMaintenanceResult>
{
    public ActionLogEntry? BuildLogEntry(UpdateMaintenanceResult response)
    {
        if (!response.Success)
            return null;

        return new ActionLogEntry
        {
            ItemType = ItemType.AssetMaintenance,
            ItemId = Id,
            ActionType = ActionType.Update,
            CreatedBy = CurrentUserId,
            CompanyId = response.CompanyId,
            Note = response.Note
        };
    }
}

public class UpdateMaintenanceCommandHandler : IRequestHandler<UpdateMaintenanceCommand, UpdateMaintenanceResult>
{
    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _companyScope;

    public UpdateMaintenanceCommandHandler(IApplicationDbContext context, ICompanyScopeService companyScope)
    {
        _context = context;
        _companyScope = companyScope;
    }

    public async Task<UpdateMaintenanceResult> Handle(UpdateMaintenanceCommand request, CancellationToken cancellationToken)
    {
        var m = await _context.AssetMaintenances
            .FirstOrDefaultAsync(x => x.Id == request.Id && x.DeletedAt == null, cancellationToken);
        if (m == null)
            return new UpdateMaintenanceResult(false, "Maintenance not found.", "NOT_FOUND");

        // [SEC-FIX S1] Company scoping → 404 hide-existence (NOT 403 — verbatim trap (a)).
        var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        if (userCompanyId.HasValue && m.CompanyId != userCompanyId.Value && m.CompanyId != Guid.Empty)
            return new UpdateMaintenanceResult(false, "Maintenance not found.", "NOT_FOUND");

        // Absolute lock: a closed record is immutable (audit-trail protection) — reject ALL fields.
        if (m.IsClosed)
            return new UpdateMaintenanceResult(false, "Bản ghi đã đóng, không thể chỉnh sửa.", "MAINTENANCE_CLOSED");

        // Locked fields — AssetId (from route), snapshot fields (never sent in DTO), StartDate:
        // reject when a DIFFERENT value is supplied so the user understands history is immutable.
        if (request.StartDate.HasValue && request.StartDate.Value != m.StartDate)
            return new UpdateMaintenanceResult(false, "Không thể thay đổi (field đã khóa): startDate.", "FIELD_LOCKED");

        if (request.CompletionDate.HasValue && request.CompletionDate.Value < m.StartDate)
            return new UpdateMaintenanceResult(false, "Ngày hoàn thành không được trước ngày bắt đầu.", "COMPLETION_BEFORE_START");
        if (request.Cost.HasValue && request.Cost.Value < 0)
            return new UpdateMaintenanceResult(false, "Chi phí không được âm.", "INVALID_COST");
        if (request.SupplierId.HasValue && !await _context.Suppliers.AnyAsync(s => s.Id == request.SupplierId.Value, cancellationToken))
            return new UpdateMaintenanceResult(false, "Nhà cung cấp không hợp lệ.", "INVALID_SUPPLIER");

        // Whitelist: Title, Notes, Type, SupplierId, CompletionDate, Cost, IsWarranty.
        // [BUG-N FIX] PATCH-SAFE: every field assigned ONLY when sent (Task M1/M2 pattern).
        // NOTE: the pre-migration code assigned SupplierId/CompletionDate/Cost directly (absent →
        // null → cleared — BUG-E class); registered as BUG-N during migration subtask C, now fixed.
        if (!string.IsNullOrWhiteSpace(request.Title)) m.Title = request.Title.Trim();
        if (request.Notes is not null) m.Notes = request.Notes;
        if (request.Type.HasValue) m.Type = request.Type.Value;
        if (request.SupplierId.HasValue) m.SupplierId = request.SupplierId;
        if (request.CompletionDate.HasValue) m.CompletionDate = DateTime.SpecifyKind(request.CompletionDate.Value, DateTimeKind.Unspecified);
        if (request.Cost.HasValue) m.Cost = request.Cost;
        if (request.IsWarranty.HasValue) m.IsWarranty = request.IsWarranty.Value;
        m.UpdatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);

        // Assignee list (replace-all, max 5, company-scoped). It may STILL be edited after the record
        // has been inspected — only the CLOSE step freezes it (the IsClosed guard above rejects any
        // edit on a closed record, which includes the assignee list).
        if (request.AssigneeUserIds != null)
        {
            var assigneeError = await MaintenanceAssignees.ValidateAsync(
                _context, _companyScope, request.AssigneeUserIds, m.CompanyId, cancellationToken);
            if (assigneeError != null)
                return new UpdateMaintenanceResult(false, assigneeError.Message, assigneeError.ErrorCode);
            await MaintenanceAssignees.ReplaceAsync(_context, request.Id, request.AssigneeUserIds, cancellationToken);
        }

        await _context.SaveChangesAsync(cancellationToken);

        return new UpdateMaintenanceResult(true, "Đã cập nhật bảo trì.",
            MaintenanceId: m.Id, CompanyId: m.CompanyId,
            Note: $"Cập nhật bảo trì \"{m.Title}\"");
    }
}
