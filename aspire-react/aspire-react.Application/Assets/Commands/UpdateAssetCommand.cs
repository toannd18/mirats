using System.Text.Json;
using aspire_react.Server.Application.Common.ActionLogs;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Application.Common.Validation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Assets.Commands;

public record UpdateAssetCommand : IRequest<AssetResult>
{
    public Guid Id { get; init; }
    public string AssetTag { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Serial { get; init; }
    public string? Image { get; init; }
    public Guid? ModelId { get; init; }
    public Guid? LocationId { get; init; }
    public Guid? SupplierId { get; init; }
    public Guid? CompanyId { get; init; }
    public decimal? PurchaseCost { get; init; }
    public DateTime? PurchaseDate { get; init; }
    public int? WarrantyMonths { get; init; }
    public bool? Physical { get; init; }
    public bool? Requestable { get; init; }
    public string? OrderNumber { get; init; }
    public string? Notes { get; init; }
    public Guid CurrentUserId { get; init; }
}

public class UpdateAssetCommandHandler : IRequestHandler<UpdateAssetCommand, AssetResult>
{
    private readonly IApplicationDbContext _context;
    private readonly IActionLogService _actionLogService;
    private readonly ICompanyScopeService _companyScope;

    public UpdateAssetCommandHandler(IApplicationDbContext context, IActionLogService actionLogService, ICompanyScopeService companyScope)
    {
        _context = context;
        _actionLogService = actionLogService;
        _companyScope = companyScope;
    }

