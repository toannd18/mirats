using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.AssetMaintenances.Commands;

public record CloseMaintenanceResult(
    bool Success,
    string Message,
    string? ErrorCode = null,
    Guid? MaintenanceId = null,
    Guid? CompanyId = null,
    bool IsClosed = false,
    DateTime? ClosedAt = null,
    Guid? ClosedById = null,
    string? Note = null);

public record InspectMaintenanceResult(
    bool Success,
    string Message,
    string? ErrorCode = null,
    Guid? MaintenanceId = null,
    Guid? CompanyId = null,
    Guid? InspectedById = null,
    DateTime? InspectedAt = null,
    string? Note = null);

public record ReopenMaintenanceResult(
    bool Success,
    string Message,
    string? ErrorCode = null,
    Guid? MaintenanceId = null,
    Guid? CompanyId = null,
    bool IsClosed = false,
    DateTime? ClosedAt = null,
    Guid? ClosedById = null,
    string? Note = null);

/// <summary>
/// [Subtask D] POST api/v1/maintenances/{id}/close (extracted verbatim from
/// AssetMaintenancesController.Close). Guard order verbatim: NOT_FOUND → scope (403 Forbid —
/// verbatim trap (a) reversal: reads AND Close/Inspect use 403, only Update uses 404) →
/// MAINTENANCE_ALREADY_CLOSED → MAINTENANCE_NOT_COMPLETED_YET (CompletionDate required) →
/// MAINTENANCE_NOT_INSPECTED_YET (InspectedById required — inspection is an independent pre-close
/// approval step; workflow: Hoàn thành → Kiểm tra → Đóng) → freeze (IsClosed/ClosedAt/ClosedById).
/// Anyone who may edit the record (same company or Superuser) may close it — floater records
/// (CompanyId == Guid.Empty) are manageable by everyone. ILoggableCommand only (single SaveChanges
/// + log in the ambient tx).
/// </summary>
public record CloseMaintenanceCommand(Guid Id, Guid CurrentUserId)
    : IRequest<CloseMaintenanceResult>, ILoggableCommand<CloseMaintenanceResult>
{
    public ActionLogEntry? BuildLogEntry(CloseMaintenanceResult response)
    {
        if (!response.Success)
            return null;

        return new ActionLogEntry
        {
            ItemType = ItemType.AssetMaintenance,
            ItemId = Id,
            ActionType = ActionType.Close,
            CreatedBy = CurrentUserId,
            CompanyId = response.CompanyId,
            Note = response.Note
        };
    }
}

public class CloseMaintenanceCommandHandler : IRequestHandler<CloseMaintenanceCommand, CloseMaintenanceResult>
{
    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _companyScope;

    public CloseMaintenanceCommandHandler(IApplicationDbContext context, ICompanyScopeService companyScope)
    {
        _context = context;
        _companyScope = companyScope;
    }

    public async Task<CloseMaintenanceResult> Handle(CloseMaintenanceCommand request, CancellationToken cancellationToken)
    {
        var m = await _context.AssetMaintenances
            .FirstOrDefaultAsync(x => x.Id == request.Id && x.DeletedAt == null, cancellationToken);
        if (m == null)
            return new CloseMaintenanceResult(false, "Maintenance not found.", "NOT_FOUND");

        // Same edit rights as Update: a regular user may close records of their own company
        // (floater records are manageable by everyone). Out-of-scope → 403 (NOT 404 — verbatim).
        var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        if (userCompanyId.HasValue && m.CompanyId != userCompanyId.Value && m.CompanyId != Guid.Empty)
            return new CloseMaintenanceResult(false, "Forbidden.", "FORBIDDEN");

        if (m.IsClosed)
            return new CloseMaintenanceResult(false, "Bản ghi đã đóng.", "MAINTENANCE_ALREADY_CLOSED");
        if (m.CompletionDate == null)
            return new CloseMaintenanceResult(false, "Cần nhập Ngày hoàn thành trước khi đóng bảo trì.", "MAINTENANCE_NOT_COMPLETED_YET");
        // Inspection is an independent pre-close approval step — a completed-but-not-yet-inspected
        // record cannot be closed (workflow: Hoàn thành → Kiểm tra → Đóng).
        if (m.InspectedById == null)
            return new CloseMaintenanceResult(false, "Cần kiểm tra trước khi đóng bảo trì.", "MAINTENANCE_NOT_INSPECTED_YET");

        m.IsClosed = true;
        m.ClosedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
        m.ClosedById = request.CurrentUserId;
        m.UpdatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);

        await _context.SaveChangesAsync(cancellationToken);

        return new CloseMaintenanceResult(true, "Đã đóng bảo trì.",
            MaintenanceId: m.Id, CompanyId: m.CompanyId,
            IsClosed: m.IsClosed, ClosedAt: m.ClosedAt, ClosedById: m.ClosedById,
            Note: $"Đóng bảo trì \"{m.Title}\"");
    }
}

/// <summary>
/// [Subtask D] POST api/v1/maintenances/{id}/inspect (extracted verbatim from
/// AssetMaintenancesController.Inspect). Marks the maintenance as inspected — the independent
/// approval step BETWEEN "Hoàn thành" (CompletionDate) and "Đóng" (Close). Guard order verbatim:
/// NOT_FOUND → scope 403 (verbatim trap (a)) → MAINTENANCE_CLOSED → MAINTENANCE_NOT_COMPLETED_YET.
/// The step may be REPEATED (overwrites InspectedBy/InspectedAt, i.e. "inspect again") and does
/// NOT lock anything — only Close freezes the record. ILoggableCommand only.
/// </summary>
public record InspectMaintenanceCommand(Guid Id, Guid CurrentUserId)
    : IRequest<InspectMaintenanceResult>, ILoggableCommand<InspectMaintenanceResult>
{
    public ActionLogEntry? BuildLogEntry(InspectMaintenanceResult response)
    {
        if (!response.Success)
            return null;

        return new ActionLogEntry
        {
            ItemType = ItemType.AssetMaintenance,
            ItemId = Id,
            ActionType = ActionType.Inspect,
            CreatedBy = CurrentUserId,
            CompanyId = response.CompanyId,
            Note = response.Note
        };
    }
}

