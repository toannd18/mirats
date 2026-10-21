using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Application.Common.Sorting;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.SystemInfos.Queries;

public record SystemInfoPositionDto(Guid Id, string Code, string Name, string? Description, Guid SystemInfoId, string SystemInfoName);

public record SystemInfoCompanyDto(Guid Id, string Name);

public record SystemInfoDto(Guid Id, string Code, string Name, string? Description, Guid? CompanyId,
    DateTime? NextMaintenanceDueDate, SystemInfoCompanyDto? Company, IReadOnlyList<SystemInfoPositionDto> Positions);

/// <summary>
/// [Giai đoạn 3] GET /api/v1/system-infos (extracted from SystemInfoController.GetAll).
/// FMCS multi-tenant verbatim: superuser sees everything; regular user only company-less or
/// own-company systems. Projection (NOT the raw entity — the graph is cyclic
/// SystemInfo → Positions → SystemInfo and would fail JSON serialization).
/// Sorting is done SERVER-SIDE (full list, no paging): default = code in PostgreSQL natural
/// (numeric-aware) order via <see cref="SystemInfoListSort"/>, so SYS-2026-2 sorts before
/// SYS-2026-10 — frontend must never re-sort client-side.
/// </summary>
public record ListSystemInfosQuery(string? SortBy = null, string? SortDir = null)
    : IRequest<IReadOnlyList<SystemInfoDto>>;

/// <summary>
/// Whitelist sort for the SystemInfo list. Unknown/absent <c>sortBy</c> falls back to the DEFAULT
/// order — code ascending in the natural ICU collation (<c>NaturalSort.Collation</c> =
/// <c>asset_tag_natural</c>, ICU <c>und-u-kn-true</c>) so human codes sort numerically.
/// Only <c>code</c> (natural) and <c>name</c> are sortable; every other key is ignored (never
/// reflected into SQL). Secondary key keeps the order deterministic.
/// </summary>
public static class SystemInfoListSort
{
    public static IOrderedQueryable<SystemInfo> Apply(IQueryable<SystemInfo> query, string? sortBy, bool desc)
    {
        var key = sortBy?.Trim().ToLowerInvariant();
        return key switch
        {
            "name" => desc
                ? query.OrderByDescending(s => s.Name).ThenBy(s => EF.Functions.Collate(s.Code, NaturalSort.Collation))
                : query.OrderBy(s => s.Name).ThenBy(s => EF.Functions.Collate(s.Code, NaturalSort.Collation)),
            "code" => desc
                ? query.OrderByDescending(s => EF.Functions.Collate(s.Code, NaturalSort.Collation))
                : query.OrderBy(s => EF.Functions.Collate(s.Code, NaturalSort.Collation)),
            // Default: natural code ascending (no CreatedAt on SystemInfo — code is the only stable key).
            _ => query.OrderBy(s => EF.Functions.Collate(s.Code, NaturalSort.Collation)),
        };
    }
}

public class ListSystemInfosQueryHandler : IRequestHandler<ListSystemInfosQuery, IReadOnlyList<SystemInfoDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _companyScope;

    public ListSystemInfosQueryHandler(IApplicationDbContext context, ICompanyScopeService companyScope)
    {
        _context = context;
        _companyScope = companyScope;
    }

    public async Task<IReadOnlyList<SystemInfoDto>> Handle(ListSystemInfosQuery request, CancellationToken cancellationToken)
    {
        var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        var query = _context.SystemInfos.AsNoTracking();
        if (userCompanyId.HasValue)
            query = query.Where(s => s.CompanyId == null || s.CompanyId == userCompanyId.Value);

        var desc = string.Equals(request.SortDir?.Trim(), "desc", StringComparison.OrdinalIgnoreCase);
        var ordered = SystemInfoListSort.Apply(query, request.SortBy, desc);

        var list = await ordered
            .Select(s => new SystemInfoDto(
                s.Id,
                s.Code,
                s.Name,
                s.Description,
                s.CompanyId,
                s.NextMaintenanceDueDate,
                s.Company == null ? null : new SystemInfoCompanyDto(s.Company.Id, s.Company.Name),
                s.Positions.OrderBy(p => p.Code).Select(p => new SystemInfoPositionDto(
                    p.Id, p.Code, p.Name, p.Description, p.SystemInfoId, s.Name)).ToList()))
            .ToListAsync(cancellationToken);

        return list;
    }
}
