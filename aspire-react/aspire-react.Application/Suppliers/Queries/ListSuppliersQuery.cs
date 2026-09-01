using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Suppliers.Queries;

/// <summary>
/// [Giai đoạn 2] GET /api/v1/suppliers (extracted from AdminController.GetSuppliers).
/// Reference data — NOT company-scoped (no CompanyId, by design), raw entity list ordered by
/// Code. OutputCache attribute stays on the controller action.
/// </summary>
public record ListSuppliersQuery : IRequest<IReadOnlyList<Supplier>>;

public class ListSuppliersQueryHandler : IRequestHandler<ListSuppliersQuery, IReadOnlyList<Supplier>>
{
    private readonly IApplicationDbContext _context;

    public ListSuppliersQueryHandler(IApplicationDbContext context) => _context = context;

    public async Task<IReadOnlyList<Supplier>> Handle(ListSuppliersQuery request, CancellationToken cancellationToken)
    {
        var list = await _context.Suppliers.AsNoTracking().OrderBy(s => s.Code).ToListAsync(cancellationToken);
        return list;
    }
}
