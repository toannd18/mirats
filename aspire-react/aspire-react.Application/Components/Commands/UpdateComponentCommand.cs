using aspire_react.Server.Application.Common.Interfaces;
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
        if (request.CategoryId.HasValue && request.CategoryId.Value != c.CategoryId) locked.Add("categoryId");
        if (request.CompanyId.HasValue && request.CompanyId.Value != c.CompanyId) locked.Add("companyId");
        if (request.TrackingType.HasValue && request.TrackingType.Value != c.TrackingType) locked.Add("trackingType");
        if (locked.Count > 0)
            return new ComponentOperationResult(false,
                $"Không thể thay đổi (field đã khóa): {string.Join(", ", locked)}. Tracking type, Category và Company chỉ xác định lúc tạo.",
                "FIELD_LOCKED");

        // ─── Patch semantics (Task M1, mirroring Task F Asset): only fields EXPLICITLY sent
        // (non-null) are applied. A partial payload (e.g. only Name/Notes) must NOT wipe the other
        // fields back to null/empty. Qty/Serial/ItemNo stay silently ignored (Qty is read-only).
        if (!string.IsNullOrWhiteSpace(request.Name)) c.Name = request.Name;
        c.Notes = request.Notes ?? c.Notes;
        if (request.SupplierId is not null) c.SupplierId = request.SupplierId;
        if (request.ManufacturerId is not null) c.ManufacturerId = request.ManufacturerId;
        if (request.ModelNumber is not null) c.ModelNumber = request.ModelNumber;
        if (request.MinAmt.HasValue) c.MinAmt = request.MinAmt.Value;
        if (request.LocationId is not null) c.LocationId = request.LocationId;
        if (request.OrderNumber is not null) c.OrderNumber = request.OrderNumber;
        if (request.PurchaseCost is not null) c.PurchaseCost = request.PurchaseCost;
        if (request.PurchaseDate is not null) c.PurchaseDate = request.PurchaseDate;

        _actionLogService.Log(new ActionLogEntry { ItemType = ItemType.Component, ItemId = request.Id, ActionType = ActionType.Update, CreatedBy = request.CurrentUserId, CompanyId = c.CompanyId, Note = $"Cập nhật linh kiện \"{c.Name}\"" });
        await _context.SaveChangesAsync(cancellationToken);
        return new ComponentOperationResult(true, "Component updated.");
    }
}
