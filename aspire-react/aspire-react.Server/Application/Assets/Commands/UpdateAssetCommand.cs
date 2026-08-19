using System.Text.Json;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using aspire_react.Server.Infrastructure.Persistence;
using aspire_react.Server.Infrastructure.Services;
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
    private readonly AppDbContext _context;
    private readonly IActionLogService _actionLogService;
    private readonly ICompanyScopeService _companyScope;

    public UpdateAssetCommandHandler(AppDbContext context, IActionLogService actionLogService, ICompanyScopeService companyScope)
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
            if (request.ModelId is not null && request.ModelId != asset.ModelId) blockedFields.Add("ModelId");
            if (request.LocationId is not null && request.LocationId != asset.LocationId) blockedFields.Add("LocationId");
            if (request.SupplierId is not null && request.SupplierId != asset.SupplierId) blockedFields.Add("SupplierId");
            if (request.CompanyId is not null && request.CompanyId != asset.CompanyId) blockedFields.Add("CompanyId");
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
                    note: $"Update blocked (confirmed asset): {string.Join(", ", blockedFields)}",
                    logMeta: JsonSerializer.Serialize(new { blockedFields }));
                return new AssetResult(false, $"Không thể sửa các trường: {string.Join(", ", blockedFields)}. Chỉ Name và Notes được phép sửa sau khi xác nhận.", ErrorCode: "CONFIRMED_ASSET_LOCKED");
            }
        }

        // ──── Apply patch (chỉ các field được gửi tường minh) + Track changes ────
        // KHÔNG overwrite field absent — payload một phần (VD chỉ Name/Notes) phải giữ nguyên
        // giá trị cũ của các field khác (AssetTag/Serial/...), tránh xóa nhầm dữ liệu.
        var changes = new Dictionary<string, object?>();
        void Track(string field, object? oldVal, object? newVal)
        {
            if (!EqualityComparer<object>.Default.Equals(oldVal, newVal))
                changes[field] = new { old = oldVal, @new = newVal };
        }

        if (!string.IsNullOrEmpty(request.AssetTag) && request.AssetTag != asset.AssetTag)
        {
            Track("asset_tag", asset.AssetTag, request.AssetTag);
            asset.AssetTag = request.AssetTag;
        }
        // Task M2: guard Name exactly like AssetTag — a partial payload that omits `name` (or sends it
        // empty) must NOT be treated as "change to empty" and wipe the existing name. Name is required,
        // so keeping the current value on absent/empty is correct.
        if (!string.IsNullOrWhiteSpace(request.Name) && request.Name != asset.Name)
        {
            Track("name", asset.Name, request.Name);
            asset.Name = request.Name;
        }
        if (request.Serial is not null && request.Serial != asset.Serial)
        {
            Track("serial", asset.Serial, request.Serial);
            asset.Serial = request.Serial;
        }
        if (request.Image is not null && request.Image != asset.Image)
        {
            Track("image", asset.Image, request.Image);
            asset.Image = request.Image;
        }
        if (request.ModelId is not null && request.ModelId != asset.ModelId)
        {
            Track("model_id", asset.ModelId, request.ModelId);
            asset.ModelId = request.ModelId;
        }
        if (request.LocationId is not null && request.LocationId != asset.LocationId)
        {
            Track("location_id", asset.LocationId, request.LocationId);
            asset.LocationId = request.LocationId;
        }
        if (request.SupplierId is not null && request.SupplierId != asset.SupplierId)
        {
            Track("supplier_id", asset.SupplierId, request.SupplierId);
            asset.SupplierId = request.SupplierId;
        }
        if (request.CompanyId is not null && request.CompanyId != asset.CompanyId)
        {
            Track("company_id", asset.CompanyId, request.CompanyId);
            asset.CompanyId = request.CompanyId;
        }
        if (request.PurchaseCost is not null && request.PurchaseCost != asset.PurchaseCost)
        {
            Track("purchase_cost", asset.PurchaseCost, request.PurchaseCost);
            asset.PurchaseCost = request.PurchaseCost;
        }
        if (request.PurchaseDate is not null && request.PurchaseDate != asset.PurchaseDate)
        {
            Track("purchase_date", asset.PurchaseDate, request.PurchaseDate);
            asset.PurchaseDate = request.PurchaseDate;
        }
        if (request.WarrantyMonths is not null && request.WarrantyMonths != asset.WarrantyMonths)
        {
            Track("warranty_months", asset.WarrantyMonths, request.WarrantyMonths);
            asset.WarrantyMonths = request.WarrantyMonths;
        }
        if (request.OrderNumber is not null && request.OrderNumber != asset.OrderNumber)
        {
            Track("order_number", asset.OrderNumber, request.OrderNumber);
            asset.OrderNumber = request.OrderNumber;
        }
        if (request.Physical.HasValue && request.Physical.Value != asset.Physical)
        {
            Track("physical", asset.Physical, request.Physical.Value);
            asset.Physical = request.Physical.Value;
        }
        if (request.Requestable.HasValue && request.Requestable.Value != asset.Requestable)
        {
            Track("requestable", asset.Requestable, request.Requestable.Value);
            asset.Requestable = request.Requestable.Value;
        }
        if (request.Notes is not null && request.Notes != asset.Notes)
        {
            Track("notes", asset.Notes, request.Notes);
            asset.Notes = request.Notes;
        }

        if (changes.Count > 0)
        {
            _actionLogService.LogAction(
                itemType: ItemType.Asset,
                itemId: asset.Id,
                actionType: ActionType.Update,
                loggedByUserId: request.CurrentUserId,
                companyId: asset.CompanyId,
                note: $"Updated asset: {asset.AssetTag} - {asset.Name}",
                logMeta: JsonSerializer.Serialize(new { changes }));
        }

        await _context.SaveChangesAsync(cancellationToken);
        return new AssetResult(true, "Asset updated successfully.", AssetId: asset.Id);
    }
}