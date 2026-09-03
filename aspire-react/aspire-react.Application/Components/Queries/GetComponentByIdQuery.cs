using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Components.Queries;

public record ComponentRefDto(Guid Id, string Name);

public record ComponentAssetRefDto(Guid Id, string AssetTag, string Name);

public record ComponentAssignmentDto(Guid Id, int AssignedQty, string? Note, ComponentAssetRefDto Asset);

public record ComponentUnitDto(Guid Id, string SerialNo, string Status, Guid? CurrentAssetId, string? Notes,
    DateTime CreatedAt, DateTime UpdatedAt, ComponentAssetRefDto? CurrentAsset);

public record ComponentDetailDto(
    Guid Id, string Name, string? Serial, string? ItemNo, int Qty, int MinAmt,
    string? ModelNumber, string? OrderNumber, decimal? PurchaseCost, DateTime? PurchaseDate,
    string? Notes, DateTime UpdatedAt, string TrackingType, int Remaining, bool IsLowStock,
    UnitsSummaryDto UnitsSummary, bool CanDelete,
    ComponentRefDto? Category, ComponentRefDto? Company, ComponentRefDto? Location,
    ComponentRefDto? Supplier, ComponentRefDto? Manufacturer,
    IReadOnlyList<ComponentAssignmentDto> Assignments, IReadOnlyList<ComponentUnitDto> Units);

public record UnitsSummaryDto(int InStock, int Allocated, int Damaged, int Disposed);

/// <summary>
/// [Giai đoạn 3 — Nặng] GET /api/v1/components/{id} (extracted from ComponentsController.GetComponent).
/// Scope → 404 verbatim; UnitsSummary per TrackingType; canDelete via checkout-log scan;
/// projection NOT raw entity (detail shape verbatim, 20+ keys).
/// </summary>
public record GetComponentByIdQuery(Guid Id) : IRequest<ComponentDetailDto?>;

public class GetComponentByIdQueryHandler : IRequestHandler<GetComponentByIdQuery, ComponentDetailDto?>
{
    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _companyScope;

    public GetComponentByIdQueryHandler(IApplicationDbContext context, ICompanyScopeService companyScope)
    {
        _context = context;
        _companyScope = companyScope;
    }

    public async Task<ComponentDetailDto?> Handle(GetComponentByIdQuery request, CancellationToken cancellationToken)
    {
        var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        var c = await _context.Components
            .Include(x => x.Assignments).ThenInclude(a => a.Asset)
            .Include(x => x.Units).ThenInclude(u => u.CurrentAsset)
            .Include(x => x.Category).Include(x => x.Location)
            .Include(x => x.Company).Include(x => x.Supplier).Include(x => x.Manufacturer)
            .AsNoTracking().FirstOrDefaultAsync(x => x.Id == request.Id, cancellationToken);
        if (c == null || (userCompanyId.HasValue && c.CompanyId.HasValue && c.CompanyId.Value != userCompanyId.Value))
            return null;

        var inStock = c.TrackingType == TrackingType.Serial
            ? c.Units.Count(u => u.Status == ComponentUnitStatus.InStock)
            : c.Qty - c.Assignments.Sum(a => a.AssignedQty);
        var allocated = c.TrackingType == TrackingType.Serial
            ? c.Units.Count(u => u.Status == ComponentUnitStatus.Allocated)
            : c.Assignments.Sum(a => a.AssignedQty);
        var damaged = c.TrackingType == TrackingType.Serial ? c.Units.Count(u => u.Status == ComponentUnitStatus.Damaged) : 0;
        var disposed = c.TrackingType == TrackingType.Serial ? c.Units.Count(u => u.Status == ComponentUnitStatus.Disposed) : 0;

        // canDelete = the component (or any of its serial units) has NEVER been checked out.
        var unitIds = c.Units.Select(u => u.Id).ToList();
        var hasCheckout =
            await _context.ActionLogs.AsNoTracking().AnyAsync(l => l.ActionType == ActionType.Checkout &&
                ((l.ItemType == ItemType.Component && l.ItemId == request.Id) ||
                 (l.ItemType == ItemType.ComponentUnit && unitIds.Contains(l.ItemId))), cancellationToken);

        return new ComponentDetailDto(
            c.Id, c.Name, c.Serial, c.ItemNo, c.Qty, c.MinAmt,
            c.ModelNumber, c.OrderNumber, c.PurchaseCost, c.PurchaseDate,
            c.Notes, c.UpdatedAt, c.TrackingType.ToString(), inStock, inStock <= c.MinAmt,
            new UnitsSummaryDto(inStock, allocated, damaged, disposed),
            !hasCheckout,
            c.Category == null ? null : new ComponentRefDto(c.Category.Id, c.Category.Name),
            c.Company == null ? null : new ComponentRefDto(c.Company.Id, c.Company.Name),
            c.Location == null ? null : new ComponentRefDto(c.Location.Id, c.Location.Name),
            c.Supplier == null ? null : new ComponentRefDto(c.Supplier.Id, c.Supplier.Name),
            c.Manufacturer == null ? null : new ComponentRefDto(c.Manufacturer.Id, c.Manufacturer.Name),
            c.Assignments.Select(a => new ComponentAssignmentDto(a.Id, a.AssignedQty, a.Note,
                new ComponentAssetRefDto(a.Asset.Id, a.Asset.AssetTag, a.Asset.Name))).ToList(),
            c.Units.Select(u => new ComponentUnitDto(
                u.Id, u.SerialNo, u.Status.ToString(), u.CurrentAssetId, u.Notes, u.CreatedAt, u.UpdatedAt,
                u.CurrentAsset == null ? null : new ComponentAssetRefDto(u.CurrentAsset.Id, u.CurrentAsset.AssetTag, u.CurrentAsset.Name))).ToList());
    }
}
