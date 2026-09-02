using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Depreciations.Queries;

/// <summary>
/// [Giai đoạn 2-cuối] GET /api/v1/depreciations (extracted from AdminController.GetDepreciations —
/// section cuối cùng của AdminController). Reference data — NOT company-scoped (Depreciation has
/// no CompanyId), raw entity list ordered by Name. GET-only resource: no Create/Update/Delete
/// endpoints have ever existed → no Commands, no Behaviors (no log/cache markers apply).
/// </summary>
public record ListDepreciationsQuery : IRequest<IReadOnlyList<Depreciation>>;

public class ListDepreciationsQueryHandler : IRequestHandler<ListDepreciationsQuery, IReadOnlyList<Depreciation>>
{
    private readonly IApplicationDbContext _context;

    public ListDepreciationsQueryHandler(IApplicationDbContext context) => _context = context;

    public async Task<IReadOnlyList<Depreciation>> Handle(ListDepreciationsQuery request, CancellationToken cancellationToken)
    {
        var list = await _context.Depreciations.AsNoTracking().OrderBy(d => d.Name).ToListAsync(cancellationToken);
        return list;
    }
}
