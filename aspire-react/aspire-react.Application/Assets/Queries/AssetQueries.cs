using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Assets.Queries;

// ─── DTOs (shapes verbatim from the controller's anonymous projections — key-by-key audited) ───
// QUIRK preserved (user-approved): the LIST uses assignedTo.type values "User"/"Department"/
// "SystemPosition" (TargetType.ToString() — capitalized) while the DETAIL resolves the assigned
// target with lowercase type values "user"/"department"/"systemPosition" and DIFFERENT shapes
// (user → id/type/username/firstName/lastName; department/systemPosition → id/type/name).
// Both quirks are pre-existing controller behavior — kept as-is, NOT unified.

public sealed record AssetModelRefDto(Guid Id, string Name);

public sealed record AssetCategoryRefDto(Guid Id, string Name, string? TagColor);

public sealed record AssetManufacturerRefDto(Guid Id, string Name);

public sealed record AssetLocationRefDto(Guid Id, string Name);

public sealed record AssetCompanyRefDto(Guid Id, string Name);

public sealed record AssetAssignedToListDto(string Type, Guid TargetId, string? Name);

public sealed record AssetListItemDto(
    Guid Id,
    string AssetTag,
    string Name,
    string? Serial,
    string? Notes,
    decimal? PurchaseCost,
    DateTime? PurchaseDate,
    string Status,
    bool IsConfirmed,
    int CheckoutCounter,
    int CheckinCounter,
    DateTime? LastCheckout,
    DateTime? LastCheckin,
    AssetModelRefDto? Model,
    AssetCategoryRefDto? Category,
    AssetManufacturerRefDto? Manufacturer,
    AssetLocationRefDto? Location,
    AssetCompanyRefDto? Company,
    AssetAssignedToListDto? AssignedTo);

public record ListAssetsQuery(
    string? Search,
    AssetStatus? Status,
    Guid? CategoryId,
    Guid? LocationId,
    int Page,
    int PageSize) : IRequest<ListAssetsResult>;

public record ListAssetsResult(IReadOnlyList<AssetListItemDto> Items, int Total);

public class ListAssetsQueryHandler : IRequestHandler<ListAssetsQuery, ListAssetsResult>
{
    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _companyScope;

    public ListAssetsQueryHandler(IApplicationDbContext context, ICompanyScopeService companyScope)
    {
        _context = context;
        _companyScope = companyScope;
    }

    public async Task<ListAssetsResult> Handle(ListAssetsQuery request, CancellationToken cancellationToken)
    {
        var query = _context.Assets
            .Include(a => a.Model).ThenInclude(m => m != null ? m.Manufacturer : null)
            .Include(a => a.Model).ThenInclude(m => m != null ? m.Category : null)
            .Include(a => a.Location)
            .Include(a => a.Company)
            .Include(a => a.CurrentAssignment)
            .AsNoTracking();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var s = request.Search.ToLower();
            query = query.Where(a => a.AssetTag.ToLower().Contains(s) || a.Name.ToLower().Contains(s) || (a.Serial != null && a.Serial.ToLower().Contains(s)));
        }
        if (request.Status.HasValue)
            query = query.Where(a => a.Status == request.Status.Value);
        if (request.CategoryId.HasValue) query = query.Where(a => a.Model != null && a.Model.CategoryId == request.CategoryId);
        if (request.LocationId.HasValue) query = query.Where(a => a.LocationId == request.LocationId);

        var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        query = query.Where(a => userCompanyId == null || a.CompanyId == null || a.CompanyId == userCompanyId.Value);

        var total = await query.CountAsync(cancellationToken);
        var assets = await query.OrderBy(a => a.AssetTag).Skip((request.Page - 1) * request.PageSize).Take(request.PageSize)
            .Select(a => new
            {
                a.Id,
                a.AssetTag,
                a.Name,
                a.Serial,
                a.Notes,
                a.PurchaseCost,
                a.PurchaseDate,
                Status = a.Status.ToString(),
                a.IsConfirmed,
                a.CheckoutCounter,
                a.CheckinCounter,
                a.LastCheckout,
                a.LastCheckin,
                Model = a.Model == null ? null : new { a.Model.Id, a.Model.Name },
                Category = a.Model == null || a.Model.Category == null ? null : new { a.Model.Category.Id, a.Model.Category.Name, a.Model.Category.TagColor },
                Manufacturer = a.Model == null || a.Model.Manufacturer == null ? null : new { a.Model.Manufacturer.Id, a.Model.Manufacturer.Name },
                Location = a.Location == null ? null : new { a.Location.Id, a.Location.Name },
                Company = a.Company == null ? null : new { a.Company.Id, a.Company.Name },
                AssignedTo = a.CurrentAssignment == null ? null : new
                {
                    type = a.CurrentAssignment.TargetType.ToString(),
                    targetId = a.CurrentAssignment.TargetId
                }
            }).ToListAsync(cancellationToken);

