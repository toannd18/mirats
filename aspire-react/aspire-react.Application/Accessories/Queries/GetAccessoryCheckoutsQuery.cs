using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using aspire_react.Server.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Accessories.Queries;

/// <summary>
/// [FIX C / N2 remainder 2026-10-02] GET /api/v1/accessories/{id}/checkouts — the last action still
/// running EF directly inside AccessoriesController (after N2 migrated List/GetById/Update).
/// Moved verbatim: same company-scope visibility rule (out-of-scope → 404 hide-existence), same
/// projection, same ordering (CheckedOutAt DESC) and the same per-row target-name resolution
/// (user display name falls back to Username; department/location/systemPosition → Name).
/// </summary>
public record GetAccessoryCheckoutsQuery(Guid AccessoryId) : IRequest<GetAccessoryCheckoutsResult>;

public sealed record AccessoryCheckoutRowDto(
    Guid Id,
    Guid AccessoryId,
    AccessoryCheckoutType CheckoutType,
    Guid TargetId,
    string? TargetName,
    int AssignedQty,
    int ReturnedQty,
    int RemainingOut,
    string? Note,
    DateTime CheckedOutAt,
    Guid? CreatedByUserId,
    string? CreatedByName,
    string? CreatedByFirstName,
    string? CreatedByLastName);

/// <summary><c>Found=false</c> → the accessory does not exist OR is outside the caller's company
/// scope: both answer 404 "Accessory not found." (hide-existence, verbatim).</summary>
public sealed record GetAccessoryCheckoutsResult(bool Found, IReadOnlyList<AccessoryCheckoutRowDto> Items);

public class GetAccessoryCheckoutsQueryHandler : IRequestHandler<GetAccessoryCheckoutsQuery, GetAccessoryCheckoutsResult>
{
    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _companyScope;

    public GetAccessoryCheckoutsQueryHandler(IApplicationDbContext context, ICompanyScopeService companyScope)
    {
        _context = context;
        _companyScope = companyScope;
    }

    public async Task<GetAccessoryCheckoutsResult> Handle(
        GetAccessoryCheckoutsQuery request,
        CancellationToken cancellationToken)
    {
        // Company scoping: a regular user may only view the checkouts of an accessory in their company.
        var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        var visible = await _context.Accessories.AsNoTracking()
            .AnyAsync(a => a.Id == request.AccessoryId
                           && (userCompanyId == null || a.CompanyId == null || a.CompanyId == userCompanyId.Value),
                cancellationToken);
        if (!visible) return new GetAccessoryCheckoutsResult(false, Array.Empty<AccessoryCheckoutRowDto>());

        var checkouts = await _context.AccessoryCheckouts
            .Include(ch => ch.CreatedByUser)
            .Where(ch => ch.AccessoryId == request.AccessoryId)
            .OrderByDescending(ch => ch.CheckedOutAt)
            .Select(ch => new
            {
                ch.Id,
                ch.AccessoryId,
                ch.CheckoutType,
                ch.TargetId,
                ch.AssignedQty,
                ch.ReturnedQty,
                RemainingOut = ch.AssignedQty - ch.ReturnedQty,
                ch.Note,
                ch.CheckedOutAt,
                CreatedByUserId = ch.CreatedByUserId,
                CreatedByName = ch.CreatedByUser != null ? ch.CreatedByUser.Username : null,
                CreatedByFirstName = ch.CreatedByUser != null ? ch.CreatedByUser.FirstName : null,
                CreatedByLastName = ch.CreatedByUser != null ? ch.CreatedByUser.LastName : null
            })
            .ToListAsync(cancellationToken);

        // Target-name resolution stays per-row exactly as before (verbatim behaviour).
        var items = new List<AccessoryCheckoutRowDto>(checkouts.Count);
        foreach (var ch in checkouts)
        {
            var targetName = await ResolveTargetNameAsync(ch.CheckoutType, ch.TargetId, cancellationToken);
            items.Add(new AccessoryCheckoutRowDto(
                ch.Id,
                ch.AccessoryId,
                ch.CheckoutType,
                ch.TargetId,
                targetName,
                ch.AssignedQty,
                ch.ReturnedQty,
                ch.RemainingOut,
                ch.Note,
                ch.CheckedOutAt,
                ch.CreatedByUserId,
                ch.CreatedByName,
                ch.CreatedByFirstName,
                ch.CreatedByLastName));
        }

        return new GetAccessoryCheckoutsResult(true, items);
    }

    private async Task<string?> ResolveTargetNameAsync(
        AccessoryCheckoutType type,
        Guid targetId,
        CancellationToken cancellationToken)
    {
        switch (type)
        {
            case AccessoryCheckoutType.User:
                return await _context.Users.Where(u => u.Id == targetId)
                    .AsNoTracking()
                    .Select(u => (u.FirstName + " " + u.LastName).Trim() != ""
                        ? (u.FirstName + " " + u.LastName).Trim()
                        : u.Username)
                    .FirstOrDefaultAsync(cancellationToken);
            case AccessoryCheckoutType.Department:
                return await _context.Departments.Where(d => d.Id == targetId).AsNoTracking()
                    .Select(d => d.Name).FirstOrDefaultAsync(cancellationToken);
            case AccessoryCheckoutType.Location:
                return await _context.Locations.Where(l => l.Id == targetId).AsNoTracking()
                    .Select(l => l.Name).FirstOrDefaultAsync(cancellationToken);
            case AccessoryCheckoutType.SystemPosition:
                return await _context.SystemPositions.Where(sp => sp.Id == targetId).AsNoTracking()
                    .Select(sp => sp.Name).FirstOrDefaultAsync(cancellationToken);
            default:
                return null;
        }
    }
}
