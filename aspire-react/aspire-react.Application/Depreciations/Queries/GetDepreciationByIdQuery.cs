using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Depreciations.Queries;

/// <summary>
/// [Giai đoạn 2-cuối] GET /api/v1/depreciations/{id} — NEW endpoint (was missing pre-migration),
/// added per playbook §6.5 (approved pattern). No company-scoping — reference data.
/// </summary>
public record GetDepreciationByIdQuery(Guid Id) : IRequest<Depreciation?>;

public class GetDepreciationByIdQueryHandler : IRequestHandler<GetDepreciationByIdQuery, Depreciation?>
{
    private readonly IApplicationDbContext _context;

    public GetDepreciationByIdQueryHandler(IApplicationDbContext context) => _context = context;

    public async Task<Depreciation?> Handle(GetDepreciationByIdQuery request, CancellationToken cancellationToken)
        => await _context.Depreciations.AsNoTracking().FirstOrDefaultAsync(x => x.Id == request.Id, cancellationToken);
}
