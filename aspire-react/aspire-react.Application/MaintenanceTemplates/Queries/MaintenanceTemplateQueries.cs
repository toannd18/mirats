using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.MaintenanceTemplates.Queries;

// ─── DTOs (shapes verbatim from the controller's anonymous projections — JSON keys must match) ───

public sealed record TemplateCompanyRefDto(Guid Id, string Name);

public sealed record TemplateSystemInfoDto(Guid Id, string? Code, string Name);

public sealed record TemplateCurrentVersionDto(Guid Id, int VersionNumber, DateTime? PublishedAt, int ItemsCount, int ParamsCount);

public sealed record TemplateListItemDto(
    Guid Id,
    string Name,
    bool IsActive,
    Guid? CompanyId,
    TemplateCompanyRefDto? Company,
    TemplateSystemInfoDto SystemInfo,
    int VersionsCount,
    int CampaignCount,
    TemplateCurrentVersionDto? CurrentVersion);

public sealed record SystemPositionRefDto(Guid Id, string? Code, string? Name);

public sealed record TemplateSystemInfoWithPositionsDto(Guid Id, string? Code, string Name, IReadOnlyList<SystemPositionRefDto> Positions);

public sealed record TemplateVersionSummaryDto(
    Guid Id, int VersionNumber, DateTime? EffectiveFrom, DateTime? PublishedAt, bool IsCurrent,
    int ItemsCount, int ParamsCount, int CampaignCount);

public sealed record TemplateDetailDto(
    Guid Id, string Name, bool IsActive, Guid? CompanyId,
    TemplateCompanyRefDto? Company, TemplateSystemInfoWithPositionsDto SystemInfo,
    IReadOnlyList<TemplateVersionSummaryDto> Versions);

public sealed record TemplateParamDto(
    Guid Id, string ParamName, string? NominalValue, MaintenanceThresholdOperator ThresholdOperator,
    decimal ThresholdValue, string? CheckMethod, string? Unit);

public sealed record TemplateItemDto(
    Guid Id, int Order, string Name, int CycleMonths, string? ToolsRequired, string? Instruction,
    IReadOnlyList<Guid> PositionIds, IReadOnlyList<string?> PositionNames,
    IReadOnlyList<TemplateParamDto> StandardParams);

public sealed record TemplateVersionDetailDto(
    Guid Id, int VersionNumber, DateTime? EffectiveFrom, DateTime? PublishedAt, bool IsCurrent,
    bool HasCampaigns, bool Editable, IReadOnlyList<TemplateItemDto> Items);

// ─── Queries ───

public record ListMaintenanceTemplatesQuery(Guid? SystemInfoId) : IRequest<ListMaintenanceTemplatesResult>;

public record ListMaintenanceTemplatesResult(IReadOnlyList<TemplateListItemDto> Items);

public class ListMaintenanceTemplatesQueryHandler : IRequestHandler<ListMaintenanceTemplatesQuery, ListMaintenanceTemplatesResult>
{
    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _companyScope;

    public ListMaintenanceTemplatesQueryHandler(IApplicationDbContext context, ICompanyScopeService companyScope)
    {
        _context = context;
        _companyScope = companyScope;
    }

    public async Task<ListMaintenanceTemplatesResult> Handle(ListMaintenanceTemplatesQuery request, CancellationToken cancellationToken)
    {
        var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        var query = _context.MaintenanceChecklistTemplates.AsNoTracking();
        if (userCompanyId.HasValue)
            query = query.Where(t => t.CompanyId == null || t.CompanyId == userCompanyId.Value);
        if (request.SystemInfoId.HasValue && request.SystemInfoId.Value != Guid.Empty)
            query = query.Where(t => t.SystemInfoId == request.SystemInfoId.Value);

        var list = await query
            .OrderBy(t => t.Name)
            .Select(t => new TemplateListItemDto(
                t.Id,
                t.Name,
                t.IsActive,
                t.CompanyId,
                t.Company == null ? null : new TemplateCompanyRefDto(t.Company.Id, t.Company.Name),
                new TemplateSystemInfoDto(t.SystemInfo.Id, t.SystemInfo.Code, t.SystemInfo.Name),
                t.Versions.Count(),
                t.Versions.SelectMany(v => v.Campaigns).Count(),
                t.Versions.Where(v => v.IsCurrent).Select(v => new TemplateCurrentVersionDto(
                    v.Id, v.VersionNumber, v.PublishedAt,
                    v.Items.Count(), v.Items.SelectMany(i => i.StandardParams).Count())).FirstOrDefault()))
            .ToListAsync(cancellationToken);
        return new ListMaintenanceTemplatesResult(list);
    }
}

public record GetMaintenanceTemplateByIdQuery(Guid Id) : IRequest<GetMaintenanceTemplateByIdResult>;

public record GetMaintenanceTemplateByIdResult(bool Success, string? ErrorCode = null, TemplateDetailDto? Detail = null);

public class GetMaintenanceTemplateByIdQueryHandler : IRequestHandler<GetMaintenanceTemplateByIdQuery, GetMaintenanceTemplateByIdResult>
{
    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _companyScope;