        // ── Batch-resolve assigned-to target names (3 dictionaries, verbatim) ──
        var atAssets = assets.Where(a => a.AssignedTo != null).Select(a => a.AssignedTo!).ToList();
        var uDict = new Dictionary<Guid, string>(); var dDict = new Dictionary<Guid, string>(); var pDict = new Dictionary<Guid, string>();
        if (atAssets.Any())
        {
            var uids = atAssets.Where(x => x.type == "User").Select(x => x.targetId).Distinct().ToList();
            var dids = atAssets.Where(x => x.type == "Department").Select(x => x.targetId).Distinct().ToList();
            var pids = atAssets.Where(x => x.type == "SystemPosition").Select(x => x.targetId).Distinct().ToList();
            if (uids.Any()) uDict = await _context.Users.Where(u => uids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => (u.FirstName + " " + u.LastName).Trim() != "" ? (u.FirstName + " " + u.LastName).Trim() : u.Username, cancellationToken);
            if (dids.Any()) dDict = await _context.Departments.Where(d => dids.Contains(d.Id)).ToDictionaryAsync(d => d.Id, d => d.Name, cancellationToken);
            if (pids.Any()) pDict = await _context.SystemPositions.Where(sp => pids.Contains(sp.Id)).ToDictionaryAsync(sp => sp.Id, sp => sp.Name, cancellationToken);
        }
        var enriched = assets.Select(a =>
        {
            string? an = null;
            if (a.AssignedTo != null) an = a.AssignedTo.type switch { "User" => uDict.GetValueOrDefault(a.AssignedTo.targetId), "Department" => dDict.GetValueOrDefault(a.AssignedTo.targetId), "SystemPosition" => pDict.GetValueOrDefault(a.AssignedTo.targetId), _ => null };
            return new AssetListItemDto(
                a.Id, a.AssetTag, a.Name, a.Serial, a.Notes, a.PurchaseCost, a.PurchaseDate, a.Status, a.IsConfirmed,
                a.CheckoutCounter, a.CheckinCounter, a.LastCheckout, a.LastCheckin,
                a.Model == null ? null : new AssetModelRefDto(a.Model.Id, a.Model.Name),
                a.Category == null ? null : new AssetCategoryRefDto(a.Category.Id, a.Category.Name, a.Category.TagColor),
                a.Manufacturer == null ? null : new AssetManufacturerRefDto(a.Manufacturer.Id, a.Manufacturer.Name),
                a.Location == null ? null : new AssetLocationRefDto(a.Location.Id, a.Location.Name),
                a.Company == null ? null : new AssetCompanyRefDto(a.Company.Id, a.Company.Name),
                a.AssignedTo == null ? null : new AssetAssignedToListDto(a.AssignedTo.type, a.AssignedTo.targetId, an));
        }).ToList();

        return new ListAssetsResult(enriched, total);
    }
}

// ─── Detail ───

public sealed record AssetAssignedToUserDto(Guid Id, string Type, string Username, string FirstName, string? LastName);

public sealed record AssetAssignedToNamedDto(Guid Id, string Type, string? Name);

public sealed record AssetDetailDto(
    Guid Id,
    string AssetTag,
    string Name,
    string? Serial,
    string? Image,
    decimal? PurchaseCost,
    DateTime? PurchaseDate,
    int? WarrantyMonths,
    DateTime? LastCheckout,
    DateTime? LastCheckin,
    DateTime? LastAuditDate,
    DateTime? NextAuditDate,
    int CheckinCounter,
    int CheckoutCounter,
    int RequestsCounter,
    string Status,
    bool IsConfirmed,
    bool Physical,
    bool Requestable,
    string? Accepted,
    string? OrderNumber,
    string? Notes,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    AssetModelRefDto? Model,
    AssetCategoryRefDto? Category,
    AssetManufacturerRefDto? Manufacturer,
    AssetLocationRefDto? Location,
    AssetCompanyRefDto? Supplier,
    AssetCompanyRefDto? Company,
    object? AssignedTo);

public record GetAssetByIdQuery(Guid Id) : IRequest<GetAssetByIdResult>;

public record GetAssetByIdResult(bool Success, AssetDetailDto? Asset = null);

public class GetAssetByIdQueryHandler : IRequestHandler<GetAssetByIdQuery, GetAssetByIdResult>
{
    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _companyScope;

    public GetAssetByIdQueryHandler(IApplicationDbContext context, ICompanyScopeService companyScope)
    {
        _context = context;
        _companyScope = companyScope;
    }

