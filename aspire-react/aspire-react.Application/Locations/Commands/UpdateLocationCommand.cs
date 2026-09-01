using System.Text.Json;
using aspire_react.Server.Application.Common.Interfaces;
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
        var before = new { l.Name, l.ParentId, l.CompanyId, l.ManagerId, l.Address, l.City, l.State, l.Country, l.Zip };
        if (!string.IsNullOrWhiteSpace(request.Name)) l.Name = request.Name;
        if (request.ParentId is not null) l.ParentId = request.ParentId;
        if (request.CompanyId is not null) l.CompanyId = request.CompanyId;
        if (request.ManagerId is not null) l.ManagerId = request.ManagerId;
        if (request.Address is not null) l.Address = request.Address;
        if (request.City is not null) l.City = request.City;
        if (request.State is not null) l.State = request.State;
        if (request.Country is not null) l.Country = request.Country;
        if (request.Zip is not null) l.Zip = request.Zip;
        await _context.SaveChangesAsync(cancellationToken);

        var logMeta = JsonSerializer.Serialize(new
        {
            changes = new
            {
                name = new { old = before.Name, @new = l.Name },
                parentId = new { old = before.ParentId, @new = l.ParentId },
                companyId = new { old = before.CompanyId, @new = l.CompanyId },
                managerId = new { old = before.ManagerId, @new = l.ManagerId },
                address = new { old = before.Address, @new = l.Address },
                city = new { old = before.City, @new = l.City },
                state = new { old = before.State, @new = l.State },
                country = new { old = before.Country, @new = l.Country },
                zip = new { old = before.Zip, @new = l.Zip }
            }
        });

        return new LocationResult(
            true, "Updated.",
            LocationId: l.Id, Name: l.Name, CompanyId: l.CompanyId,
            LogMeta: logMeta, Note: $"Cập nhật địa điểm \"{l.Name}\"");
    }
}
