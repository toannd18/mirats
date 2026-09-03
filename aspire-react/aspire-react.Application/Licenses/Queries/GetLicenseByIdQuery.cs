using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Application.Licenses;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Licenses.Queries;

public record LicenseDetailDto(
    Guid Id, string Name, string? Serial, int Seats, bool Reassignable,
    DateTime? ExpirationDate, DateTime? TerminationDate, decimal? PurchaseCost, DateTime? PurchaseDate,
    string? OrderNumber, int? MinSeats, string? Notes, Guid? SupplierId, Guid? ManufacturerId,
    Guid? CategoryId, Guid? CompanyId, int AssignedSeats, int AvailableSeats,
    LicenseRefDto? Category, LicenseRefDto? Company, LicenseRefDto? Supplier, LicenseRefDto? Manufacturer,
    IReadOnlyList<LicenseSeatDto> SeatDetails);

/// <summary>
/// [Giai đoạn 3 — Nặng] GET /api/v1/licenses/{id} (extracted from LicensesController.GetLicense).
/// DeletedAt filter + IsLicenseVisible → NULL → 404; seats materialized + counted verbatim.
/// </summary>
public record GetLicenseByIdQuery(Guid Id) : IRequest<LicenseDetailDto?>;

public class GetLicenseByIdQueryHandler : IRequestHandler<GetLicenseByIdQuery, LicenseDetailDto?>
{
    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _companyScope;

    public GetLicenseByIdQueryHandler(IApplicationDbContext context, ICompanyScopeService companyScope)
    {
        _context = context;
        _companyScope = companyScope;
    }

    public async Task<LicenseDetailDto?> Handle(GetLicenseByIdQuery request, CancellationToken cancellationToken)
    {
        var l = await _context.Licenses
            .Include(x => x.Category).Include(x => x.Company).Include(x => x.Supplier).Include(x => x.Manufacturer)
            .AsNoTracking().FirstOrDefaultAsync(x => x.Id == request.Id && x.DeletedAt == null, cancellationToken);
        if (l == null) return null;
        var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        if (!LicenseRules.IsLicenseVisible(l, userCompanyId)) return null;

        var seats = await _context.LicenseSeats.AsNoTracking()
            .Include(s => s.User).Include(s => s.Asset).Include(s => s.SystemInfo)
            .Where(s => s.LicenseId == request.Id).OrderBy(s => s.SeatNumber).ToListAsync(cancellationToken);

        var assigned = seats.Count(s => LicenseRules.CountTargets(s) > 0);
        return new LicenseDetailDto(
            l.Id, l.Name, l.Serial, l.Seats, l.Reassignable,
            l.ExpirationDate, l.TerminationDate, l.PurchaseCost, l.PurchaseDate,
            l.OrderNumber, l.MinSeats, l.Notes, l.SupplierId, l.ManufacturerId,
            l.CategoryId, l.CompanyId, assigned, l.Seats - assigned,
            l.Category == null ? null : new LicenseRefDto(l.Category.Id, l.Category.Name),
            l.Company == null ? null : new LicenseRefDto(l.Company.Id, l.Company.Name),
            l.Supplier == null ? null : new LicenseRefDto(l.Supplier.Id, l.Supplier.Name),
            l.Manufacturer == null ? null : new LicenseRefDto(l.Manufacturer.Id, l.Manufacturer.Name),
            LicenseSeatProjection.Project(seats));
    }
}

/// <summary>Seat projection (used by detail + /seats + /checkout-free flows) — moved verbatim from ProjectSeats.</summary>
public static class LicenseSeatProjection
{
    public static IReadOnlyList<LicenseSeatDto> Project(IEnumerable<LicenseSeat> seats) => seats.Select(s => new LicenseSeatDto(
        s.Id,
        s.SeatNumber,
        LicenseRules.CountTargets(s) > 0,
        s.UserId != null ? "User" : s.AssetId != null ? "Asset" : s.SystemInfoId != null ? "SystemInfo" : null,
        s.User == null ? null : new LicenseUserDto(s.User.Id, (s.User.FirstName + " " + s.User.LastName).Trim() != "" ? (s.User.FirstName + " " + s.User.LastName).Trim() : s.User.Username),
        s.Asset == null ? null : new LicenseAssetDto(s.Asset.Id, s.Asset.AssetTag, s.Asset.Name),
        s.SystemInfo == null ? null : new LicenseSystemDto(s.SystemInfo.Id, s.SystemInfo.Code, s.SystemInfo.Name),
        s.Note,
        s.AssignedAt)).ToList();
}
