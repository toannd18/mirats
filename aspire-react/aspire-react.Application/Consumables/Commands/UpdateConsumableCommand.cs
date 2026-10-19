using aspire_react.Server.Application.Common.ActionLogs;
using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Consumables.Commands;

/// <summary>
/// [Giai đoạn 3 — Nặng] PUT /api/v1/consumables/{id} (extracted from ConsumablesController.Update).
/// Verbatim: scope → 404; TWO branches — Confirmed → CONFIRMED_CONSUMABLE_LOCKED (only Location +
/// Notes editable, same-value passes) / Unconfirmed → FIELD_LOCKED on company change when ever
/// checked out + full patch ×14; LogMeta differs per branch (verbatim).
/// </summary>
public record UpdateConsumableCommand(
    Guid Id, string? Name, string? ItemNo, int? Qty, int? MinAmt, Guid? CategoryId,
    Guid? ManufacturerId, Guid? SupplierId, Guid? LocationId, Guid? CompanyId,
    string? ModelNumber, string? OrderNumber, decimal? PurchaseCost, DateTime? PurchaseDate,
    string? Notes, string? Image, Guid CurrentUserId)
    : IRequest<ConsumableResult>;

public class UpdateConsumableCommandHandler : IRequestHandler<UpdateConsumableCommand, ConsumableResult>
{
    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _companyScope;
    private readonly IActionLogService _actionLogService;

    public UpdateConsumableCommandHandler(IApplicationDbContext context, ICompanyScopeService companyScope, IActionLogService actionLogService)
    {
        _context = context;
        _companyScope = companyScope;
        _actionLogService = actionLogService;
    }

