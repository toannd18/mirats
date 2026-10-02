using aspire_react.Server.Domain.Interfaces;
using aspire_react.Server.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Accessories.Queries;

/// <summary>
/// [FIX-N2 2026-10-02] GET /api/v1/accessories/{id} — verbatim move out of AccessoriesController:
/// same Includes, same company-scope rule (out-of-scope behaves like not-found → 404 hide-existence),
/// same projection fields (including Remaining / PercentRemaining / IsLowStock math).
/// </summary>
public record GetAccessoryByIdQuery(Guid Id) : IRequest<GetAccessoryByIdResult>;

public sealed record AccessoryNamedRefDto(Guid Id, string Name);

public sealed record AccessoryDetailDto(
    Guid Id,
    string Name,
    string? ItemNo,
    int Qty,
    int MinAmt,
    string? ModelNumber,
    string? OrderNumber,
    DateTime? PurchaseDate,
    decimal? PurchaseCost,
    string? Notes,
    Guid? CategoryId,
    Guid? ManufacturerId,
    Guid? SupplierId,
    Guid? LocationId,
    Guid? CompanyId,
    int Remaining,
    double PercentRemaining,
    bool IsLowStock,
    int CheckedOutQty,
    AccessoryNamedRefDto? Category,
    AccessoryNamedRefDto? Manufacturer,
    AccessoryNamedRefDto? Supplier,
    AccessoryNamedRefDto? Location,
    AccessoryNamedRefDto? Company);

public sealed record GetAccessoryByIdResult(AccessoryDetailDto? Accessory);

public class GetAccessoryByIdQueryHandler : IRequestHandler<GetAccessoryByIdQuery, GetAccessoryByIdResult>
{
    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _companyScope;

    public GetAccessoryByIdQueryHandler(IApplicationDbContext context, ICompanyScopeService companyScope)
    {
        _context = context;
        _companyScope = companyScope;
    }

    public async Task<GetAccessoryByIdResult> Handle(GetAccessoryByIdQuery request, CancellationToken cancellationToken)
    {
        var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        var a = await _context.Accessories.Include(x => x.Checkouts).Include(x => x.Category)
            .Include(x => x.Manufacturer).Include(x => x.Supplier).Include(x => x.Location)
            .Include(x => x.Company).AsNoTracking().FirstOrDefaultAsync(x => x.Id == request.Id, cancellationToken);
        if (a == null || (userCompanyId.HasValue && a.CompanyId.HasValue && a.CompanyId.Value != userCompanyId.Value))
            return new GetAccessoryByIdResult(null);

        var remaining = a.Qty - a.Checkouts.Sum(ch => ch.AssignedQty - ch.ReturnedQty);
        var dto = new AccessoryDetailDto(
            a.Id,
            a.Name,
            a.ItemNo,
            a.Qty,
            a.MinAmt,
            a.ModelNumber,
            a.OrderNumber,
            a.PurchaseDate,
            a.PurchaseCost,
            a.Notes,
            a.CategoryId,
            a.ManufacturerId,
            a.SupplierId,
            a.LocationId,
            a.CompanyId,
            remaining,
            a.Qty > 0 ? Math.Round((double)remaining / a.Qty * 100, 2) : 0,
            remaining <= a.MinAmt,
            a.Checkouts.Sum(ch => ch.AssignedQty - ch.ReturnedQty),
            a.Category == null ? null : new AccessoryNamedRefDto(a.Category.Id, a.Category.Name),
            a.Manufacturer == null ? null : new AccessoryNamedRefDto(a.Manufacturer.Id, a.Manufacturer.Name),
            a.Supplier == null ? null : new AccessoryNamedRefDto(a.Supplier.Id, a.Supplier.Name),
            a.Location == null ? null : new AccessoryNamedRefDto(a.Location.Id, a.Location.Name),
            a.Company == null ? null : new AccessoryNamedRefDto(a.Company.Id, a.Company.Name));

        return new GetAccessoryByIdResult(dto);
    }
}
