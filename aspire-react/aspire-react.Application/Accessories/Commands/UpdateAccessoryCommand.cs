using System.Text.Json;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using aspire_react.Server.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Accessories.Commands;

/// <summary>
/// [FIX-N2 2026-10-02] PUT /api/v1/accessories/{id} — moved out of AccessoriesController.
///
/// WHY this existed as a gap (audit 2026-10-02, finding N2): the controller action performed the
/// whole update with EF directly and wrote **no ActionLog at all**, violating the project-wide
/// "ActionLog is mandatory for every Update" rule (its own comment claimed logging happened
/// "via centralized service through SaveChanges" — nothing staged a log). This command implements
/// <see cref="ILoggableCommand{TResponse}"/> so ActionLogBehavior persists the entry in the SAME
/// transaction as the data change.
///
/// Behaviour is otherwise a VERBATIM move — same guards, same order, same bodies:
///   not-found / out-of-scope → 404 (hide-existence) · company change after any checkout history →
///   400 FIELD_LOCKED · any active (unreturned) checkout → 400 without error_code · Task M2 patch
///   semantics for all 15 fields (absent → keep).
/// </summary>
public record UpdateAccessoryCommand : IRequest<UpdateAccessoryResult>, ILoggableCommand<UpdateAccessoryResult>
{
    public Guid Id { get; init; }
    public string? Name { get; init; }
    public string? ItemNo { get; init; }
    public int? Qty { get; init; }
    public int? MinAmt { get; init; }
    public Guid? CategoryId { get; init; }
    public Guid? ManufacturerId { get; init; }
    public Guid? SupplierId { get; init; }
    public Guid? LocationId { get; init; }
    public Guid? CompanyId { get; init; }
    public string? ModelNumber { get; init; }
    public string? OrderNumber { get; init; }
    public decimal? PurchaseCost { get; init; }
    public DateTime? PurchaseDate { get; init; }
    public string? Notes { get; init; }
    public string? Image { get; init; }
    public Guid CurrentUserId { get; init; }

    public ActionLogEntry? BuildLogEntry(UpdateAccessoryResult response)
    {
        // Soft-fail (not found / field locked / active checkouts) never logged — mirrors the
        // early returns of the old controller action.
        if (!response.Success) return null;

        return new ActionLogEntry
        {
            ItemType = ItemType.Accessory,
            ItemId = Id,
            ActionType = ActionType.Update,
            CreatedBy = CurrentUserId,
            CompanyId = response.CompanyId,
            Note = response.Note,
            LogMeta = response.LogMeta
        };
    }
}

public record UpdateAccessoryResult(
    bool Success,
    string Message,
    Guid? AccessoryId = null,
    string? ErrorCode = null,
    Guid? CompanyId = null,
    string? Note = null,
    string? LogMeta = null);

public class UpdateAccessoryCommandHandler : IRequestHandler<UpdateAccessoryCommand, UpdateAccessoryResult>
{
    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _companyScope;

    public UpdateAccessoryCommandHandler(IApplicationDbContext context, ICompanyScopeService companyScope)
    {
        _context = context;
        _companyScope = companyScope;
    }

