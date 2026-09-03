using aspire_react.Server.Application.Common.Interfaces;
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
/// </summary>
public record ListSystemInfosQuery : IRequest<IReadOnlyList<SystemInfoDto>>;

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

        var list = await query
            .OrderBy(s => s.Code)
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
