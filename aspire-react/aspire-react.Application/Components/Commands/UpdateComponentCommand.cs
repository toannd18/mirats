using aspire_react.Server.Application.Common.ActionLogs;
using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Application.Common.Validation;
using Microsoft.EntityFrameworkCore;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using MediatR;

namespace aspire_react.Server.Application.Components.Commands;

/// <summary>
/// [Giai đoạn 3 — Nặng] PUT /api/v1/components/{id} (extracted from ComponentsController.Update).
/// Verbatim: scope → 404; FIELD_LOCKED (categoryId/companyId/trackingType rejected when DIFFERENT);
/// patch semantics (Qty/Serial/ItemNo silently ignored — Qty read-only); log via IActionLogService
/// INSIDE the same SaveChanges as data (NOT ILoggableCommand — verbatim log ordering, and this
/// flow has no explicit transaction so the behavior ordering is equivalent but kept identical).
/// </summary>
public record UpdateComponentCommand(
    Guid Id, string? Name, string? Notes, Guid? SupplierId, Guid? ManufacturerId,
    string? ModelNumber, int? MinAmt, Guid? LocationId, string? OrderNumber, decimal? PurchaseCost,
    DateTime? PurchaseDate, Guid? CategoryId, Guid? CompanyId, TrackingType? TrackingType, Guid CurrentUserId)
    : IRequest<ComponentOperationResult>;

public class UpdateComponentCommandHandler : IRequestHandler<UpdateComponentCommand, ComponentOperationResult>
{
    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _companyScope;
    private readonly IActionLogService _actionLogService;

    public UpdateComponentCommandHandler(IApplicationDbContext context, ICompanyScopeService companyScope, IActionLogService actionLogService)
    {
        _context = context;
        _companyScope = companyScope;
        _actionLogService = actionLogService;
    }

    public async Task<ComponentOperationResult> Handle(UpdateComponentCommand request, CancellationToken cancellationToken)
    {
        var c = await _context.Components.FindAsync(new object?[] { request.Id }, cancellationToken);
        if (c == null)
            return new ComponentOperationResult(false, "Component not found.", "NOT_FOUND");

        // Company scoping: a regular user may only edit components of their own company (or floater).
        var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        if (userCompanyId.HasValue && c.CompanyId.HasValue && c.CompanyId.Value != userCompanyId.Value)
            return new ComponentOperationResult(false, "Component not found.", "NOT_FOUND");

        // ─── Locked fields: cannot be changed via update ───
        // Reject when the payload carries a value DIFFERENT from the current one so the user
        // knows these fields are immutable (not silently ignored). Sending the same value is fine.
        var locked = new List<string>();
        if (request.CategoryId.HasValue && OptionalForeignKey.Cleared(request.CategoryId) != c.CategoryId) locked.Add("categoryId");
        if (request.CompanyId.HasValue && OptionalForeignKey.Cleared(request.CompanyId) != c.CompanyId) locked.Add("companyId");
        if (request.TrackingType.HasValue && request.TrackingType.Value != c.TrackingType) locked.Add("trackingType");
        if (locked.Count > 0)
            return new ComponentOperationResult(false,
                $"Không thể thay đổi (field đã khóa): {string.Join(", ", locked)}. Tracking type, Category và Company chỉ xác định lúc tạo.",
                "FIELD_LOCKED");

        // [AUDIT-LOG B2] Chụp giá trị CŨ trước khi gán (Task M1 whitelist bên dưới) → LogMeta.changes.
        // [FIX-GUID-EMPTY phần 2 — 2026-10-20] FK TÙY CHỌN: vắng = giữ; Guid.Empty = xóa liên kết (null);
        // id thật phải TỒN TẠI ⇒ trước đây id lạ/Guid.Empty ghi thẳng vào cột FK ⇒ Postgres 23503 ⇒ 500.
        var newSupplierId = OptionalForeignKey.Cleared(request.SupplierId);
        var newManufacturerId = OptionalForeignKey.Cleared(request.ManufacturerId);
        var newLocationId = OptionalForeignKey.Cleared(request.LocationId);
        if (newSupplierId is not null
            && !await _context.Suppliers.AnyAsync(s => s.Id == newSupplierId, cancellationToken))
            return new ComponentOperationResult(false, OptionalForeignKey.NotFoundMessage("supplierId"), "RESOURCE_NOT_FOUND");
        if (newManufacturerId is not null
            && !await _context.Manufacturers.AnyAsync(m => m.Id == newManufacturerId, cancellationToken))
            return new ComponentOperationResult(false, OptionalForeignKey.NotFoundMessage("manufacturerId"), "RESOURCE_NOT_FOUND");
        if (newLocationId is not null
            && !await _context.Locations.AnyAsync(l => l.Id == newLocationId, cancellationToken))
            return new ComponentOperationResult(false, OptionalForeignKey.NotFoundMessage("locationId"), "RESOURCE_NOT_FOUND");

        var changes = new ActionLogChanges()
            .Track("name", c.Name)
            .Track("notes", c.Notes)
            .Track("supplierId", c.SupplierId)
            .Track("manufacturerId", c.ManufacturerId)
            .Track("modelNumber", c.ModelNumber)
            .Track("minAmt", c.MinAmt)
            .Track("locationId", c.LocationId)
            .Track("orderNumber", c.OrderNumber)
            .Track("purchaseCost", c.PurchaseCost)
            .Track("purchaseDate", c.PurchaseDate);

        // ─── Patch semantics (Task M1, mirroring Task F Asset): only fields EXPLICITLY sent
        // (non-null) are applied. A partial payload (e.g. only Name/Notes) must NOT wipe the other
        // fields back to null/empty. Qty/Serial/ItemNo stay silently ignored (Qty is read-only).
        if (!string.IsNullOrWhiteSpace(request.Name)) c.Name = request.Name;
        c.Notes = request.Notes ?? c.Notes;
        if (request.SupplierId is not null) c.SupplierId = newSupplierId;
        if (request.ManufacturerId is not null) c.ManufacturerId = newManufacturerId;
        if (request.ModelNumber is not null) c.ModelNumber = request.ModelNumber;
        if (request.MinAmt.HasValue) c.MinAmt = request.MinAmt.Value;
        if (request.LocationId is not null) c.LocationId = newLocationId;
        if (request.OrderNumber is not null) c.OrderNumber = request.OrderNumber;
        if (request.PurchaseCost is not null) c.PurchaseCost = request.PurchaseCost;
        if (request.PurchaseDate is not null) c.PurchaseDate = request.PurchaseDate;

        // [AUDIT-LOG B2] Giá trị MỚI sau khi gán → LogMeta.changes (null khi không field nào đổi).
        var logMeta = changes
            .Set("name", c.Name)
            .Set("notes", c.Notes)
            .Set("supplierId", c.SupplierId)
            .Set("manufacturerId", c.ManufacturerId)
            .Set("modelNumber", c.ModelNumber)
            .Set("minAmt", c.MinAmt)
            .Set("locationId", c.LocationId)
            .Set("orderNumber", c.OrderNumber)
            .Set("purchaseCost", c.PurchaseCost)
            .Set("purchaseDate", c.PurchaseDate)
            .ToLogMeta();

        _actionLogService.Log(new ActionLogEntry { ItemType = ItemType.Component, ItemId = request.Id, ActionType = ActionType.Update, CreatedBy = request.CurrentUserId, CompanyId = c.CompanyId, Note = $"Cập nhật linh kiện \"{c.Name}\"", LogMeta = logMeta });
        await _context.SaveChangesAsync(cancellationToken);
        return new ComponentOperationResult(true, "Component updated.");
    }
}
