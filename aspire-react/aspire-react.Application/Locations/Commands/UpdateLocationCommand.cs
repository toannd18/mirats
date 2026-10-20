using aspire_react.Server.Application.Common.ActionLogs;
using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Application.Common.Validation;
using Microsoft.EntityFrameworkCore;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using MediatR;

namespace aspire_react.Server.Application.Locations.Commands;

/// <summary>
/// [Giai đoạn 2] PUT /api/v1/locations/{id} (extracted from AdminController.UpdateLocation).
/// Rule order verbatim: 404 → scope 404 → patch assigns. Patch semantics (Task M2): ALL 9 fields
/// conditional (`is not null` / non-whitespace for Name) — this section was ALREADY patch-safe.
/// Self-referencing ParentId assignment has NO cycle-check (pre-migration had none — parity).
/// LogMeta changes-snapshot (9 fields) built in the handler, carried to ActionLogBehavior.
/// </summary>
public record UpdateLocationCommand(
    Guid Id,
    string? Name,
    Guid? ParentId,
    Guid? CompanyId,
    Guid? ManagerId,
    string? Address,
    string? City,
    string? State,
    string? Country,
    string? Zip,
    Guid CurrentUserId)
    : IRequest<LocationResult>, ILoggableCommand<LocationResult>
{
    public ActionLogEntry? BuildLogEntry(LocationResult response)
    {
        if (!response.Success)
            return null;

        return new ActionLogEntry
        {
            ItemType = ItemType.Location,
            ItemId = Id,
            ActionType = ActionType.Update,
            CreatedBy = CurrentUserId,
            CompanyId = response.CompanyId,
            LogMeta = response.LogMeta,
            Note = response.Note
        };
    }
}

public class UpdateLocationCommandHandler : IRequestHandler<UpdateLocationCommand, LocationResult>
{
    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _companyScope;

    public UpdateLocationCommandHandler(IApplicationDbContext context, ICompanyScopeService companyScope)
    {
        _context = context;
        _companyScope = companyScope;
    }

    public async Task<LocationResult> Handle(UpdateLocationCommand request, CancellationToken cancellationToken)
    {
        var l = await _context.Locations.FindAsync(request.Id);
        if (l == null)
            return new LocationResult(false, "Not found.", "NOT_FOUND");

        // Company scoping: a regular user may only edit locations of their own company (or floater).
        var userCompanyIdUpdate = await _companyScope.GetCurrentUserCompanyIdAsync();
        if (userCompanyIdUpdate.HasValue && l.CompanyId.HasValue && l.CompanyId.Value != userCompanyIdUpdate.Value)
            return new LocationResult(false, "Not found.", "NOT_FOUND");

        // Patch semantics (Task M2): only fields explicitly sent are applied (absent → keep current).
        // [FIX-GUID-EMPTY phần 2 — 2026-10-20] FK TÙY CHỌN: vắng = giữ; Guid.Empty = xóa liên kết (null);
        // id thật phải TỒN TẠI (trước đây ⇒ Postgres 23503 ⇒ 500).
        var newParentId = OptionalForeignKey.Cleared(request.ParentId);
        var newCompanyId = OptionalForeignKey.Cleared(request.CompanyId);
        var newManagerId = OptionalForeignKey.Cleared(request.ManagerId);
        if (newParentId is not null
            && !await _context.Locations.AnyAsync(x => x.Id == newParentId, cancellationToken))
            return new LocationResult(false, OptionalForeignKey.NotFoundMessage("parentId"), "RESOURCE_NOT_FOUND");
        if (newCompanyId is not null
            && !await _context.Companies.AnyAsync(x => x.Id == newCompanyId, cancellationToken))
            return new LocationResult(false, OptionalForeignKey.NotFoundMessage("companyId"), "RESOURCE_NOT_FOUND");
        if (newManagerId is not null
            && !await _context.Users.AnyAsync(x => x.Id == newManagerId, cancellationToken))
            return new LocationResult(false, OptionalForeignKey.NotFoundMessage("managerId"), "RESOURCE_NOT_FOUND");
        var before = new { l.Name, l.ParentId, l.CompanyId, l.ManagerId, l.Address, l.City, l.State, l.Country, l.Zip };
        // [AUDIT-LOG B6a] helper dùng chung: tên field snake_case, CHỈ field thực sự đổi.
        var changes = new ActionLogChanges()
            .Track("name", before.Name)
            .Track("parentId", before.ParentId)
            .Track("companyId", before.CompanyId)
            .Track("managerId", before.ManagerId)
            .Track("address", before.Address)
            .Track("city", before.City)
            .Track("state", before.State)
            .Track("country", before.Country)
            .Track("zip", before.Zip);
        if (!string.IsNullOrWhiteSpace(request.Name)) l.Name = request.Name;
        if (request.ParentId is not null) l.ParentId = newParentId;
        if (request.CompanyId is not null) l.CompanyId = newCompanyId;
        if (request.ManagerId is not null) l.ManagerId = newManagerId;
        if (request.Address is not null) l.Address = request.Address;
        if (request.City is not null) l.City = request.City;
        if (request.State is not null) l.State = request.State;
        if (request.Country is not null) l.Country = request.Country;
        if (request.Zip is not null) l.Zip = request.Zip;
        await _context.SaveChangesAsync(cancellationToken);

        var logMeta = changes
            .Set("name", l.Name)
            .Set("parentId", l.ParentId)
            .Set("companyId", l.CompanyId)
            .Set("managerId", l.ManagerId)
            .Set("address", l.Address)
            .Set("city", l.City)
            .Set("state", l.State)
            .Set("country", l.Country)
            .Set("zip", l.Zip)
            .ToLogMeta();

        return new LocationResult(
            true, "Updated.",
            LocationId: l.Id, Name: l.Name, CompanyId: l.CompanyId,
            LogMeta: logMeta, Note: $"Cập nhật địa điểm \"{l.Name}\"");
    }
}
