using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Application.Licenses;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Licenses.Queries;

public record LicenseSeatDto(
    Guid Id, int SeatNumber, bool Assigned, string? TargetType,
    LicenseUserDto? User, LicenseAssetDto? Asset, LicenseSystemDto? SystemInfo,
    string? Note, DateTime? AssignedAt);

public record LicenseUserDto(Guid Id, string Name);

public record LicenseAssetDto(Guid Id, string AssetTag, string Name);

public record LicenseSystemDto(Guid Id, string Code, string Name);

public record LicenseListItemDto(
    Guid Id, string Name, string? Serial, string? Notes, int Seats, bool Reassignable,
    DateTime? ExpirationDate, DateTime? TerminationDate, int? MinSeats,
    int AssignedSeats, int AvailableSeats, bool ExpiringSoon, bool IsExpired, bool IsLowSeats,
    LicenseRefDto? Category, LicenseRefDto? Company, LicenseRefDto? Supplier, LicenseRefDto? Manufacturer);

public record LicenseRefDto(Guid Id, string Name);

public record LicenseListResult(IReadOnlyList<LicenseListItemDto> Items, int Total);

/// <summary>
/// [Giai đoạn 3 — Nặng] GET /api/v1/licenses (extracted from LicensesController.GetLicenses).
/// VERBATIM: DeletedAt filter + FMCS scoping BEFORE paging; expiringSoon/lowSeats applied as a
/// POST-FILTER ON THE PAGE (quirk preserved — total stays unfiltered by those flags).
/// </summary>
public record ListLicensesQuery(string? Search, Guid? CategoryId, Guid? CompanyId, bool ExpiringSoon = false,
    bool LowSeats = false, int Page = 1, int PageSize = 20) : IRequest<LicenseListResult>;

public class ListLicensesQueryHandler : IRequestHandler<ListLicensesQuery, LicenseListResult>
{
    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _companyScope;

    public ListLicensesQueryHandler(IApplicationDbContext context, ICompanyScopeService companyScope)
    {
        _context = context;
        _companyScope = companyScope;
    }

    public async Task<LicenseListResult> Handle(ListLicensesQuery request, CancellationToken cancellationToken)
    {
        var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        var query = _context.Licenses.AsNoTracking()
            .Where(l => l.DeletedAt == null)
            .Where(l => userCompanyId == null || l.CompanyId == null || l.CompanyId == userCompanyId.Value);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var s = request.Search.ToLower();
            query = query.Where(l => l.Name.ToLower().Contains(s) || (l.Serial != null && l.Serial.ToLower().Contains(s)));
        }
        if (request.CategoryId.HasValue) query = query.Where(l => l.CategoryId == request.CategoryId);
        if (request.CompanyId.HasValue) query = query.Where(l => l.CompanyId == request.CompanyId);

        var now = DateTime.UtcNow;
        var soon = now.AddDays(30);

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .Include(l => l.LicenseSeats)
            .OrderBy(l => l.Name)
            .Skip((request.Page - 1) * request.PageSize).Take(request.PageSize)
            .Select(l => new LicenseListItemDto(
                l.Id, l.Name, l.Serial, l.Notes, l.Seats, l.Reassignable,
                l.ExpirationDate, l.TerminationDate, l.MinSeats,
                l.LicenseSeats.Count(s => s.UserId != null || s.AssetId != null || s.SystemInfoId != null),
                l.Seats - l.LicenseSeats.Count(s => s.UserId != null || s.AssetId != null || s.SystemInfoId != null),
                l.ExpirationDate != null && l.ExpirationDate <= soon && l.ExpirationDate > now,
                l.ExpirationDate != null && l.ExpirationDate < now,
                l.MinSeats != null && (l.Seats - l.LicenseSeats.Count(s => s.UserId != null || s.AssetId != null || s.SystemInfoId != null)) <= l.MinSeats.Value,
                l.Category == null ? null : new LicenseRefDto(l.Category.Id, l.Category.Name),
                l.Company == null ? null : new LicenseRefDto(l.Company.Id, l.Company.Name),
                l.Supplier == null ? null : new LicenseRefDto(l.Supplier.Id, l.Supplier.Name),
                l.Manufacturer == null ? null : new LicenseRefDto(l.Manufacturer.Id, l.Manufacturer.Name)))
            .ToListAsync(cancellationToken);

        var list = items.ToList();
        if (request.ExpiringSoon) list = list.Where(i => i.ExpiringSoon || i.IsExpired).ToList();
        if (request.LowSeats) list = list.Where(i => i.IsLowSeats).ToList();

        return new LicenseListResult(list, total);
    }
}
