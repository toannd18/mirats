using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Search;

public sealed record SearchPermissions(
    bool Assets,
    bool Systems,
    bool Consumables,
    bool Components,
    bool Accessories,
    bool Licenses,
    bool Users)
{
    public static SearchPermissions None => new(false, false, false, false, false, false, false);
}

public sealed record SearchItemDto(
    string Type,
    Guid Id,
    string Title,
    string Subtitle,
    string Url);

public sealed record SearchGroupDto(
    string Type,
    string Label,
    IReadOnlyList<SearchItemDto> Items);

public sealed record GlobalSearchResult(
    IReadOnlyList<SearchGroupDto> Groups,
    bool HasMore)
{
    public int Total => Groups.Sum(g => g.Items.Count);
}

public sealed record GlobalSearchQuery(
    string Query,
    SearchPermissions Permissions) : IRequest<GlobalSearchResult>;

public sealed class GlobalSearchQueryHandler : IRequestHandler<GlobalSearchQuery, GlobalSearchResult>
{
    private const int PerTypeLimit = 5;
    private const int TotalLimit = 20;

    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _companyScope;

    public GlobalSearchQueryHandler(IApplicationDbContext context, ICompanyScopeService companyScope)
    {
        _context = context;
        _companyScope = companyScope;
    }

