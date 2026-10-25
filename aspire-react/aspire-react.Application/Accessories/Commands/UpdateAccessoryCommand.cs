using aspire_react.Server.Application.Common.ActionLogs;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using aspire_react.Server.Application.Common.CompanyScope;
using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Application.Common.Validation;
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

        // [DF-8] Non-superuser cannot move to another company or clear to floater.
        if (!await CompanyAssignmentPolicy.CanChangeAsync(_companyScope, r.CompanyId))
            return new UpdateAccessoryResult(false, "Bạn không được đổi công ty của phụ kiện.", ErrorCode: "FORBIDDEN");

        // CompanyId-lock after any checkout history (mirrors Consumable/License): past checkouts were
        // tied to the old company. Patch-aware — only when CompanyId is explicitly sent and differs.
        if (r.CompanyId.HasValue && OptionalForeignKey.Cleared(r.CompanyId) != a.CompanyId
            && await _context.AccessoryCheckouts.AnyAsync(ch => ch.AccessoryId == r.Id, cancellationToken))
            return new UpdateAccessoryResult(false,
                "Phụ kiện đã từng được cấp phát — không thể đổi công ty.", ErrorCode: "FIELD_LOCKED");

        var hasActiveCheckouts = await _context.AccessoryCheckouts
            .AnyAsync(ch => ch.AccessoryId == r.Id && ch.AssignedQty > ch.ReturnedQty, cancellationToken);
        if (hasActiveCheckouts)
            // Verbatim: this body has NO error_code (only status + message).
            return new UpdateAccessoryResult(false, "Không thể sửa phụ kiện đang có thiết bị đang được cấp phát.");

        // [FIX-GUID-EMPTY phần 2 — 2026-10-20] FK TÙY CHỌN: vắng (null) = giữ nguyên; Guid.Empty = XÓA liên
        // kết (null); id thật phải TỒN TẠI — trước đây id lạ/Guid.Empty ghi thẳng vào cột FK ⇒ 23503 ⇒ 500.
        var newCategoryId = OptionalForeignKey.Cleared(r.CategoryId);
        var newManufacturerId = OptionalForeignKey.Cleared(r.ManufacturerId);
        var newSupplierId = OptionalForeignKey.Cleared(r.SupplierId);
        var newLocationId = OptionalForeignKey.Cleared(r.LocationId);
        var newCompanyId = OptionalForeignKey.Cleared(r.CompanyId);
        if (newCategoryId is not null
            && !await _context.Categories.AnyAsync(x => x.Id == newCategoryId, cancellationToken))
            return new UpdateAccessoryResult(false, OptionalForeignKey.NotFoundMessage("categoryId"), ErrorCode: "RESOURCE_NOT_FOUND");
        if (newManufacturerId is not null
            && !await _context.Manufacturers.AnyAsync(x => x.Id == newManufacturerId, cancellationToken))
            return new UpdateAccessoryResult(false, OptionalForeignKey.NotFoundMessage("manufacturerId"), ErrorCode: "RESOURCE_NOT_FOUND");
        if (newSupplierId is not null
            && !await _context.Suppliers.AnyAsync(x => x.Id == newSupplierId, cancellationToken))
            return new UpdateAccessoryResult(false, OptionalForeignKey.NotFoundMessage("supplierId"), ErrorCode: "RESOURCE_NOT_FOUND");
        if (newLocationId is not null
            && !await _context.Locations.AnyAsync(x => x.Id == newLocationId, cancellationToken))
            return new UpdateAccessoryResult(false, OptionalForeignKey.NotFoundMessage("locationId"), ErrorCode: "RESOURCE_NOT_FOUND");
        if (newCompanyId is not null
            && !await _context.Companies.AnyAsync(x => x.Id == newCompanyId, cancellationToken))
            return new UpdateAccessoryResult(false, OptionalForeignKey.NotFoundMessage("companyId"), ErrorCode: "RESOURCE_NOT_FOUND");

        // [AUDIT-LOG B6a] helper dùng chung: Track BEFORE mutation (giá trị cũ), Set AFTER (giá trị mới).
        var changes = new ActionLogChanges()
            .Track("name", a.Name)
            .Track("itemNo", a.ItemNo)
            .Track("qty", a.Qty)
            .Track("minAmt", a.MinAmt)
            .Track("categoryId", a.CategoryId)
            .Track("manufacturerId", a.ManufacturerId)
            .Track("supplierId", a.SupplierId)
            .Track("locationId", a.LocationId)
            .Track("companyId", a.CompanyId)
            .Track("modelNumber", a.ModelNumber)
            .Track("orderNumber", a.OrderNumber)
            .Track("purchaseCost", a.PurchaseCost)
            .Track("purchaseDate", a.PurchaseDate)
            .Track("notes", a.Notes)
            .Track("image", a.Image);

        // Task M2 patch semantics: only fields explicitly sent are applied.
        if (!string.IsNullOrWhiteSpace(r.Name)) a.Name = r.Name;
        if (r.ItemNo is not null) a.ItemNo = r.ItemNo;
        if (r.Qty.HasValue) a.Qty = r.Qty.Value;
        if (r.MinAmt.HasValue) a.MinAmt = r.MinAmt.Value;
        if (r.CategoryId is not null) a.CategoryId = newCategoryId;
        if (r.ManufacturerId is not null) a.ManufacturerId = newManufacturerId;
        if (r.SupplierId is not null) a.SupplierId = newSupplierId;
        if (r.LocationId is not null) a.LocationId = newLocationId;
        if (r.CompanyId.HasValue) a.CompanyId = newCompanyId;
        if (r.ModelNumber is not null) a.ModelNumber = r.ModelNumber;
        if (r.OrderNumber is not null) a.OrderNumber = r.OrderNumber;
        if (r.PurchaseCost is not null) a.PurchaseCost = r.PurchaseCost;
        if (r.PurchaseDate is not null) a.PurchaseDate = r.PurchaseDate;
        if (r.Notes is not null) a.Notes = r.Notes;
        if (r.Image is not null) a.Image = r.Image;

        await _context.SaveChangesAsync(cancellationToken);

        // LogMeta: chỉ field thực sự đổi (old → new); null khi không field nào đổi.
        var logMeta = changes
            .Set("name", a.Name)
            .Set("itemNo", a.ItemNo)
            .Set("qty", a.Qty)
            .Set("minAmt", a.MinAmt)
            .Set("categoryId", a.CategoryId)
            .Set("manufacturerId", a.ManufacturerId)
            .Set("supplierId", a.SupplierId)
            .Set("locationId", a.LocationId)
            .Set("companyId", a.CompanyId)
            .Set("modelNumber", a.ModelNumber)
            .Set("orderNumber", a.OrderNumber)
            .Set("purchaseCost", a.PurchaseCost)
            .Set("purchaseDate", a.PurchaseDate)
            .Set("notes", a.Notes)
            .Set("image", a.Image)
            .ToLogMeta();

        return new UpdateAccessoryResult(
            true,
            "Accessory updated.",
            AccessoryId: a.Id,
            CompanyId: a.CompanyId,
            Note: $"Cập nhật phụ kiện: {a.Name}" + (a.ItemNo != null ? $" (#{a.ItemNo})" : ""),
            LogMeta: logMeta);
    }
}