    public async Task<GetAssetByIdResult> Handle(GetAssetByIdQuery request, CancellationToken cancellationToken)
    {
        var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        var asset = await _context.Assets
            .Include(a => a.Model).ThenInclude(m => m != null ? m.Manufacturer : null)
            .Include(a => a.Model).ThenInclude(m => m != null ? m.Category : null)
            .Include(a => a.Location).Include(a => a.Supplier).Include(a => a.Company)
            .Include(a => a.CurrentAssignment)
            .AsNoTracking().FirstOrDefaultAsync(a => a.Id == request.Id, cancellationToken);

        // Company scoping: a regular user may only view assets of their company (or company-less / floater).
        if (asset == null || (userCompanyId.HasValue && asset.CompanyId.HasValue && asset.CompanyId.Value != userCompanyId.Value))
            return new GetAssetByIdResult(false);

        object? assignedTo = null;
        if (asset.CurrentAssignment != null)
        {
            var asgn = asset.CurrentAssignment;
            assignedTo = asgn.TargetType switch
            {
                // QUIRK verbatim: detail uses LOWERCASE type values + per-target-type shapes.
                AssignmentTargetType.User => await _context.Users.AsNoTracking().Select(u => new AssetAssignedToUserDto(u.Id, "user", u.Username, u.FirstName, u.LastName)).FirstOrDefaultAsync(u => u.Id == asgn.TargetId, cancellationToken),
                AssignmentTargetType.Department => await _context.Departments.AsNoTracking().Select(d => new AssetAssignedToNamedDto(d.Id, "department", d.Name)).FirstOrDefaultAsync(d => d.Id == asgn.TargetId, cancellationToken),
                AssignmentTargetType.SystemPosition => await _context.SystemPositions.AsNoTracking().Select(sp => new AssetAssignedToNamedDto(sp.Id, "systemPosition", sp.Name)).FirstOrDefaultAsync(sp => sp.Id == asgn.TargetId, cancellationToken),
                _ => null
            };
        }

        var dto = new AssetDetailDto(
            asset.Id, asset.AssetTag, asset.Name, asset.Serial, asset.Image,
            asset.PurchaseCost, asset.PurchaseDate, asset.WarrantyMonths,
            asset.LastCheckout, asset.LastCheckin, asset.LastAuditDate, asset.NextAuditDate,
            asset.CheckinCounter, asset.CheckoutCounter, asset.RequestsCounter,
            asset.Status.ToString(), asset.IsConfirmed, asset.Physical, asset.Requestable, asset.Accepted,
            asset.OrderNumber, asset.Notes, asset.CreatedAt, asset.UpdatedAt,
            asset.Model == null ? null : new AssetModelRefDto(asset.Model.Id, asset.Model.Name),
            asset.Model?.Category == null ? null : new AssetCategoryRefDto(asset.Model.Category.Id, asset.Model.Category.Name, asset.Model.Category.TagColor),
            asset.Model?.Manufacturer == null ? null : new AssetManufacturerRefDto(asset.Model.Manufacturer.Id, asset.Model.Manufacturer.Name),
            asset.Location == null ? null : new AssetLocationRefDto(asset.Location.Id, asset.Location.Name),
            asset.Supplier == null ? null : new AssetCompanyRefDto(asset.Supplier.Id, asset.Supplier.Name),
            asset.Company == null ? null : new AssetCompanyRefDto(asset.Company.Id, asset.Company.Name),
            assignedTo);
        return new GetAssetByIdResult(true, dto);
    }
}

// ─── History ───

public sealed record AssetHistoryCreatorDto(Guid Id, string Username, string FirstName, string? LastName);

public sealed record AssetHistoryLogDto(
    Guid Id,
    ActionType ActionType,
    string? Note,
    string? LogMeta,
    DateTime ActionDate,
    Guid? LocationId,
    string? RemoteIp,
    ActionSource ActionSource,
    AssetHistoryCreatorDto Creator);

public record GetAssetHistoryQuery(Guid Id) : IRequest<GetAssetHistoryResult>;

public record GetAssetHistoryResult(bool Success, IReadOnlyList<AssetHistoryLogDto>? Logs = null);

public class GetAssetHistoryQueryHandler : IRequestHandler<GetAssetHistoryQuery, GetAssetHistoryResult>
{
    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _companyScope;

    public GetAssetHistoryQueryHandler(IApplicationDbContext context, ICompanyScopeService companyScope)
    {
        _context = context;
        _companyScope = companyScope;
    }

    public async Task<GetAssetHistoryResult> Handle(GetAssetHistoryQuery request, CancellationToken cancellationToken)
    {
        // [Task K] Company-scoping: history of an asset is only visible to users who can see the
        // asset itself (mirrors GetAsset). Out-of-scope asset → 404 to hide existence.
        var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        var visible = await _context.Assets.AsNoTracking()
            .AnyAsync(a => a.Id == request.Id && (userCompanyId == null || a.CompanyId == null || a.CompanyId == userCompanyId.Value), cancellationToken);
        if (!visible)
            return new GetAssetHistoryResult(false);

        var logs = await _context.ActionLogs.Include(l => l.Creator).AsNoTracking()
            .Where(l => l.ItemType == ItemType.Asset && l.ItemId == request.Id && l.DeletedAt == null)
            .OrderByDescending(l => l.ActionDate).Take(50)
            .Select(l => new AssetHistoryLogDto(
                l.Id,
                l.ActionType,
                l.Note,
                l.LogMeta,
                l.ActionDate,
                l.LocationId,
                l.RemoteIp,
                l.ActionSource,
                new AssetHistoryCreatorDto(l.Creator.Id, l.Creator.Username, l.Creator.FirstName, l.Creator.LastName)))
            .ToListAsync(cancellationToken);
        return new GetAssetHistoryResult(true, logs);
    }
}