    public async Task<GlobalSearchResult> Handle(GlobalSearchQuery request, CancellationToken cancellationToken)
    {
        var term = SearchDbFunctions.NormalizeQuery(request.Query);
        if (term.Length < 2) return new GlobalSearchResult(Array.Empty<SearchGroupDto>(), false);

        var scope = await _companyScope.GetCurrentUserCompanyIdAsync();
        var groups = new List<SearchGroupDto>(7);
        var anyTypeHasMore = false;

        if (request.Permissions.Assets)
        {
            var items = await _context.Assets.AsNoTracking()
                .Where(a => (scope == null || a.CompanyId == null || a.CompanyId == scope)
                    && (SearchDbFunctions.Unaccent(a.AssetTag).ToLower().Contains(term)
                        || SearchDbFunctions.Unaccent(a.Name).ToLower().Contains(term)
                        || (a.Serial != null && SearchDbFunctions.Unaccent(a.Serial).ToLower().Contains(term))))
                .OrderByDescending(a => SearchDbFunctions.Unaccent(a.AssetTag).ToLower() == term
                    || (a.Serial != null && SearchDbFunctions.Unaccent(a.Serial).ToLower() == term))
                .ThenByDescending(a => SearchDbFunctions.Unaccent(a.AssetTag).ToLower().StartsWith(term)
                    || (a.Serial != null && SearchDbFunctions.Unaccent(a.Serial).ToLower().StartsWith(term)))
                .ThenBy(a => a.AssetTag)
                .Select(a => new SearchItemDto(
                    "asset", a.Id, a.AssetTag, a.Name, $"/assets/{a.Id}"))
                .Take(PerTypeLimit + 1)
                .ToListAsync(cancellationToken);
            AddGroup(groups, "asset", "Tài sản", items, ref anyTypeHasMore);
        }

        if (request.Permissions.Systems)
        {
            var items = await _context.SystemInfos.AsNoTracking()
                .Where(s => (scope == null || s.CompanyId == null || s.CompanyId == scope)
                    && (SearchDbFunctions.Unaccent(s.Code).ToLower().Contains(term)
                        || SearchDbFunctions.Unaccent(s.Name).ToLower().Contains(term)))
                .OrderByDescending(s => SearchDbFunctions.Unaccent(s.Code).ToLower() == term)
                .ThenByDescending(s => SearchDbFunctions.Unaccent(s.Code).ToLower().StartsWith(term))
                .ThenBy(s => s.Code)
                .Select(s => new SearchItemDto(
                    "system", s.Id, s.Code, s.Name, $"/systems/{s.Id}"))
                .Take(PerTypeLimit + 1)
                .ToListAsync(cancellationToken);
            AddGroup(groups, "system", "Hệ thống", items, ref anyTypeHasMore);
        }

        if (request.Permissions.Consumables)
        {
            var items = await _context.Consumables.AsNoTracking()
                .Where(c => (scope == null || c.CompanyId == null || c.CompanyId == scope)
                    && (SearchDbFunctions.Unaccent(c.Name).ToLower().Contains(term)
                        || (c.ItemNo != null && SearchDbFunctions.Unaccent(c.ItemNo).ToLower().Contains(term))))
                .OrderByDescending(c => c.ItemNo != null && SearchDbFunctions.Unaccent(c.ItemNo).ToLower() == term)
                .ThenByDescending(c => c.ItemNo != null && SearchDbFunctions.Unaccent(c.ItemNo).ToLower().StartsWith(term))
                .ThenBy(c => c.Name)
                .Select(c => new SearchItemDto(
                    "consumable", c.Id, c.Name, c.ItemNo ?? "Vật tư tiêu hao", $"/consumables/{c.Id}/view"))
                .Take(PerTypeLimit + 1)
                .ToListAsync(cancellationToken);
            AddGroup(groups, "consumable", "Vật tư tiêu hao", items, ref anyTypeHasMore);
        }

        if (request.Permissions.Components)
        {
            var items = await _context.Components.AsNoTracking()
                .Where(c => (scope == null || c.CompanyId == null || c.CompanyId == scope)
                    && (SearchDbFunctions.Unaccent(c.Name).ToLower().Contains(term)
                        || (c.ItemNo != null && SearchDbFunctions.Unaccent(c.ItemNo).ToLower().Contains(term))))
                .OrderByDescending(c => c.ItemNo != null && SearchDbFunctions.Unaccent(c.ItemNo).ToLower() == term)
                .ThenByDescending(c => c.ItemNo != null && SearchDbFunctions.Unaccent(c.ItemNo).ToLower().StartsWith(term))
                .ThenBy(c => c.Name)
                .Select(c => new SearchItemDto(
                    "component", c.Id, c.Name, c.ItemNo ?? "Linh kiện", $"/components/{c.Id}"))
                .Take(PerTypeLimit + 1)
                .ToListAsync(cancellationToken);
            AddGroup(groups, "component", "Linh kiện", items, ref anyTypeHasMore);
        }

        if (request.Permissions.Accessories)
        {
            var items = await _context.Accessories.AsNoTracking()
                .Where(a => (scope == null || a.CompanyId == null || a.CompanyId == scope)
                    && (SearchDbFunctions.Unaccent(a.Name).ToLower().Contains(term)
                        || (a.ItemNo != null && SearchDbFunctions.Unaccent(a.ItemNo).ToLower().Contains(term))))
                .OrderByDescending(a => a.ItemNo != null && SearchDbFunctions.Unaccent(a.ItemNo).ToLower() == term)
                .ThenByDescending(a => a.ItemNo != null && SearchDbFunctions.Unaccent(a.ItemNo).ToLower().StartsWith(term))
                .ThenBy(a => a.Name)
                .Select(a => new SearchItemDto(
                    "accessory", a.Id, a.Name, a.ItemNo ?? "Phụ kiện", $"/accessories/{a.Id}/view"))
                .Take(PerTypeLimit + 1)
                .ToListAsync(cancellationToken);
            AddGroup(groups, "accessory", "Phụ kiện", items, ref anyTypeHasMore);
        }

        if (request.Permissions.Licenses)
        {
            var items = await _context.Licenses.AsNoTracking()
                .Where(l => l.DeletedAt == null
                    && (scope == null || l.CompanyId == null || l.CompanyId == scope)
                    && (SearchDbFunctions.Unaccent(l.Name).ToLower().Contains(term)
                        || (l.Serial != null && SearchDbFunctions.Unaccent(l.Serial).ToLower().Contains(term))))
                .OrderByDescending(l => l.Serial != null && SearchDbFunctions.Unaccent(l.Serial).ToLower() == term)
                .ThenByDescending(l => l.Serial != null && SearchDbFunctions.Unaccent(l.Serial).ToLower().StartsWith(term))
                .ThenBy(l => l.Name)
                .Select(l => new SearchItemDto(
                    "license", l.Id, l.Name, l.Serial ?? "Bản quyền", $"/licenses/{l.Id}"))
                .Take(PerTypeLimit + 1)
                .ToListAsync(cancellationToken);
            AddGroup(groups, "license", "Bản quyền", items, ref anyTypeHasMore);
        }

        if (request.Permissions.Users)
        {
            var items = await _context.Users.AsNoTracking()
                .Where(u => u.IsActive
                    && (scope == null || u.CompanyId == null || u.CompanyId == scope)
                    && (SearchDbFunctions.Unaccent(u.Username).ToLower().Contains(term)
                        || SearchDbFunctions.Unaccent(u.FirstName + " " + u.LastName).ToLower().Contains(term)
                        || SearchDbFunctions.Unaccent(u.Email).ToLower().Contains(term)))
                .OrderByDescending(u => SearchDbFunctions.Unaccent(u.Username).ToLower() == term
                    || SearchDbFunctions.Unaccent(u.Email).ToLower() == term)
                .ThenByDescending(u => SearchDbFunctions.Unaccent(u.Username).ToLower().StartsWith(term)
                    || SearchDbFunctions.Unaccent(u.Email).ToLower().StartsWith(term))
                .ThenBy(u => u.Username)
                .Select(u => new SearchItemDto(
                    "user", u.Id, u.Username,
                    ((u.FirstName ?? "") + " " + (u.LastName ?? "")).Trim(),
                    $"/users/{u.Id}"))
                .Take(PerTypeLimit + 1)
                .ToListAsync(cancellationToken);
            AddGroup(groups, "user", "Người dùng", items, ref anyTypeHasMore);
        }

        // Enforce the global 20-result cap after per-type grouping, preserving type order.
        var trimmed = new List<SearchGroupDto>(groups.Count);
        var remaining = TotalLimit;
        foreach (var group in groups)
        {
            if (remaining <= 0) { anyTypeHasMore = true; break; }
            var items = group.Items.Take(remaining).ToList();
            if (items.Count < group.Items.Count) anyTypeHasMore = true;
            trimmed.Add(group with { Items = items });
            remaining -= items.Count;
        }

        return new GlobalSearchResult(trimmed, anyTypeHasMore);
    }

    private static void AddGroup(
        List<SearchGroupDto> groups,
        string type,
        string label,
        IReadOnlyList<SearchItemDto> items,
        ref bool anyTypeHasMore)
    {
        if (items.Count > PerTypeLimit)
        {
            anyTypeHasMore = true;
            items = items.Take(PerTypeLimit).ToList();
        }
        if (items.Count > 0) groups.Add(new SearchGroupDto(type, label, items));
    }
}