public class InspectMaintenanceCommandHandler : IRequestHandler<InspectMaintenanceCommand, InspectMaintenanceResult>
{
    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _companyScope;

    public InspectMaintenanceCommandHandler(IApplicationDbContext context, ICompanyScopeService companyScope)
    {
        _context = context;
        _companyScope = companyScope;
    }

    public async Task<InspectMaintenanceResult> Handle(InspectMaintenanceCommand request, CancellationToken cancellationToken)
    {
        var m = await _context.AssetMaintenances
            .FirstOrDefaultAsync(x => x.Id == request.Id && x.DeletedAt == null, cancellationToken);
        if (m == null)
            return new InspectMaintenanceResult(false, "Maintenance not found.", "NOT_FOUND");

        // Same edit rights as Close/Update: a regular user may inspect records of their own company
        // (floater records are manageable by everyone). Out-of-scope → 403 (verbatim).
        var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        if (userCompanyId.HasValue && m.CompanyId != userCompanyId.Value && m.CompanyId != Guid.Empty)
            return new InspectMaintenanceResult(false, "Forbidden.", "FORBIDDEN");

        if (m.IsClosed)
            return new InspectMaintenanceResult(false, "Bản ghi đã đóng, không thể kiểm tra.", "MAINTENANCE_CLOSED");
        if (m.CompletionDate == null)
            return new InspectMaintenanceResult(false, "Cần nhập Ngày hoàn thành trước khi kiểm tra bảo trì.", "MAINTENANCE_NOT_COMPLETED_YET");

        m.InspectedById = request.CurrentUserId;
        m.InspectedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
        m.UpdatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);

        await _context.SaveChangesAsync(cancellationToken);

        return new InspectMaintenanceResult(true, "Đã đánh dấu đã kiểm tra.",
            MaintenanceId: m.Id, CompanyId: m.CompanyId,
            InspectedById: m.InspectedById, InspectedAt: m.InspectedAt,
            Note: $"Kiểm tra bảo trì \"{m.Title}\"");
    }
}

/// <summary>
/// [Subtask D] POST api/v1/maintenances/{id}/reopen (extracted verbatim from
/// AssetMaintenancesController.Reopen). Reopen breaks the audit lock — Superuser only (consistent
/// with delete rights). Guard order verbatim: superuser-gate FIRST (FORBIDDEN, BEFORE the lookup —
/// no company-scope branch, the superuser requirement subsumes it) → NOT_FOUND →
/// MAINTENANCE_NOT_CLOSED → unfreeze (IsClosed = false; ClosedAt/ClosedById KEPT — they record the
/// most recent close, each close/reopen cycle is itself audited in ActionLog). ILoggableCommand only.
/// </summary>
public record ReopenMaintenanceCommand(Guid Id, Guid CurrentUserId)
    : IRequest<ReopenMaintenanceResult>, ILoggableCommand<ReopenMaintenanceResult>
{
    public ActionLogEntry? BuildLogEntry(ReopenMaintenanceResult response)
    {
        if (!response.Success)
            return null;

        return new ActionLogEntry
        {
            ItemType = ItemType.AssetMaintenance,
            ItemId = Id,
            ActionType = ActionType.Reopen,
            CreatedBy = CurrentUserId,
            CompanyId = response.CompanyId,
            Note = response.Note
        };
    }
}

public class ReopenMaintenanceCommandHandler : IRequestHandler<ReopenMaintenanceCommand, ReopenMaintenanceResult>
{
    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _companyScope;

    public ReopenMaintenanceCommandHandler(IApplicationDbContext context, ICompanyScopeService companyScope)
    {
        _context = context;
        _companyScope = companyScope;
    }

    public async Task<ReopenMaintenanceResult> Handle(ReopenMaintenanceCommand request, CancellationToken cancellationToken)
    {
        // Reopen breaks the audit lock — Superuser only (consistent with delete rights).
        if (!_companyScope.IsSuperUser())
            return new ReopenMaintenanceResult(false, "Forbidden.", "FORBIDDEN");

        var m = await _context.AssetMaintenances
            .FirstOrDefaultAsync(x => x.Id == request.Id && x.DeletedAt == null, cancellationToken);
        if (m == null)
            return new ReopenMaintenanceResult(false, "Maintenance not found.", "NOT_FOUND");
        if (!m.IsClosed)
            return new ReopenMaintenanceResult(false, "Bản ghi chưa đóng.", "MAINTENANCE_NOT_CLOSED");

        m.IsClosed = false;
        // Keep ClosedAt/ClosedById — they record the most recent close (each cycle is audited in ActionLog).
        m.UpdatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);

        await _context.SaveChangesAsync(cancellationToken);

        return new ReopenMaintenanceResult(true, "Đã mở lại bảo trì.",
            MaintenanceId: m.Id, CompanyId: m.CompanyId,
            IsClosed: m.IsClosed, ClosedAt: m.ClosedAt, ClosedById: m.ClosedById,
            Note: $"Mở lại bảo trì \"{m.Title}\" (superuser)");
    }
}