    public async Task<ConsumableResult> Handle(UpdateConsumableCommand request, CancellationToken cancellationToken)
    {
        var c = await _context.Consumables.FindAsync(new object?[] { request.Id }, cancellationToken);
        if (c == null)
            return new ConsumableResult(false, "Consumable not found.", "NOT_FOUND");

        // Company scoping: a regular user may only edit consumables of their own company (or floater).
        var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        if (userCompanyId.HasValue && c.CompanyId.HasValue && c.CompanyId.Value != userCompanyId.Value)
            return new ConsumableResult(false, "Consumable not found.", "NOT_FOUND");

        // ─── Confirmed consumables: ONLY Location + Notes remain editable (mirrors Task F Asset:
        // confirmed → only Name/Notes). Everything else is locked post-confirmation. Patch-aware:
        // sending the SAME value as current is allowed; only a DIFFERENT value on a locked field
        // is rejected (so the edit form can submit its loaded values untouched).
        if (c.Status == ConsumableStatus.Confirmed)
        {
            var locked = new List<string>();
            if (request.Name is not null && request.Name != c.Name) locked.Add("name");
            if (request.ItemNo is not null && request.ItemNo != c.ItemNo) locked.Add("itemNo");
            if (request.Qty.HasValue && request.Qty.Value != c.Qty) locked.Add("qty");
            if (request.MinAmt.HasValue && request.MinAmt.Value != c.MinAmt) locked.Add("minAmt");
            if (request.CategoryId is not null && request.CategoryId != c.CategoryId) locked.Add("categoryId");
            if (request.ManufacturerId is not null && request.ManufacturerId != c.ManufacturerId) locked.Add("manufacturerId");
            if (request.SupplierId is not null && request.SupplierId != c.SupplierId) locked.Add("supplierId");
            if (request.CompanyId.HasValue && request.CompanyId.Value != c.CompanyId) locked.Add("companyId");
            if (request.ModelNumber is not null && request.ModelNumber != c.ModelNumber) locked.Add("modelNumber");
            if (request.OrderNumber is not null && request.OrderNumber != c.OrderNumber) locked.Add("orderNumber");
            if (request.PurchaseCost.HasValue && request.PurchaseCost.Value != c.PurchaseCost) locked.Add("purchaseCost");
            if (request.PurchaseDate.HasValue && request.PurchaseDate.Value != c.PurchaseDate) locked.Add("purchaseDate");
            if (request.Image is not null && request.Image != c.Image) locked.Add("image");
            if (locked.Count > 0)
                return new ConsumableResult(false,
                    $"Không thể sửa các trường: {string.Join(", ", locked)}. Vật tư đã xác nhận — chỉ Vị trí và Ghi chú được phép sửa.",
                    "CONFIRMED_CONSUMABLE_LOCKED");
            // [AUDIT-LOG B3] Vật tư ĐÃ XÁC NHẬN ⇒ chỉ Location + Notes sửa được, nên `changes`
            // chỉ chứa đúng 2 field này (trước đây thiếu wrapper "changes" ⇒ timeline không đọc được).
            var changes = new ActionLogChanges()
                .Track("locationId", c.LocationId)
                .Track("notes", c.Notes);
            if (request.LocationId is not null) c.LocationId = request.LocationId;
            if (request.Notes is not null) c.Notes = request.Notes;
            _actionLogService.LogAction(
                itemType: ItemType.Consumable,
                itemId: request.Id,
                actionType: ActionType.Update,
                loggedByUserId: request.CurrentUserId,
                note: $"Cập nhật vật tư: {c.Name}",
                logMeta: changes.Set("locationId", c.LocationId).Set("notes", c.Notes).ToLogMeta(),
                companyId: c.CompanyId);
        }
        else
        {
            // Field-lock company: a consumable that has ever been checked out cannot change company —
            // past checkouts were tied to the old company (mirrors License/Component's FIELD_LOCKED).
            // Patch-aware: only trigger when CompanyId is EXPLICITLY sent and differs.
            if (request.CompanyId.HasValue && request.CompanyId.Value != c.CompanyId
                && await _context.ConsumableCheckouts.AnyAsync(ch => ch.ConsumableId == request.Id, cancellationToken))
                return new ConsumableResult(false, "Vật tư đã từng được cấp phát — không thể đổi công ty.", "FIELD_LOCKED");

            // [AUDIT-LOG B3] Chụp giá trị CŨ của ĐỦ 15 field patch được TRƯỚC khi gán ⇒ LogMeta.changes.
            // [BUG-P FIX] `minAmt` trước đây chụp sau khi gán nên old == new (mất thông tin).
            var changes = new ActionLogChanges()
                .Track("name", c.Name)
                .Track("itemNo", c.ItemNo)
                .Track("qty", c.Qty)
                .Track("minAmt", c.MinAmt)
                .Track("categoryId", c.CategoryId)
                .Track("manufacturerId", c.ManufacturerId)
                .Track("supplierId", c.SupplierId)
                .Track("locationId", c.LocationId)
                .Track("companyId", c.CompanyId)
                .Track("modelNumber", c.ModelNumber)
                .Track("orderNumber", c.OrderNumber)
                .Track("purchaseCost", c.PurchaseCost)
                .Track("purchaseDate", c.PurchaseDate)
                .Track("notes", c.Notes)
                .Track("image", c.Image);

            // ─── Patch semantics (Task M1, mirroring Task F Asset): only fields explicitly sent are applied.
            if (!string.IsNullOrWhiteSpace(request.Name)) c.Name = request.Name;
            if (request.ItemNo is not null) c.ItemNo = request.ItemNo;
            if (request.Qty.HasValue) c.Qty = request.Qty.Value;
            if (request.MinAmt.HasValue) c.MinAmt = request.MinAmt.Value;
            if (request.CategoryId is not null) c.CategoryId = request.CategoryId;
            if (request.ManufacturerId is not null) c.ManufacturerId = request.ManufacturerId;
            if (request.SupplierId is not null) c.SupplierId = request.SupplierId;
            if (request.LocationId is not null) c.LocationId = request.LocationId;
            if (request.CompanyId.HasValue) c.CompanyId = request.CompanyId.Value;
            if (request.ModelNumber is not null) c.ModelNumber = request.ModelNumber;
            if (request.OrderNumber is not null) c.OrderNumber = request.OrderNumber;
            if (request.PurchaseCost is not null) c.PurchaseCost = request.PurchaseCost;
            if (request.PurchaseDate is not null) c.PurchaseDate = request.PurchaseDate;
            if (request.Notes is not null) c.Notes = request.Notes;
            if (request.Image is not null) c.Image = request.Image;

            _actionLogService.LogAction(
                itemType: ItemType.Consumable,
                itemId: request.Id,
                actionType: ActionType.Update,
                loggedByUserId: request.CurrentUserId,
                note: $"Cập nhật vật tư: {c.Name}",
                logMeta: changes
                    .Set("name", c.Name)
                    .Set("itemNo", c.ItemNo)
                    .Set("qty", c.Qty)
                    .Set("minAmt", c.MinAmt)
                    .Set("categoryId", c.CategoryId)
                    .Set("manufacturerId", c.ManufacturerId)
                    .Set("supplierId", c.SupplierId)
                    .Set("locationId", c.LocationId)
                    .Set("companyId", c.CompanyId)
                    .Set("modelNumber", c.ModelNumber)
                    .Set("orderNumber", c.OrderNumber)
                    .Set("purchaseCost", c.PurchaseCost)
                    .Set("purchaseDate", c.PurchaseDate)
                    .Set("notes", c.Notes)
                    .Set("image", c.Image)
                    .ToLogMeta(),
                companyId: c.CompanyId);
        }
        await _context.SaveChangesAsync(cancellationToken);
        return new ConsumableResult(true, "Consumable updated.");
    }
}
