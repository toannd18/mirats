using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Systems.Queries;

public record SystemAccessoryRowDto(
    Guid Id,
    Guid AccessoryId,
    string AccessoryName,
    string? AccessoryItemNo,
    int AssignedQty,
    int ReturnedQty,
    int RemainingCheckedOut,
    SystemAssetPositionDto? SystemPosition,
    string? Note,
    DateTime CheckedOutAt,
    Guid? CreatedByUserId,
    string? CreatedByName);

/// <summary>
/// [Giai đoạn 3] GET /api/v1/systems/{id}/accessories (extracted from
/// SystemsController.GetAccessories). Accessories checked out across ALL child SystemPositions
/// (AccessoryCheckout.CheckoutType = SystemPosition) with optional position narrow. Company
/// defense-in-depth verbatim; CreatedByName trim→username fallback verbatim. No pagination.
/// </summary>
public record GetSystemAccessoriesQuery(Guid Id, Guid? SystemPositionId = null)
    : IRequest<IReadOnlyList<SystemAccessoryRowDto>?>;

public class GetSystemAccessoriesQueryHandler : IRequestHandler<GetSystemAccessoriesQuery, IReadOnlyList<SystemAccessoryRowDto>?>
{
    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _companyScope;

    public GetSystemAccessoriesQueryHandler(IApplicationDbContext context, ICompanyScopeService companyScope)
    {
        _context = context;
        _companyScope = companyScope;
    }

    public async Task<IReadOnlyList<SystemAccessoryRowDto>?> Handle(GetSystemAccessoriesQuery request, CancellationToken cancellationToken)
    {
        if (!await SystemsVisibility.IsSystemVisibleAsync(_context, _companyScope, request.Id, cancellationToken))
            return null;

        var positionIds = await _context.SystemPositions.AsNoTracking()
            .Where(sp => sp.SystemInfoId == request.Id)
            .Select(sp => sp.Id)
            .ToListAsync(cancellationToken);

        if (positionIds.Count == 0)
            return new List<SystemAccessoryRowDto>();

        var query = _context.AccessoryCheckouts.AsNoTracking()
            .Include(ch => ch.Accessory)
            .Include(ch => ch.CreatedByUser)
            .Where(ch => ch.CheckoutType == AccessoryCheckoutType.SystemPosition && positionIds.Contains(ch.TargetId));

        if (request.SystemPositionId.HasValue)
            query = query.Where(ch => ch.TargetId == request.SystemPositionId.Value);

        // Defense in depth: same company rule as the accessory checkout command (an accessory is
        // scoped to its company; company-less accessories are visible to everyone).
        var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        if (userCompanyId.HasValue)
            query = query.Where(ch => ch.Accessory.CompanyId == null || ch.Accessory.CompanyId == userCompanyId.Value);

        var items = await query.OrderByDescending(ch => ch.CheckedOutAt)
            .Select(ch => new
            {
                ch.Id,
                ch.AccessoryId,
                AccessoryName = ch.Accessory.Name,
                AccessoryItemNo = ch.Accessory.ItemNo,
                ch.AssignedQty,
                ch.ReturnedQty,
                RemainingCheckedOut = ch.AssignedQty - ch.ReturnedQty,
                SystemPositionId = ch.TargetId,
                ch.Note,
                ch.CheckedOutAt,
                CreatedByUserId = ch.CreatedByUserId,
                CreatedByUsername = ch.CreatedByUser != null ? ch.CreatedByUser.Username : null,
                CreatedByFirstName = ch.CreatedByUser != null ? ch.CreatedByUser.FirstName : null,
                CreatedByLastName = ch.CreatedByUser != null ? ch.CreatedByUser.LastName : null
            })
            .ToListAsync(cancellationToken);

        var posIds = items.Select(i => i.SystemPositionId).Distinct().ToList();
        var posDict = new Dictionary<Guid, (string Code, string Name)>();
        if (posIds.Any())
        {
            posDict = await _context.SystemPositions.AsNoTracking()
                .Where(sp => posIds.Contains(sp.Id))
                .Select(sp => new { sp.Id, sp.Code, sp.Name })
                .ToDictionaryAsync(sp => sp.Id, sp => (sp.Code, sp.Name), cancellationToken);
        }

        var enriched = items.Select(i => new SystemAccessoryRowDto(
            i.Id,
            i.AccessoryId,
            i.AccessoryName,
            i.AccessoryItemNo,
            i.AssignedQty,
            i.ReturnedQty,
            i.RemainingCheckedOut,
            posDict.TryGetValue(i.SystemPositionId, out var p)
                ? new SystemAssetPositionDto(i.SystemPositionId, p.Code, p.Name)
                : null,
            i.Note,
            i.CheckedOutAt,
            i.CreatedByUserId,
            (i.CreatedByFirstName + " " + i.CreatedByLastName).Trim() != ""
                ? (i.CreatedByFirstName + " " + i.CreatedByLastName).Trim()
                : i.CreatedByUsername)).ToList();

        return enriched;
    }
}
