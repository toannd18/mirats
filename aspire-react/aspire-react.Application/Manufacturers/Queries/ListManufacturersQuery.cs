using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Manufacturers.Queries;

/// <summary>
/// [Giai đoạn 2] GET /api/v1/manufacturers (extracted from AdminController.GetManufacturers).
/// Reference data — NOT company-scoped (no CompanyId, by design), identical to pre-migration:
/// raw entity list ordered by Code. OutputCache attribute stays on the controller action.
/// </summary>
public record ListManufacturersQuery : IRequest<IReadOnlyList<Manufacturer>>;

public class ListManufacturersQueryHandler : IRequestHandler<ListManufacturersQuery, IReadOnlyList<Manufacturer>>
{
    private readonly IApplicationDbContext _context;

    public ListManufacturersQueryHandler(IApplicationDbContext context) => _context = context;

    public async Task<IReadOnlyList<Manufacturer>> Handle(ListManufacturersQuery request, CancellationToken cancellationToken)
    {
        var list = await _context.Manufacturers.AsNoTracking().OrderBy(m => m.Code).ToListAsync(cancellationToken);
        return list;
    }
}
