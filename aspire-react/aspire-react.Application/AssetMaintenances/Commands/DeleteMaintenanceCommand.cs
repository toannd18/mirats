using System.Text.Encodings.Web;
using System.Text.Json;
using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.AssetMaintenances.Commands;

public record DeleteMaintenanceResult(
    bool Success,
    string Message,
    string? ErrorCode = null,
    Guid? MaintenanceId = null,
    Guid? CompanyId = null,
    string? LogMeta = null,
    string? Note = null);

/// <summary>
/// [Subtask C] DELETE api/v1/maintenances/{id} (extracted verbatim from
/// AssetMaintenancesController.Delete). Order verbatim: superuser-gate FIRST
/// (non-superuser → FORBIDDEN, BEFORE the existence lookup — no company-scope branch, the
/// superuser requirement subsumes it) → NOT_FOUND → soft-delete (DeletedAt, SpecifiedKind
/// Unspecified) → full-content LogMeta → thin Delete log. NO closed-guard: a superuser may
/// delete even a closed record (only Update/Inspect refuse closed ones). ILoggableCommand only.
/// Escaping trap (user note 2): LogMeta MUST serialize with UnsafeRelaxedJsonEscaping (keeps
/// Vietnamese readable) — ported exactly; the existing
/// DeleteMaintenance_Superuser_Succeeds_AndLogsContent test (Contains "Sửa nguồn") enforces it.
/// </summary>
public record DeleteMaintenanceCommand(Guid Id, Guid CurrentUserId)
    : IRequest<DeleteMaintenanceResult>, ILoggableCommand<DeleteMaintenanceResult>
{
    public ActionLogEntry? BuildLogEntry(DeleteMaintenanceResult response)
    {
        if (!response.Success)
            return null;

        return new ActionLogEntry
        {
            ItemType = ItemType.AssetMaintenance,
            ItemId = Id,
            ActionType = ActionType.Delete,
            CreatedBy = CurrentUserId,
            CompanyId = response.CompanyId,
            LogMeta = response.LogMeta,
            Note = response.Note
        };
    }
}

public class DeleteMaintenanceCommandHandler : IRequestHandler<DeleteMaintenanceCommand, DeleteMaintenanceResult>
{
    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _companyScope;

    public DeleteMaintenanceCommandHandler(IApplicationDbContext context, ICompanyScopeService companyScope)
    {
        _context = context;
        _companyScope = companyScope;
    }

    public async Task<DeleteMaintenanceResult> Handle(DeleteMaintenanceCommand request, CancellationToken cancellationToken)
    {
        // Only Superuser may delete maintenance records (history/audit data).
        if (!_companyScope.IsSuperUser())
            return new DeleteMaintenanceResult(false, "Forbidden.", "FORBIDDEN");

        var m = await _context.AssetMaintenances
            .FirstOrDefaultAsync(x => x.Id == request.Id && x.DeletedAt == null, cancellationToken);
        if (m == null)
            return new DeleteMaintenanceResult(false, "Maintenance not found.", "NOT_FOUND");

        m.DeletedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
        m.UpdatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);

        // Log the deletion with the FULL record content (incl. snapshot) so the audit trail
        // keeps enough to reconstruct what was removed.
        var meta = JsonSerializer.Serialize(new
        {
            title = m.Title,
            type = m.Type.ToString(),
            startDate = m.StartDate,
            completionDate = m.CompletionDate,
            cost = m.Cost,
            supplierId = m.SupplierId,
            snapshotSystemInfoId = m.SnapshotSystemInfoId,
            snapshotSystemInfoName = m.SnapshotSystemInfoName,
            snapshotSystemPositionId = m.SnapshotSystemPositionId,
            snapshotSystemPositionName = m.SnapshotSystemPositionName,
            snapshotLocationId = m.SnapshotLocationId,
            snapshotLocationName = m.SnapshotLocationName,
            snapshotAssignedUserId = m.SnapshotAssignedUserId,
            snapshotAssignedUserName = m.SnapshotAssignedUserName,
            snapshotDepartmentId = m.SnapshotDepartmentId,
            snapshotDepartmentName = m.SnapshotDepartmentName
        }, new JsonSerializerOptions
        {
            // Keep Vietnamese/diacritics readable inside the audit trail.
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });

        await _context.SaveChangesAsync(cancellationToken);

        return new DeleteMaintenanceResult(true, "Đã xóa bảo trì.",
            MaintenanceId: m.Id, CompanyId: m.CompanyId, LogMeta: meta,
            Note: $"Xóa bảo trì \"{m.Title}\" (superuser)");
    }
}
