using System.Text.Json;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using aspire_react.Server.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Accessories.Commands;

// ==================== DELETE ====================

public record DeleteAccessoryCommand : IRequest<AccessoryResult>
{
    public Guid AccessoryId { get; init; }
    /// <summary>Local DB user ID (not Keycloak sub)</summary>
    public Guid CurrentUserId { get; init; }
}

public class DeleteAccessoryCommandHandler : IRequestHandler<DeleteAccessoryCommand, AccessoryResult>
{
    private readonly IApplicationDbContext _context;
    private readonly IActionLogService _actionLogService;
    private readonly ICompanyScopeService _companyScope;

    public DeleteAccessoryCommandHandler(IApplicationDbContext context, IActionLogService actionLogService, ICompanyScopeService companyScope)
    {
        _context = context;
        _actionLogService = actionLogService;
        _companyScope = companyScope;
    }

    public async Task<AccessoryResult> Handle(DeleteAccessoryCommand request, CancellationToken cancellationToken)
    {
        var accessory = await _context.Accessories
            .Include(a => a.Checkouts)
            .FirstOrDefaultAsync(a => a.Id == request.AccessoryId, cancellationToken);

        if (accessory == null)
            return new AccessoryResult(false, "Accessory not found.", ErrorCode: "NOT_FOUND");

        // Company scoping: a regular user may only delete accessories of their own company (or floater).
        var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        if (userCompanyId.HasValue && accessory.CompanyId.HasValue && accessory.CompanyId.Value != userCompanyId.Value)
            return new AccessoryResult(false, "Accessory not found.", ErrorCode: "NOT_FOUND");

        var hasCheckoutHistory = accessory.Checkouts.Any();
        if (hasCheckoutHistory)
            return new AccessoryResult(false, "Không thể xóa phụ kiện đã từng được cấp phát (lịch sử cấp phát phải được giữ).", ErrorCode: "ACCESSORY_HAS_CHECKOUTS");

        var accessoryName = accessory.Name;
        var accessoryItemNo = accessory.ItemNo;

        // Log before removal so we have the data
        _actionLogService.LogAction(
            itemType: ItemType.Accessory,
            itemId: request.AccessoryId,
            actionType: ActionType.Delete,
            loggedByUserId: request.CurrentUserId,
            companyId: accessory.CompanyId,
            note: $"Deleted accessory: {accessoryName}" + (accessoryItemNo != null ? $" (#{accessoryItemNo})" : ""),
            logMeta: JsonSerializer.Serialize(new { name = accessoryName, itemNo = accessoryItemNo, qty = accessory.Qty }));

        _context.Accessories.Remove(accessory);
        await _context.SaveChangesAsync(cancellationToken);

        return new AccessoryResult(true, "Accessory deleted successfully.");
    }
}