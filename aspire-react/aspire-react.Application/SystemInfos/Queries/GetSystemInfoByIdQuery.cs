using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.SystemInfos.Queries;

/// <summary>
/// [Giai đoạn 3] GET /api/v1/system-infos/{id} (extracted from SystemInfoController.Get).
/// Same company scope as GetAll → NULL → 404 (avoid leaking existence). Projection (NOT the
/// raw entity — cyclic graph note verbatim).
/// </summary>
public record GetSystemInfoByIdQuery(Guid Id) : IRequest<SystemInfoDto?>;

public class GetSystemInfoByIdQueryHandler : IRequestHandler<GetSystemInfoByIdQuery, SystemInfoDto?>
{
    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _companyScope;

    public GetSystemInfoByIdQueryHandler(IApplicationDbContext context, ICompanyScopeService companyScope)
    {
        _context = context;
        _companyScope = companyScope;
    }

    public async Task<SystemInfoDto?> Handle(GetSystemInfoByIdQuery request, CancellationToken cancellationToken)
    {
        var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        var visible = await _context.SystemInfos.AsNoTracking().AnyAsync(x =>
            x.Id == request.Id && (userCompanyId == null || x.CompanyId == null || x.CompanyId == userCompanyId.Value), cancellationToken);
        if (!visible) return null;

        var s = await _context.SystemInfos
            .Include(x => x.Positions)
            .Include(x => x.Company)
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == request.Id, cancellationToken);
        if (s == null) return null;

        return new SystemInfoDto(
            s.Id,
            s.Code,
            s.Name,
            s.Description,
            s.CompanyId,
            s.NextMaintenanceDueDate,
            s.Company == null ? null : new SystemInfoCompanyDto(s.Company.Id, s.Company.Name),
            s.Positions.OrderBy(p => p.Code).Select(p => new SystemInfoPositionDto(
                p.Id, p.Code, p.Name, p.Description, p.SystemInfoId, s.Name)).ToList());
    }
}
