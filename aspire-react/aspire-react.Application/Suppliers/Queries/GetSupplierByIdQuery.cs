using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Suppliers.Queries;

/// <summary>
/// [Giai đoạn 2] GET /api/v1/suppliers/{id} — NEW endpoint (was missing pre-migration),
/// added per playbook §6.5 (approved pattern). No company-scoping — reference data.
/// </summary>
public record GetSupplierByIdQuery(Guid Id) : IRequest<Supplier?>;

public class GetSupplierByIdQueryHandler : IRequestHandler<GetSupplierByIdQuery, Supplier?>
{
    private readonly IApplicationDbContext _context;

    public GetSupplierByIdQueryHandler(IApplicationDbContext context) => _context = context;

    public async Task<Supplier?> Handle(GetSupplierByIdQuery request, CancellationToken cancellationToken)
        => await _context.Suppliers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == request.Id, cancellationToken);
}
