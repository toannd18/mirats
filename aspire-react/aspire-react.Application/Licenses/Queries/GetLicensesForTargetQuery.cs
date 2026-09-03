using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Application.Licenses;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Licenses.Queries;

public record LicenseForTargetRowDto(
    Guid LicenseId, string LicenseName, string? Serial, int SeatNumber, DateTime? AssignedAt,
    string? Note, DateTime? ExpirationDate, bool ExpiringSoon, bool IsExpired, LicenseRefDto? Company);

/// <summary>
/// [Giai đoạn 3 — Nặng] GET /api/v1/licenses/for-user|for-asset|for-system (extracted from
/// LicensesController). Seats currently checked out to the target with expiring/expired flags —
/// identical shape for all three targets (verbatim).
/// </summary>
public record GetLicensesForTargetQuery(LicenseTargetKind Kind, Guid TargetId) : IRequest<IReadOnlyList<LicenseForTargetRowDto>>;

public enum LicenseTargetKind { User, Asset, SystemInfo }

public class GetLicensesForTargetQueryHandler : IRequestHandler<GetLicensesForTargetQuery, IReadOnlyList<LicenseForTargetRowDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _companyScope;

    public GetLicensesForTargetQueryHandler(IApplicationDbContext context, ICompanyScopeService companyScope)
    {
        _context = context;
        _companyScope = companyScope;
    }

    public async Task<IReadOnlyList<LicenseForTargetRowDto>> Handle(GetLicensesForTargetQuery request, CancellationToken cancellationToken)
    {
        var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        var now = DateTime.UtcNow;
        var soon = now.AddDays(30);

        var query = _context.LicenseSeats.AsNoTracking()
            .Include(s => s.License).Include(s => s.License.Company)
            .Where(s => s.License.DeletedAt == null)
            .Where(s => userCompanyId == null || s.License.CompanyId == null || s.License.CompanyId == userCompanyId.Value);

        query = request.Kind switch
        {
            LicenseTargetKind.User => query.Where(s => s.UserId == request.TargetId),
            LicenseTargetKind.Asset => query.Where(s => s.AssetId == request.TargetId),
            _ => query.Where(s => s.SystemInfoId == request.TargetId)
        };

        var seats = await query.Select(s => new LicenseForTargetRowDto(
            s.License.Id, s.License.Name, s.License.Serial, s.SeatNumber, s.AssignedAt, s.Note,
            s.License.ExpirationDate,
            s.License.ExpirationDate != null && s.License.ExpirationDate <= soon && s.License.ExpirationDate > now,
            s.License.ExpirationDate != null && s.License.ExpirationDate < now,
            s.License.Company == null ? null : new LicenseRefDto(s.License.Company.Id, s.License.Company.Name)))
            .ToListAsync(cancellationToken);
        return seats;
    }
}
