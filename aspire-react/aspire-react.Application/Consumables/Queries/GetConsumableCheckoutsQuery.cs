using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Consumables.Queries;

public record ConsumableCheckoutRowDto(
    Guid Id, Guid ConsumableId, Guid UserId, string UserName, string FirstName, string LastName,
    string? CreatedByName, string? CreatedByFirstName, string? CreatedByLastName,
    int Quantity, string? Note, DateTime CreatedAt);

/// <summary>
/// [Giai đoạn 3 — Nặng] GET /api/v1/consumables/{id}/checkouts (extracted from
/// ConsumablesController.GetCheckouts). Scope → 404; checkout rows with user + createdBy
/// resolution verbatim (CreatedAt maps from CheckedOutAt — verbatim).
/// </summary>
public record GetConsumableCheckoutsQuery(Guid Id) : IRequest<IReadOnlyList<ConsumableCheckoutRowDto>?>;

public class GetConsumableCheckoutsQueryHandler : IRequestHandler<GetConsumableCheckoutsQuery, IReadOnlyList<ConsumableCheckoutRowDto>?>
{
    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _companyScope;

    public GetConsumableCheckoutsQueryHandler(IApplicationDbContext context, ICompanyScopeService companyScope)
    {
        _context = context;
        _companyScope = companyScope;
    }

    public async Task<IReadOnlyList<ConsumableCheckoutRowDto>?> Handle(GetConsumableCheckoutsQuery request, CancellationToken cancellationToken)
    {
        // Company scoping: a regular user may only view the checkouts of a consumable in their company.
        var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        var visible = await _context.Consumables.AsNoTracking()
            .AnyAsync(c => c.Id == request.Id && (userCompanyId == null || c.CompanyId == null || c.CompanyId == userCompanyId.Value), cancellationToken);
        if (!visible) return null;

        var checkouts = await _context.ConsumableCheckouts
            .Include(ch => ch.User)
            .Include(ch => ch.CreatedByUser)
            .Where(ch => ch.ConsumableId == request.Id)
            .OrderByDescending(ch => ch.CheckedOutAt)
            .Select(ch => new ConsumableCheckoutRowDto(
                ch.Id, ch.ConsumableId, ch.UserId, ch.User.Username, ch.User.FirstName, ch.User.LastName,
                ch.CreatedByUser != null ? ch.CreatedByUser.Username : null,
                ch.CreatedByUser != null ? ch.CreatedByUser.FirstName : null,
                ch.CreatedByUser != null ? ch.CreatedByUser.LastName : null,
                ch.Quantity, ch.Note, ch.CheckedOutAt))
            .ToListAsync(cancellationToken);

        return checkouts;
    }
}
