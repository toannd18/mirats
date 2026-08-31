using System.Text.Json;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using aspire_react.Server.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Accessories.Commands;

// ==================== CHECKIN ====================

public record CheckinAccessoryCommand : IRequest<AccessoryResult>
{
    public Guid CheckoutId { get; init; }
    public int ReturnQty { get; init; }
    public string? Note { get; init; }
    /// <summary>Local DB user ID (not Keycloak sub)</summary>
    public Guid CurrentUserId { get; init; }
}

public class CheckinAccessoryCommandHandler : IRequestHandler<CheckinAccessoryCommand, AccessoryResult>
{
    private readonly IApplicationDbContext _context;
    private readonly IActionLogService _actionLogService;
    private readonly ICompanyScopeService _companyScope;

    public CheckinAccessoryCommandHandler(IApplicationDbContext context, IActionLogService actionLogService, ICompanyScopeService companyScope)
    {
        _context = context;
        _actionLogService = actionLogService;
        _companyScope = companyScope;
    }

    public async Task<AccessoryResult> Handle(CheckinAccessoryCommand request, CancellationToken cancellationToken)
    {
        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _context.Database.BeginTransactionAsync(cancellationToken);

            var co = await _context.AccessoryCheckouts.Include(c => c.Accessory)
                .FirstOrDefaultAsync(c => c.Id == request.CheckoutId, cancellationToken);
            if (co == null)
                return new AccessoryResult(false, "Checkout record not found.", ErrorCode: "CHECKOUT_NOT_FOUND");

            // [SEC-FIX S2/S4-S6, 2026-08-23] Actor-scope (same pattern as DeleteAccessoryCommand in
            // this domain): a regular user may only check in accessories of their own company (or
            // floater); Superuser bypasses. Previously CheckinAccessoryCommand had NO company check
            // at all — a user from company A could return (check in) a company-B accessory's
            // checkout record by id.
            var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
            if (userCompanyId.HasValue && co.Accessory?.CompanyId.HasValue == true && co.Accessory.CompanyId.Value != userCompanyId.Value)
                return new AccessoryResult(false, "Checkout record not found.", ErrorCode: "CHECKOUT_NOT_FOUND");

            var currentlyOut = co.AssignedQty - co.ReturnedQty;

            if (request.ReturnQty <= 0)
                return new AccessoryResult(false, "Return quantity must be greater than 0.", ErrorCode: "INVALID_RETURN_QTY");

            if (request.ReturnQty > currentlyOut)
                return new AccessoryResult(false, $"Cannot return more than checked out. Currently out: {currentlyOut}", ErrorCode: "EXCEEDS_CHECKED_OUT");

            // Apply the return
            co.ReturnedQty += request.ReturnQty;
            var remainingOut = co.AssignedQty - co.ReturnedQty;

            // Map AccessoryCheckoutType to AssignmentTargetType for ActionLog
            var targetType = MapCheckoutTypeToTargetType(co.CheckoutType);

            // Log via centralized service
            _actionLogService.LogAction(
                itemType: ItemType.Accessory,
                itemId: co.AccessoryId,
                actionType: ActionType.Checkin,
                loggedByUserId: request.CurrentUserId,
                targetType: targetType,
                targetId: co.TargetId,
                companyId: co.Accessory?.CompanyId,
                note: request.Note,
                logMeta: JsonSerializer.Serialize(new
                {
                    changes = new Dictionary<string, object?>
                    {
                        ["return_qty"] = new { old = (int?)null, @new = request.ReturnQty },
                        ["quantity"] = new { old = remainingOut + request.ReturnQty, @new = remainingOut }
                    }
                }));

            await _context.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);

            return new AccessoryResult(true, $"Returned {request.ReturnQty} accessory(s). Remaining checked out: {remainingOut}", AccessoryId: co.AccessoryId);
        });
    }

    private static AssignmentTargetType MapCheckoutTypeToTargetType(AccessoryCheckoutType checkoutType)
    {
        // 1:1 — must stay identical to CheckoutAccessoryCommand.MapCheckoutTypeToTargetType.
        return checkoutType switch
        {
            AccessoryCheckoutType.User => AssignmentTargetType.User,
            AccessoryCheckoutType.Department => AssignmentTargetType.Department,
            AccessoryCheckoutType.Location => AssignmentTargetType.Location,
            AccessoryCheckoutType.SystemPosition => AssignmentTargetType.SystemPosition,
            _ => AssignmentTargetType.User
        };
    }
}