    public GetMaintenanceTemplateByIdQueryHandler(IApplicationDbContext context, ICompanyScopeService companyScope)
    {
        _context = context;
        _companyScope = companyScope;
    }

    public async Task<GetMaintenanceTemplateByIdResult> Handle(GetMaintenanceTemplateByIdQuery request, CancellationToken cancellationToken)
    {
        // Out-of-scope → 404 hide-existence (Q1).
        var t = await MaintenanceTemplateAccess.GetVisibleTemplateAsync(_context, _companyScope, request.Id);
        if (t == null)
            return new GetMaintenanceTemplateByIdResult(false, "NOT_FOUND");

        var versions = await _context.MaintenanceChecklistTemplateVersions.AsNoTracking()
            .Where(v => v.TemplateId == request.Id)
            .OrderByDescending(v => v.VersionNumber)
            .Select(v => new TemplateVersionSummaryDto(
                v.Id, v.VersionNumber, v.EffectiveFrom, v.PublishedAt, v.IsCurrent,
                v.Items.Count(), v.Items.SelectMany(i => i.StandardParams).Count(), v.Campaigns.Count()))
            .ToListAsync(cancellationToken);

        var detail = new TemplateDetailDto(
            t.Id, t.Name, t.IsActive, t.CompanyId,
            t.Company == null ? null : new TemplateCompanyRefDto(t.Company.Id, t.Company.Name),
            new TemplateSystemInfoWithPositionsDto(
                t.SystemInfo.Id, t.SystemInfo.Code, t.SystemInfo.Name,
                // [MC-7d] Vị trí của hệ thống template — nguồn options cho multi-select vị trí áp dụng
                // của hạng mục (cùng policy maintenance.templates, không phụ thuộc systems.view).
                t.SystemInfo.Positions.OrderBy(p => p.Code)
                    .Select(p => new SystemPositionRefDto(p.Id, p.Code, p.Name)).ToList()),
            versions);
        return new GetMaintenanceTemplateByIdResult(true, Detail: detail);
    }
}

public record GetMaintenanceTemplateVersionQuery(Guid TemplateId, Guid VersionId) : IRequest<GetMaintenanceTemplateVersionResult>;

public record GetMaintenanceTemplateVersionResult(bool Success, string? ErrorCode = null, TemplateVersionDetailDto? Detail = null);

public class GetMaintenanceTemplateVersionQueryHandler : IRequestHandler<GetMaintenanceTemplateVersionQuery, GetMaintenanceTemplateVersionResult>
{
    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _companyScope;

    public GetMaintenanceTemplateVersionQueryHandler(IApplicationDbContext context, ICompanyScopeService companyScope)
    {
        _context = context;
        _companyScope = companyScope;
    }

    public async Task<GetMaintenanceTemplateVersionResult> Handle(GetMaintenanceTemplateVersionQuery request, CancellationToken cancellationToken)
    {
        var t = await MaintenanceTemplateAccess.GetVisibleTemplateAsync(_context, _companyScope, request.TemplateId);
        if (t == null)
            return new GetMaintenanceTemplateVersionResult(false, "NOT_FOUND");
        var version = await MaintenanceTemplateAccess.GetVersionOfTemplateAsync(_context, t, request.VersionId);
        if (version == null)
            return new GetMaintenanceTemplateVersionResult(false, "VERSION_NOT_FOUND");

        var hasCampaigns = await MaintenanceTemplateAccess.VersionHasCampaignsAsync(_context, request.VersionId);
        var items = await _context.MaintenanceChecklistItems.AsNoTracking()
            .Include(i => i.Positions).ThenInclude(p => p.SystemPosition)
            .Include(i => i.StandardParams)
            .Where(i => i.TemplateVersionId == request.VersionId)
            .OrderBy(i => i.Order)
            .Select(i => new TemplateItemDto(
                i.Id, i.Order, i.Name, i.CycleMonths, i.ToolsRequired, i.Instruction,
                // [MC-7b] Phạm vi vị trí: [] = universal (mọi vị trí); kèm names để UI hiển thị.
                i.Positions.Select(p => p.SystemPositionId).ToList(),
                i.Positions.Select(p => p.SystemPosition != null ? p.SystemPosition.Name : null).ToList(),
                // [MC-8] Tiêu chuẩn kỹ thuật NESTED trong từng hạng mục (thuộc tính con), không còn mảng song song.
                // [MC-10] Ngưỡng cấu trúc: ThresholdOperator (string) + ThresholdValue (number).
                i.StandardParams.OrderBy(p => p.ParamName).Select(p => new TemplateParamDto(
                    p.Id, p.ParamName, p.NominalValue, p.ThresholdOperator, p.ThresholdValue, p.CheckMethod, p.Unit)).ToList()))
            .ToListAsync(cancellationToken);

        var detail = new TemplateVersionDetailDto(
            version.Id, version.VersionNumber, version.EffectiveFrom, version.PublishedAt, version.IsCurrent,
            hasCampaigns, !hasCampaigns, items);
        return new GetMaintenanceTemplateVersionResult(true, Detail: detail);
    }
}
