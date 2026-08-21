using System.Text.Json;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using aspire_react.Server.Infrastructure.Persistence;
using aspire_react.Server.Infrastructure.Services;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Assets.Commands;

public record CreateAssetCommand : IRequest<AssetResult>
{
    public string AssetTag { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Serial { get; init; }
    public string? Image { get; init; }
    public Guid? ModelId { get; init; }
    public Guid? StatusId { get; init; }
    public Guid? LocationId { get; init; }
    public Guid? RtdLocationId { get; init; }
    public Guid? SupplierId { get; init; }
    public Guid? CompanyId { get; init; }
    public decimal? PurchaseCost { get; init; }
    public DateTime? PurchaseDate { get; init; }
    public int? WarrantyMonths { get; init; }
    public bool Physical { get; init; } = true;
    public bool Requestable { get; init; }
    public string? OrderNumber { get; init; }
    public string? Notes { get; init; }
    public Guid CurrentUserId { get; init; }
}

public record AssetResult(bool Success, string Message, Guid? AssetId = null, string? ErrorCode = null);

public class CreateAssetCommandValidator : AbstractValidator<CreateAssetCommand>
{
    private readonly AppDbContext _context;
    public CreateAssetCommandValidator(AppDbContext context)
    {
        _context = context;
        // [Task ASSET-TAG-AUTO] AssetTag is now OPTIONAL: empty/null → auto-generated. Only bound its length.
        RuleFor(x => x.AssetTag).MaximumLength(255);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(255);
        RuleFor(x => x.AssetTag)
            .MustAsync(async (tag, ct) => string.IsNullOrWhiteSpace(tag) ||
                !await _context.Assets.AnyAsync(a => a.AssetTag == tag, ct))
            .WithMessage("Mã tài sản đã tồn tại trong hệ thống.");
    }
}

public class CreateAssetCommandHandler : IRequestHandler<CreateAssetCommand, AssetResult>
{
    private readonly AppDbContext _context;
    private readonly IActionLogService _actionLogService;
    private readonly ICompanyScopeService _companyScope;
    private readonly IAssetTagGenerator _assetTagGenerator;

    public CreateAssetCommandHandler(AppDbContext context, IActionLogService actionLogService, ICompanyScopeService companyScope, IAssetTagGenerator assetTagGenerator)
    {
        _context = context;
        _actionLogService = actionLogService;
        _companyScope = companyScope;
        _assetTagGenerator = assetTagGenerator;
    }

    public async Task<AssetResult> Handle(CreateAssetCommand request, CancellationToken cancellationToken)
    {
        // [Task L2] Company-scoping on CREATE: a regular user may only create assets for their own
        // company (or company-less floater). Superuser (GetCurrentUserCompanyIdAsync → null) may
        // create for any company.
        var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        if (userCompanyId.HasValue && request.CompanyId.HasValue && request.CompanyId.Value != userCompanyId.Value)
            return new AssetResult(false, "Bạn chỉ được tạo tài sản cho công ty của mình.", ErrorCode: "COMPANY_MISMATCH");

        // [Task ASSET-TAG-AUTO] Empty/null AssetTag → auto-generate from the configured format.
        // Non-empty → use the caller's value as-is (still passes the unique DB constraint).
        var assetTag = await _assetTagGenerator.ResolveAssetTagAsync(request.AssetTag, request.CompanyId, cancellationToken);

        var asset = new Asset
        {
            AssetTag = assetTag,
            Name = request.Name,
            Serial = request.Serial,
            Image = request.Image,
            ModelId = request.ModelId,
            LocationId = request.LocationId,
            SupplierId = request.SupplierId,
            CompanyId = request.CompanyId,
            PurchaseCost = request.PurchaseCost,
            PurchaseDate = request.PurchaseDate,
            WarrantyMonths = request.WarrantyMonths,
            Physical = request.Physical,
            Requestable = request.Requestable,
            OrderNumber = request.OrderNumber,
            Notes = request.Notes,
            // The "Xác nhận tạo" button in the UI IS the final confirmation: the asset is
            // created officially onboarded (IsConfirmed=true) so it is immediately ready for
            // allocation and its fields are locked (only Name/Notes remain editable).
            IsConfirmed = true
        };
        _context.Assets.Add(asset);

        _actionLogService.LogAction(
            itemType: ItemType.Asset,
            itemId: asset.Id,
            actionType: ActionType.Create,
            loggedByUserId: request.CurrentUserId,
            companyId: asset.CompanyId,
            note: $"Created asset: {assetTag} - {request.Name}",
            logMeta: JsonSerializer.Serialize(new { assetTag, name = request.Name, serial = request.Serial }));

        await _context.SaveChangesAsync(cancellationToken);

        return new AssetResult(true, "Asset created successfully.", AssetId: asset.Id);
    }
}