using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Models.Queries;

/// <summary>
/// [Giai đoạn 2] GET /api/v1/models/{id} — NEW endpoint (was missing pre-migration),
/// added per playbook §6.5 (approved pattern). No company-scoping — reference data.
/// Returns the AssetModel entity (no company-isolation risk — entity has no CompanyId).
/// </summary>
public record GetModelByIdQuery(Guid Id) : IRequest<AssetModel?>;

public class GetModelByIdQueryHandler : IRequestHandler<GetModelByIdQuery, AssetModel?>
{
    private readonly IApplicationDbContext _context;

    public GetModelByIdQueryHandler(IApplicationDbContext context) => _context = context;

    public async Task<AssetModel?> Handle(GetModelByIdQuery request, CancellationToken cancellationToken)
        => await _context.Models.AsNoTracking().FirstOrDefaultAsync(x => x.Id == request.Id, cancellationToken);
}
