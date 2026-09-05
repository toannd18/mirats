using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using MediatR;

namespace aspire_react.Server.Application.Locations.Commands;

/// <summary>
/// [Giai đoạn 2] POST /api/v1/locations (extracted from AdminController.CreateLocation).
/// [BUG-G FIX 2026-09-05] Company-scoping ADDED — Task L2 pattern (identical to
/// CreateDepartmentCommand): a regular user may only create a location for their OWN company (or
/// leave CompanyId null → floater); superuser may target any company. Mismatch → 400
/// COMPANY_MISMATCH, checked FIRST, before any entity is added (blocked requests create neither a
/// row nor an ActionLog — BuildLogEntry returns null on failure). Known accepted limitation
/// (BACKLOG BUG-G, same as Department): no company-existence check (nonexistent ids pass) and no
/// empty-name/dup-name check — deliberately OUT of this fix's scope.
/// ILoggableCommand only: locations have NO output-cache (no [OutputCache] on GET /locations
/// pre-migration) — ICacheInvalidatingCommand deliberately NOT implemented.
/// </summary>
public record CreateLocationCommand(
    string Name,
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
            ItemId = response.LocationId!.Value,
            ActionType = ActionType.Create,
            CreatedBy = CurrentUserId,
            CompanyId = CompanyId,
            Note = $"Tạo địa điểm \"{Name}\""
        };
    }
}

public class CreateLocationCommandHandler : IRequestHandler<CreateLocationCommand, LocationResult>
{
    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _companyScope;

    public CreateLocationCommandHandler(IApplicationDbContext context, ICompanyScopeService companyScope)
    {
        _context = context;
        _companyScope = companyScope;
    }

    public async Task<LocationResult> Handle(CreateLocationCommand request, CancellationToken cancellationToken)
    {
        // [BUG-G FIX] Company-scoping on CREATE — Task L2 pattern (Department), check FIRST:
        // regular user (scope = own company) may only create for their company or floater;
        // superuser (scope null) bypasses. Out-of-scope create → 400 COMPANY_MISMATCH.
        var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        if (userCompanyId.HasValue && request.CompanyId.HasValue && request.CompanyId.Value != userCompanyId.Value)
            return new LocationResult(false, "Bạn chỉ được tạo địa điểm cho công ty của mình.", "COMPANY_MISMATCH");

        var l = new Location
        {
            Name = request.Name,
            ParentId = request.ParentId,
            CompanyId = request.CompanyId,
            ManagerId = request.ManagerId,
            Address = request.Address,
            City = request.City,
            State = request.State,
            Country = request.Country,
            Zip = request.Zip
        };
        _context.Locations.Add(l);
        await _context.SaveChangesAsync(cancellationToken);

        return new LocationResult(true, "Created.", LocationId: l.Id, Name: l.Name, CompanyId: l.CompanyId);
    }
}