    public async Task<AssetResult> Handle(UpdateAssetCommand request, CancellationToken cancellationToken)
    {
        var asset = await _context.Assets.FindAsync([request.Id], cancellationToken);
        if (asset == null)
            return new AssetResult(false, "Asset not found.", ErrorCode: "NOT_FOUND");

        // Company scoping: a regular user may only edit assets of their own company (or floater).
        // Superuser (userCompanyId == null) bypasses. Same "hide existence" convention as GetAsset.
        var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        if (userCompanyId.HasValue && asset.CompanyId.HasValue && asset.CompanyId.Value != userCompanyId.Value)
            return new AssetResult(false, "Asset not found.", ErrorCode: "NOT_FOUND");

        if (asset.Status == AssetStatus.Archived)
            return new AssetResult(false, "Không thể sửa tài sản đã lưu trữ.", ErrorCode: "ASSET_ARCHIVED");

        // AssetTag uniqueness check
        if (!string.IsNullOrEmpty(request.AssetTag))
        {
            var duplicate = await _context.Assets.AnyAsync(a => a.AssetTag == request.AssetTag && a.Id != request.Id, cancellationToken);
            if (duplicate)
                return new AssetResult(false, "Mã tài sản đã tồn tại.", ErrorCode: "DUPLICATE_ASSET_TAG");
        }

        // ──── IsConfirmed gate (patch-aware) ────
        // Rule (đã xác nhận): asset ĐÃ confirmed → CHỈ Name/Notes được sửa; asset CHƯA confirmed
        // → sửa được MỌI field. Chỉ các field mà request EXPLICITLY gửi (khác null) và khác giá
        // trị hiện tại mới bị coi là "thay đổi" — field absent (null/default) KHÔNG bao giờ bị
        // flag, để payload một phần (VD chỉ Name/Notes) không bị chặn nhầm trên asset confirmed.
        if (asset.IsConfirmed)
        {
            // Only Name and Notes are editable after confirmation
            var blockedFields = new List<string>();
            if (request.Serial is not null && request.Serial != asset.Serial) blockedFields.Add("Serial");
            if (request.ModelId is not null && OptionalForeignKey.Cleared(request.ModelId) != asset.ModelId) blockedFields.Add("ModelId");
            if (request.LocationId is not null && OptionalForeignKey.Cleared(request.LocationId) != asset.LocationId) blockedFields.Add("LocationId");
            if (request.SupplierId is not null && OptionalForeignKey.Cleared(request.SupplierId) != asset.SupplierId) blockedFields.Add("SupplierId");
            if (request.CompanyId is not null && OptionalForeignKey.Cleared(request.CompanyId) != asset.CompanyId) blockedFields.Add("CompanyId");
            if (request.PurchaseCost is not null && request.PurchaseCost != asset.PurchaseCost) blockedFields.Add("PurchaseCost");
            if (request.PurchaseDate is not null && request.PurchaseDate != asset.PurchaseDate) blockedFields.Add("PurchaseDate");
            if (request.WarrantyMonths is not null && request.WarrantyMonths != asset.WarrantyMonths) blockedFields.Add("WarrantyMonths");
            if (request.OrderNumber is not null && request.OrderNumber != asset.OrderNumber) blockedFields.Add("OrderNumber");
            if (request.Physical.HasValue && request.Physical.Value != asset.Physical) blockedFields.Add("Physical");
            if (request.Requestable.HasValue && request.Requestable.Value != asset.Requestable) blockedFields.Add("Requestable");

            if (blockedFields.Count > 0)
            {
                _actionLogService.LogAction(
                    itemType: ItemType.Asset,
                    itemId: asset.Id,
                    actionType: ActionType.UpdateRejected,
                    loggedByUserId: request.CurrentUserId,
                    companyId: asset.CompanyId,
                    note: $"Cập nhật bị từ chối (thiết bị đã xác nhận): {string.Join(", ", blockedFields)}",
                    logMeta: JsonSerializer.Serialize(new { blockedFields }));
                return new AssetResult(false, $"Không thể sửa các trường: {string.Join(", ", blockedFields)}. Chỉ Name và Notes được phép sửa sau khi xác nhận.", ErrorCode: "CONFIRMED_ASSET_LOCKED");
            }
        }

        // ──── Apply patch (chỉ các field được gửi tường minh) + Track changes ────
        // KHÔNG overwrite field absent — payload một phần (VD chỉ Name/Notes) phải giữ nguyên
        // giá trị cũ của các field khác (AssetTag/Serial/...), tránh xóa nhầm dữ liệu.
        // [AUDIT-LOG B6a] helper dùng chung: Track TRƯỚC khi gán (giá trị cũ), Set SAU (giá trị mới).
        var changes = new ActionLogChanges()
            .Track("assetTag", asset.AssetTag)
            .Track("name", asset.Name)
            .Track("serial", asset.Serial)
            .Track("image", asset.Image)
            .Track("modelId", asset.ModelId)
            .Track("locationId", asset.LocationId)
            .Track("supplierId", asset.SupplierId)
            .Track("companyId", asset.CompanyId)
            .Track("purchaseCost", asset.PurchaseCost)
            .Track("purchaseDate", asset.PurchaseDate)
            .Track("warrantyMonths", asset.WarrantyMonths)
            .Track("orderNumber", asset.OrderNumber)
            .Track("physical", asset.Physical)
            .Track("requestable", asset.Requestable)
            .Track("notes", asset.Notes);

        // [FIX-GUID-EMPTY phần 2 — 2026-10-20] Quy ước FK TÙY CHỌN: vắng (null) = giữ nguyên;
        // Guid.Empty = XÓA liên kết (null); id thật phải TỒN TẠI (nếu không → 400 RESOURCE_NOT_FOUND,
        // thay vì để Postgres 23503 nổ ra 500). Xem Common/Validation/OptionalForeignKey.cs.
        var newModelId = OptionalForeignKey.Cleared(request.ModelId);
        var newLocationId = OptionalForeignKey.Cleared(request.LocationId);
        var newSupplierId = OptionalForeignKey.Cleared(request.SupplierId);
        var newCompanyId = OptionalForeignKey.Cleared(request.CompanyId);
        if (newModelId is not null
            && !await _context.Models.AnyAsync(m => m.Id == newModelId, cancellationToken))
            return new AssetResult(false, OptionalForeignKey.NotFoundMessage("modelId"), ErrorCode: "RESOURCE_NOT_FOUND");
        if (newLocationId is not null
            && !await _context.Locations.AnyAsync(l => l.Id == newLocationId, cancellationToken))
            return new AssetResult(false, OptionalForeignKey.NotFoundMessage("locationId"), ErrorCode: "RESOURCE_NOT_FOUND");
        if (newSupplierId is not null
            && !await _context.Suppliers.AnyAsync(s => s.Id == newSupplierId, cancellationToken))
            return new AssetResult(false, OptionalForeignKey.NotFoundMessage("supplierId"), ErrorCode: "RESOURCE_NOT_FOUND");
        if (newCompanyId is not null
            && !await _context.Companies.AnyAsync(c => c.Id == newCompanyId, cancellationToken))
            return new AssetResult(false, OptionalForeignKey.NotFoundMessage("companyId"), ErrorCode: "RESOURCE_NOT_FOUND");

        if (!string.IsNullOrEmpty(request.AssetTag) && request.AssetTag != asset.AssetTag)
        {
            asset.AssetTag = request.AssetTag;
        }
        // Task M2: guard Name exactly like AssetTag — a partial payload that omits `name` (or sends it
        // empty) must NOT be treated as "change to empty" and wipe the existing name. Name is required,
        // so keeping the current value on absent/empty is correct.
        if (!string.IsNullOrWhiteSpace(request.Name) && request.Name != asset.Name)
        {
            asset.Name = request.Name;
        }
        if (request.Serial is not null && request.Serial != asset.Serial)
        {
            asset.Serial = request.Serial;
        }
        if (request.Image is not null && request.Image != asset.Image)
        {
            asset.Image = request.Image;
        }
        if (request.ModelId is not null && request.ModelId != asset.ModelId)
        {
            asset.ModelId = newModelId;
        }
        if (request.LocationId is not null && request.LocationId != asset.LocationId)
        {
            asset.LocationId = newLocationId;
        }
        if (request.SupplierId is not null && request.SupplierId != asset.SupplierId)
        {
            asset.SupplierId = newSupplierId;
        }
        if (request.CompanyId is not null && request.CompanyId != asset.CompanyId)
        {
            asset.CompanyId = newCompanyId;
        }
        if (request.PurchaseCost is not null && request.PurchaseCost != asset.PurchaseCost)
        {
            asset.PurchaseCost = request.PurchaseCost;
        }
        if (request.PurchaseDate is not null && request.PurchaseDate != asset.PurchaseDate)
        {
            asset.PurchaseDate = request.PurchaseDate;
        }
        if (request.WarrantyMonths is not null && request.WarrantyMonths != asset.WarrantyMonths)
        {
            asset.WarrantyMonths = request.WarrantyMonths;
        }
        if (request.OrderNumber is not null && request.OrderNumber != asset.OrderNumber)
        {
            asset.OrderNumber = request.OrderNumber;
        }
        if (request.Physical.HasValue && request.Physical.Value != asset.Physical)
        {
            asset.Physical = request.Physical.Value;
        }
        if (request.Requestable.HasValue && request.Requestable.Value != asset.Requestable)
        {
            asset.Requestable = request.Requestable.Value;
        }
        if (request.Notes is not null && request.Notes != asset.Notes)
        {
            asset.Notes = request.Notes;
        }

        changes
            .Set("assetTag", asset.AssetTag)
            .Set("name", asset.Name)
            .Set("serial", asset.Serial)
            .Set("image", asset.Image)
            .Set("modelId", asset.ModelId)
            .Set("locationId", asset.LocationId)
            .Set("supplierId", asset.SupplierId)
            .Set("companyId", asset.CompanyId)
            .Set("purchaseCost", asset.PurchaseCost)
            .Set("purchaseDate", asset.PurchaseDate)
            .Set("warrantyMonths", asset.WarrantyMonths)
            .Set("orderNumber", asset.OrderNumber)
            .Set("physical", asset.Physical)
            .Set("requestable", asset.Requestable)
            .Set("notes", asset.Notes);

        if (changes.HasChanges)
        {
            _actionLogService.LogAction(
                itemType: ItemType.Asset,
                itemId: asset.Id,
                actionType: ActionType.Update,
                loggedByUserId: request.CurrentUserId,
                companyId: asset.CompanyId,
                note: $"Cập nhật thiết bị: {asset.AssetTag} - {asset.Name}",
                logMeta: changes.ToLogMeta());
        }

        await _context.SaveChangesAsync(cancellationToken);
        return new AssetResult(true, "Asset updated successfully.", AssetId: asset.Id);
    }
}