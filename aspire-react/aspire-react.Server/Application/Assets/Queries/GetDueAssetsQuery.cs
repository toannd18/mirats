using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Assets.Queries;

public record GetDueAuditAssetsQuery : IRequest<List<DueAssetDto>>;

public record DueAssetDto(
    Guid Id,
    string AssetTag,
    string Name,
    string? Serial,
    DateTime? LastAuditDate,
    DateTime? NextAuditDate,
    Guid? CurrentAssignmentId,
    string? StatusName,
    DueAssetLocationDto? Location);

public record DueAssetLocationDto(Guid Id, string Name);

public class GetDueAuditAssetsQueryHandler : IRequestHandler<GetDueAuditAssetsQuery, List<DueAssetDto>>
{
    private readonly AppDbContext _context;

    public GetDueAuditAssetsQueryHandler(AppDbContext context) => _context = context;

    public async Task<List<DueAssetDto>> Handle(GetDueAuditAssetsQuery request, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        return await _context.Assets
            .AsNoTracking()
            .Where(a => a.Status != AssetStatus.Archived && a.NextAuditDate != null && a.NextAuditDate < now)
            .Include(a => a.Location)
            .OrderBy(a => a.NextAuditDate)
            .Take(100)
            .Select(a => new DueAssetDto(
                a.Id, a.AssetTag, a.Name, a.Serial, a.LastAuditDate, a.NextAuditDate,
                a.CurrentAssignmentId,
                a.Status.ToString(),
                a.Location == null ? null : new DueAssetLocationDto(a.Location.Id, a.Location.Name)))
            .ToListAsync(cancellationToken);
    }
}