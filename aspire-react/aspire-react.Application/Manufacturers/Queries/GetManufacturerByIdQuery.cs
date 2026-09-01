using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Manufacturers.Queries;

/// <summary>
/// [Giai đoạn 2] GET /api/v1/manufacturers/{id} — NEW endpoint (was missing pre-migration),
/// added per playbook §6.5 (approved pattern). No company-scoping — reference data.
/// </summary>
public record GetManufacturerByIdQuery(Guid Id) : IRequest<Manufacturer?>;

public class GetManufacturerByIdQueryHandler : IRequestHandler<GetManufacturerByIdQuery, Manufacturer?>
{
    private readonly IApplicationDbContext _context;

    public GetManufacturerByIdQueryHandler(IApplicationDbContext context) => _context = context;

    public async Task<Manufacturer?> Handle(GetManufacturerByIdQuery request, CancellationToken cancellationToken)
        => await _context.Manufacturers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == request.Id, cancellationToken);
}
