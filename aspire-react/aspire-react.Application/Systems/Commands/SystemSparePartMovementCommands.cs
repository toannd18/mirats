using System.Text.Json;
using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Application.Common.Inventory;
using aspire_react.Server.Application.Systems.Queries;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Systems.Commands;

/// <summary>
/// [FEAT-SPARE 3] Issue or return a spare part for one system. Parts are inventory-wide, so this
/// command carries the item id directly (no per-system quota). The handler owns the transaction
/// because the item row must be locked with <c>FOR UPDATE</c>; the ActionLog is staged through
/// <see cref="IActionLogService.Log"/> and saved in the SAME transaction.
/// </summary>
public sealed record SparePartMovementResult(
    bool Success,
    string Message,
    string? ErrorCode = null,
    Guid? MovementId = null,
    int? Remaining = null,
    int? IssuedToSystem = null);

public record RecordSystemSparePartMovementCommand(
    Guid SystemInfoId,
    Guid? ComponentId,
    Guid? ConsumableId,
    int Quantity,
    SparePartMovementDirection Direction,
    string? Note,
    Guid CurrentUserId) : IRequest<SparePartMovementResult>;

public class RecordSystemSparePartMovementCommandHandler
    : IRequestHandler<RecordSystemSparePartMovementCommand, SparePartMovementResult>
{
    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _scope;
    private readonly IActionLogService _actionLogService;

    public RecordSystemSparePartMovementCommandHandler(
        IApplicationDbContext context, ICompanyScopeService scope, IActionLogService actionLogService)
    {
        _context = context;
        _scope = scope;
        _actionLogService = actionLogService;
    }

    public async Task<SparePartMovementResult> Handle(
        RecordSystemSparePartMovementCommand request, CancellationToken cancellationToken)
    {
        // InMemory (unit tests) has no relational transactions/SQL locking; the real path below is
        // exercised by the ThrowawayDb concurrency test on Postgres.
        if (!_context.Database.IsRelational())
        {
            var inMemoryResult = await ExecuteAsync(request, cancellationToken);
            if (inMemoryResult.Success)
                await _context.SaveChangesAsync(cancellationToken);
            return inMemoryResult;
        }

        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _context.Database.BeginTransactionAsync(cancellationToken);
            var result = await ExecuteAsync(request, cancellationToken);
            if (!result.Success)
            {
                await tx.RollbackAsync(cancellationToken);
                return result;
            }

            await _context.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
            return result;
        });
    }

    private async Task<SparePartMovementResult> ExecuteAsync(
        RecordSystemSparePartMovementCommand request, CancellationToken ct)
    {
        if (request.Quantity <= 0)
            return new(false, "Số lượng phải lớn hơn 0.", "INVALID_QUANTITY");

        if ((request.ComponentId.HasValue ? 1 : 0) + (request.ConsumableId.HasValue ? 1 : 0) != 1)
            return new(false, "Phải chọn đúng một linh kiện hoặc vật tư.", "INVALID_SPARE_ITEM");

        if (!await SystemsVisibility.IsSystemVisibleAsync(_context, _scope, request.SystemInfoId, ct))
            return new(false, "Không tìm thấy hệ thống hoặc ngoài phạm vi công ty.", "NOT_FOUND");

        var system = await _context.SystemInfos.AsNoTracking()
            .FirstAsync(s => s.Id == request.SystemInfoId, ct);

        if (request.ComponentId.HasValue)
        {
            var componentId = request.ComponentId.Value;
            var componentQuery = _context.Database.IsRelational()
                ? _context.Components.FromSqlRaw(
                    "SELECT * FROM components WHERE \"Id\" = {0} FOR UPDATE", componentId)
                : _context.Components;
            var component = await componentQuery
                .FirstOrDefaultAsync(c => c.Id == componentId, ct);
            if (component == null)
                return new(false, "Không tìm thấy linh kiện.", "NOT_FOUND");
            await _context.Entry(component).Collection(c => c.Assignments).LoadAsync(ct);
            await _context.Entry(component).Collection(c => c.Units).LoadAsync(ct);
            await _context.Entry(component).Collection(c => c.SparePartMovements).LoadAsync(ct);

            if (component.CompanyId != system.CompanyId)
                return new(false, "Linh kiện không thuộc cùng công ty với hệ thống.", "SPARE_PART_ITEM_MISMATCH");
            if (component.TrackingType == TrackingType.Serial)
                return new(false,
                    "Linh kiện quản lý theo số serial chưa hỗ trợ cấp/nhận vật tư dự phòng.",
                    "SPARE_PART_SERIAL_UNSUPPORTED");

            var remaining = InventoryRemaining.ForComponent(component);
            var issued = InventoryRemaining.IssuedToSystem(component.SparePartMovements, request.SystemInfoId);
            var validation = Validate(request, remaining, issued);
            if (validation != null) return validation;

            var movement = new SystemSparePartMovement
            {
                CompanyId = system.CompanyId,
                SystemInfoId = request.SystemInfoId,
                ComponentId = component.Id,
                Quantity = request.Quantity,
                Direction = request.Direction,
                Note = request.Note,
                CreatedById = request.CurrentUserId,
                CreatedAt = DateTime.UtcNow,
            };
            _context.SystemSparePartMovements.Add(movement);
            LogMovement(request, system, component.Name, ItemType.Component, component.Id, remaining, issued);
            return Success(request, movement.Id, remaining, issued);
        }

        var consumableId = request.ConsumableId!.Value;
        var consumableQuery = _context.Database.IsRelational()
            ? _context.Consumables.FromSqlRaw(
                "SELECT * FROM consumables WHERE \"Id\" = {0} FOR UPDATE", consumableId)
            : _context.Consumables;
        var consumable = await consumableQuery
            .FirstOrDefaultAsync(c => c.Id == consumableId, ct);
        if (consumable == null)
            return new(false, "Không tìm thấy vật tư.", "NOT_FOUND");
        await _context.Entry(consumable).Collection(c => c.Checkouts).LoadAsync(ct);
        await _context.Entry(consumable).Collection(c => c.SparePartMovements).LoadAsync(ct);

        if (consumable.CompanyId != system.CompanyId)
            return new(false, "Vật tư không thuộc cùng công ty với hệ thống.", "SPARE_PART_ITEM_MISMATCH");

        var consumableRemaining = InventoryRemaining.ForConsumable(consumable);
        var consumableIssued = InventoryRemaining.IssuedToSystem(
            consumable.SparePartMovements, request.SystemInfoId);
        var consumableValidation = Validate(request, consumableRemaining, consumableIssued);
        if (consumableValidation != null) return consumableValidation;

        var consumableMovement = new SystemSparePartMovement
        {
            CompanyId = system.CompanyId,
            SystemInfoId = request.SystemInfoId,
            ConsumableId = consumable.Id,
            Quantity = request.Quantity,
            Direction = request.Direction,
            Note = request.Note,
            CreatedById = request.CurrentUserId,
            CreatedAt = DateTime.UtcNow,
        };
        _context.SystemSparePartMovements.Add(consumableMovement);
        LogMovement(request, system, consumable.Name, ItemType.Consumable, consumable.Id,
            consumableRemaining, consumableIssued);
        return Success(request, consumableMovement.Id, consumableRemaining, consumableIssued);
    }

    private static SparePartMovementResult? Validate(
        RecordSystemSparePartMovementCommand request, int remaining, int issued)
    {
        if (request.Direction == SparePartMovementDirection.Issue && request.Quantity > remaining)
            return new(false, $"Tồn kho không đủ (còn lại {remaining}).", "SPARE_PART_INSUFFICIENT_STOCK");
        if (request.Direction == SparePartMovementDirection.Return && request.Quantity > issued)
            return new(false, $"Số lượng nhận lại vượt quá số đã cấp cho hệ thống (đã cấp {issued}).",
                "SPARE_PART_RETURN_EXCEEDS_ISSUED");
        return null;
    }

    private static SparePartMovementResult Success(
        RecordSystemSparePartMovementCommand request, Guid movementId, int remaining, int issued)
    {
        var signed = request.Direction == SparePartMovementDirection.Issue ? request.Quantity : -request.Quantity;
        return new(true,
            request.Direction == SparePartMovementDirection.Issue ? "Đã cấp vật tư cho hệ thống." : "Đã nhận lại vật tư.",
            MovementId: movementId,
            Remaining: remaining - signed,
            IssuedToSystem: issued + signed);
    }

    private void LogMovement(
        RecordSystemSparePartMovementCommand request,
        SystemInfo system,
        string itemName,
        ItemType itemType,
        Guid itemId,
        int remainingBefore,
        int issuedBefore)
    {
        var signed = request.Direction == SparePartMovementDirection.Issue ? request.Quantity : -request.Quantity;
        _actionLogService.Log(new ActionLogEntry
        {
            ItemType = itemType,
            ItemId = itemId,
            ActionType = request.Direction == SparePartMovementDirection.Issue
                ? ActionType.Checkout
                : ActionType.Checkin,
            CreatedBy = request.CurrentUserId,
            CompanyId = system.CompanyId,
            TargetType = AssignmentTargetType.SystemInfo,
            TargetId = system.Id,
            TargetSystemInfoId = system.Id,
            // UI builds the compact movement sentence from LogMeta; the note here is only the
            // operator's free-text note so the audit line never repeats item name/quantity.
            Note = request.Note,
            LogMeta = JsonSerializer.Serialize(new
            {
                item_name = itemName,
                quantity = request.Quantity,
                direction = request.Direction.ToString(),
                remaining = remainingBefore - signed,
                issued_to_system = issuedBefore + signed,
                note = request.Note,
            }),
        });
    }
}