    public async Task<UpdateAccessoryResult> Handle(UpdateAccessoryCommand r, CancellationToken cancellationToken)
    {
        var a = await _context.Accessories.FirstOrDefaultAsync(x => x.Id == r.Id, cancellationToken);
        if (a == null) return new UpdateAccessoryResult(false, "Accessory not found.", ErrorCode: "NOT_FOUND");

        // Company scoping: a regular user may only edit accessories of their own company (or floater).
        var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        if (userCompanyId.HasValue && a.CompanyId.HasValue && a.CompanyId.Value != userCompanyId.Value)
            return new UpdateAccessoryResult(false, "Accessory not found.", ErrorCode: "NOT_FOUND");

        // CompanyId-lock after any checkout history (mirrors Consumable/License): past checkouts were
        // tied to the old company. Patch-aware — only when CompanyId is explicitly sent and differs.
        if (r.CompanyId.HasValue && r.CompanyId.Value != a.CompanyId
            && await _context.AccessoryCheckouts.AnyAsync(ch => ch.AccessoryId == r.Id, cancellationToken))
            return new UpdateAccessoryResult(false,
                "Phụ kiện đã từng được cấp phát — không thể đổi công ty.", ErrorCode: "FIELD_LOCKED");

        var hasActiveCheckouts = await _context.AccessoryCheckouts
            .AnyAsync(ch => ch.AccessoryId == r.Id && ch.AssignedQty > ch.ReturnedQty, cancellationToken);
        if (hasActiveCheckouts)
            // Verbatim: this body has NO error_code (only status + message).
            return new UpdateAccessoryResult(false, "Không thể sửa phụ kiện đang có thiết bị đang được cấp phát.");

        // Before-snapshot for the ActionLog changes map (command is immutable — captured here).
        var before = new Dictionary<string, object?>
        {
            ["name"] = a.Name,
            ["itemNo"] = a.ItemNo,
            ["qty"] = a.Qty,
            ["minAmt"] = a.MinAmt,
            ["categoryId"] = a.CategoryId,
            ["manufacturerId"] = a.ManufacturerId,
            ["supplierId"] = a.SupplierId,
            ["locationId"] = a.LocationId,
            ["companyId"] = a.CompanyId,
            ["modelNumber"] = a.ModelNumber,
            ["orderNumber"] = a.OrderNumber,
            ["purchaseCost"] = a.PurchaseCost,
            ["purchaseDate"] = a.PurchaseDate,
            ["notes"] = a.Notes,
            ["image"] = a.Image
        };

        // Task M2 patch semantics: only fields explicitly sent are applied.
        if (!string.IsNullOrWhiteSpace(r.Name)) a.Name = r.Name;
        if (r.ItemNo is not null) a.ItemNo = r.ItemNo;
        if (r.Qty.HasValue) a.Qty = r.Qty.Value;
        if (r.MinAmt.HasValue) a.MinAmt = r.MinAmt.Value;
        if (r.CategoryId is not null) a.CategoryId = r.CategoryId;
        if (r.ManufacturerId is not null) a.ManufacturerId = r.ManufacturerId;
        if (r.SupplierId is not null) a.SupplierId = r.SupplierId;
        if (r.LocationId is not null) a.LocationId = r.LocationId;
        if (r.CompanyId.HasValue) a.CompanyId = r.CompanyId.Value;
        if (r.ModelNumber is not null) a.ModelNumber = r.ModelNumber;
        if (r.OrderNumber is not null) a.OrderNumber = r.OrderNumber;
        if (r.PurchaseCost is not null) a.PurchaseCost = r.PurchaseCost;
        if (r.PurchaseDate is not null) a.PurchaseDate = r.PurchaseDate;
        if (r.Notes is not null) a.Notes = r.Notes;
        if (r.Image is not null) a.Image = r.Image;

        await _context.SaveChangesAsync(cancellationToken);

        // LogMeta: only the fields that actually changed (old → new), same shape as other updates.
        var after = new Dictionary<string, object?>
        {
            ["name"] = a.Name,
            ["itemNo"] = a.ItemNo,
            ["qty"] = a.Qty,
            ["minAmt"] = a.MinAmt,
            ["categoryId"] = a.CategoryId,
            ["manufacturerId"] = a.ManufacturerId,
            ["supplierId"] = a.SupplierId,
            ["locationId"] = a.LocationId,
            ["companyId"] = a.CompanyId,
            ["modelNumber"] = a.ModelNumber,
            ["orderNumber"] = a.OrderNumber,
            ["purchaseCost"] = a.PurchaseCost,
            ["purchaseDate"] = a.PurchaseDate,
            ["notes"] = a.Notes,
            ["image"] = a.Image
        };
        var changes = new Dictionary<string, object?>();
        foreach (var kv in after)
        {
            if (!Equals(before[kv.Key], kv.Value))
                changes[kv.Key] = new { old = before[kv.Key], @new = kv.Value };
        }

        return new UpdateAccessoryResult(
            true,
            "Accessory updated.",
            AccessoryId: a.Id,
            CompanyId: a.CompanyId,
            Note: $"Updated accessory: {a.Name}" + (a.ItemNo != null ? $" (#{a.ItemNo})" : ""),
            LogMeta: JsonSerializer.Serialize(new { changes }));
    }
}
