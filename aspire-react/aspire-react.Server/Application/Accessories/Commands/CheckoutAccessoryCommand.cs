using System.Text.Json;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using aspire_react.Server.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Accessories.Commands;

// ==================== CHECKOUT ====================

public record CheckoutAccessoryCommand : IRequest<AccessoryResult>
{
    public Guid AccessoryId { get; init; }
    public AccessoryCheckoutType CheckoutType { get; init; }
    public Guid TargetId { get; init; }
    public int Quantity { get; init; }
    public string? Note { get; init; }
    /// <summary>Local DB user ID (not Keycloak sub)</summary>
    public Guid CurrentUserId { get; init; }
}

public class CheckoutAccessoryCommandHandler : IRequestHandler<CheckoutAccessoryCommand, AccessoryResult>
{
    private readonly AppDbContext _context;
    private readonly IActionLogService _actionLogService;

    public CheckoutAccessoryCommandHandler(AppDbContext context, IActionLogService actionLogService)
    {
        _context = context;
        _actionLogService = actionLogService;
    }

    public async Task<AccessoryResult> Handle(CheckoutAccessoryCommand request, CancellationToken cancellationToken)
    {
        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _context.Database.BeginTransactionAsync(cancellationToken);

            // Task O-FIX: lock the accessory row FOR UPDATE (mirroring the Asset checkout pattern) so two
            // concurrent checkouts cannot both read the same remaining and overcommit the last unit. On EF
            // InMemory (no raw SQL) fall back to a normal load — real locking is covered by Category=Concurrency
            // tests against real Postgres.
            var accessory = _context.Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory"
                ? await _context.Accessories.FirstOrDefaultAsync(a => a.Id == request.AccessoryId, cancellationToken)
                : await _context.Accessories
                    .FromSqlRaw("SELECT * FROM accessories WHERE \"Id\" = {0} FOR UPDATE", request.AccessoryId)
                    .FirstOrDefaultAsync(cancellationToken);

            if (accessory == null)
                return new AccessoryResult(false, "Accessory not found.", ErrorCode: "NOT_FOUND");

            var checkedOut = await _context.AccessoryCheckouts
                .Where(a => a.AccessoryId == request.AccessoryId)
                .SumAsync(a => (int?)(a.AssignedQty - a.ReturnedQty), cancellationToken) ?? 0;
            var remaining = accessory.Qty - checkedOut;
            if (request.Quantity > remaining)
                return new AccessoryResult(false, $"Insufficient stock. Remaining: {remaining}", ErrorCode: "INSUFFICIENT_STOCK");

            var targetValid = request.CheckoutType switch
            {
                AccessoryCheckoutType.User => await _context.Users.AnyAsync(u => u.Id == request.TargetId, cancellationToken),
                AccessoryCheckoutType.Department => await _context.Departments.AnyAsync(d => d.Id == request.TargetId, cancellationToken),
                AccessoryCheckoutType.Location => await _context.Locations.AnyAsync(l => l.Id == request.TargetId, cancellationToken),
                AccessoryCheckoutType.SystemPosition => await _context.SystemPositions.AnyAsync(sp => sp.Id == request.TargetId, cancellationToken),
                _ => false
            };

            if (!targetValid)
                return new AccessoryResult(false, $"Target entity not found for type '{request.CheckoutType}'.", ErrorCode: "TARGET_NOT_FOUND");

            // ──── Company Isolation ────
            // If the accessory is scoped to a company, the target must belong to the same company.
            if (accessory.CompanyId.HasValue)
            {
                Guid? targetCompanyId = request.CheckoutType switch
                {
                    AccessoryCheckoutType.User => await _context.Users
                        .Where(u => u.Id == request.TargetId)
                        .Select(u => u.CompanyId)
                        .FirstOrDefaultAsync(cancellationToken),
                    AccessoryCheckoutType.Department => await _context.Departments
                        .Where(d => d.Id == request.TargetId)
                        .Select(d => d.CompanyId)
                        .FirstOrDefaultAsync(cancellationToken),
                    AccessoryCheckoutType.Location => await _context.Locations
                        .Where(l => l.Id == request.TargetId)
                        .Select(l => (Guid?)null) // Locations don't have CompanyId — always allowed
                        .FirstOrDefaultAsync(cancellationToken),
                    AccessoryCheckoutType.SystemPosition => await _context.SystemPositions
                        .Include(sp => sp.SystemInfo)
                        .Where(sp => sp.Id == request.TargetId)
                        .Select(sp => sp.SystemInfo.CompanyId)
                        .FirstOrDefaultAsync(cancellationToken),
                    _ => null
                };

                // Locations are allowed regardless (no CompanyId on Location entity)
                if (request.CheckoutType != AccessoryCheckoutType.Location
                    && targetCompanyId != accessory.CompanyId)
                {
                    return new AccessoryResult(false,
                        $"Đối tượng nhận không thuộc cùng công ty với phụ kiện này.",
                        ErrorCode: "COMPANY_MISMATCH");
                }
            }

            var co = new AccessoryCheckout
            {
                AccessoryId = request.AccessoryId,
                CheckoutType = request.CheckoutType,
                TargetId = request.TargetId,
                AssignedQty = request.Quantity,
                ReturnedQty = 0,
                CreatedByUserId = request.CurrentUserId,
                Note = request.Note
            };
            _context.AccessoryCheckouts.Add(co);

            _actionLogService.LogAction(
                itemType: ItemType.Accessory,
                itemId: request.AccessoryId,
                actionType: ActionType.Checkout,
                loggedByUserId: request.CurrentUserId,
                targetType: MapCheckoutTypeToTargetType(request.CheckoutType),
                targetId: request.TargetId,
                companyId: accessory.CompanyId,
                note: request.Note,
                logMeta: JsonSerializer.Serialize(new
                {
                    changes = new Dictionary<string, object?>
                    {
                        ["quantity"] = new { old = remaining, @new = checkedOut + request.Quantity },
                        ["checkout_type"] = new { old = (string?)null, @new = request.CheckoutType.ToString() }
                    }
                }));

            await _context.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);

            return new AccessoryResult(true, $"{request.Quantity} accessory(s) checked out.", AccessoryId: co.Id);
        });
    }

    private static AssignmentTargetType MapCheckoutTypeToTargetType(AccessoryCheckoutType checkoutType)
    {
        // 1:1 — the log's TargetType must reflect the REAL accessory checkout target so that
        // target-name resolution + system-history filtering behave correctly.
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