using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Locations.Commands;

/// <summary>
/// [Giai đoạn 2] DELETE /api/v1/locations/{id} (extracted from AdminController.DeleteLocation).
/// Rule order verbatim: 404 → scope 404 → tree guard (has-children → 400 WITHOUT error_code) →
/// LOCATION_IN_USE guard (components/assets/consumables/accessories/users — incl. soft-deleted
/// inventory) → remove. Self-referencing tree guard kept verbatim (no cycle-check — parity).
/// ILoggableCommand only (no output-cache on locations — no ICacheInvalidatingCommand).
/// </summary>
public record DeleteLocationCommand(Guid Id, Guid CurrentUserId)
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
            ActionType = ActionType.Delete,
            CreatedBy = CurrentUserId,
            CompanyId = response.CompanyId,
            Note = $"Xóa địa điểm \"{response.Name}\""
        };
    }
}

public class DeleteLocationCommandHandler : IRequestHandler<DeleteLocationCommand, LocationResult>
{
    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _companyScope;

    public DeleteLocationCommandHandler(IApplicationDbContext context, ICompanyScopeService companyScope)
    {
        _context = context;
        _companyScope = companyScope;
    }

    public async Task<LocationResult> Handle(DeleteLocationCommand request, CancellationToken cancellationToken)
    {
        var l = await _context.Locations.FindAsync(request.Id);
        if (l == null)
            return new LocationResult(false, "Not found.", "NOT_FOUND");

        // Company scoping: a regular user may only delete locations of their own company (or floater).
        var userCompanyIdDelete = await _companyScope.GetCurrentUserCompanyIdAsync();
        if (userCompanyIdDelete.HasValue && l.CompanyId.HasValue && l.CompanyId.Value != userCompanyIdDelete.Value)
            return new LocationResult(false, "Not found.", "NOT_FOUND");

        var hasChildren = await _context.Locations.AnyAsync(x => x.ParentId == request.Id, cancellationToken);
        if (hasChildren)
            return new LocationResult(false, "Không thể xóa địa điểm có địa điểm con.");

        // Delete guard: a location referenced by inventory/users cannot be deleted.
        if (await _context.Components.IgnoreQueryFilters().AnyAsync(c => c.LocationId == request.Id, cancellationToken)
            || await _context.Assets.IgnoreQueryFilters().AnyAsync(a => a.LocationId == request.Id, cancellationToken)
            || await _context.Consumables.AnyAsync(x => x.LocationId == request.Id, cancellationToken)
            || await _context.Accessories.AnyAsync(a => a.LocationId == request.Id, cancellationToken)
            || await _context.Users.AnyAsync(u => u.LocationId == request.Id, cancellationToken))
            return new LocationResult(false,
                "Địa điểm đang được tài sản/vật tư/phụ kiện/người dùng sử dụng — không thể xóa.",
                "LOCATION_IN_USE");

        _context.Locations.Remove(l);
        await _context.SaveChangesAsync(cancellationToken);

        return new LocationResult(true, "Deleted.", LocationId: request.Id, Name: l.Name, CompanyId: l.CompanyId);
    }
}
