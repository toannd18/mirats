using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.MaintenanceCampaigns.Queries;

// ─── DTOs (shapes verbatim from the controller's anonymous projections — JSON keys must match) ───

public sealed record CampaignListItemDto(
    Guid Id,
    Guid SystemInfoId,
    string SystemInfoName,
    Guid TemplateVersionId,
    int VersionNumber,
    DateTime StartDate,
    DateTime? EndDate,
    string? BatchNumber,
    Guid? CompanyId,
    Guid? ReviewerId,
    string Status,
    DateTime CreatedAt,
    int SnapshotCount,
    int ResultsCount);

public sealed record CampaignExecutorDto(Guid UserId, string? FullName);

public sealed record CampaignSnapshotDto(
    Guid Id, Guid AssetId, string AssetTag, string? AssetName, string? Serial,
    string? ModelNumber, Guid? SystemPositionId, string? SystemPositionName);

public sealed record CampaignResultDto(
    Guid Id, Guid DeviceSnapshotId, Guid ChecklistItemId, Guid? StandardParamId,
    string? MeasuredValue, bool IsPass, string? Notes);

public sealed record CampaignDetailDto(
    Guid Id,
    Guid SystemInfoId,
    string SystemInfoName,
    Guid TemplateVersionId,
    Guid TemplateId,
    int VersionNumber,
    DateTime StartDate,
    DateTime? EndDate,
    string? BatchNumber,
    Guid? CompanyId,
    Guid? ReviewerId,
    string Status,
    DateTime CreatedAt,
    IReadOnlyList<CampaignExecutorDto> Executors,
    IReadOnlyList<CampaignSnapshotDto> Snapshots,
    IReadOnlyList<CampaignResultDto> Results);

// ─── Queries ───

public record ListMaintenanceCampaignsQuery(Guid? SystemInfoId) : IRequest<ListMaintenanceCampaignsResult>;

public record ListMaintenanceCampaignsResult(IReadOnlyList<CampaignListItemDto> Items);

public class ListMaintenanceCampaignsQueryHandler : IRequestHandler<ListMaintenanceCampaignsQuery, ListMaintenanceCampaignsResult>
{
    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _companyScope;

    public ListMaintenanceCampaignsQueryHandler(IApplicationDbContext context, ICompanyScopeService companyScope)
    {
        _context = context;
        _companyScope = companyScope;
    }

    public async Task<ListMaintenanceCampaignsResult> Handle(ListMaintenanceCampaignsQuery request, CancellationToken cancellationToken)
    {
        var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        var query = _context.MaintenanceCampaigns.AsNoTracking();
        if (userCompanyId.HasValue)
            query = query.Where(c => c.CompanyId == null || c.CompanyId == userCompanyId.Value);
        if (request.SystemInfoId.HasValue && request.SystemInfoId.Value != Guid.Empty)
            query = query.Where(c => c.SystemInfoId == request.SystemInfoId.Value);

        var list = await query
            .OrderByDescending(c => c.StartDate)
            .Select(c => new CampaignListItemDto(
                c.Id,
                c.SystemInfoId,
                c.SystemInfo.Name,
                c.TemplateVersionId,
                c.TemplateVersion.VersionNumber,
                c.StartDate,
                c.EndDate,
                c.BatchNumber,
                c.CompanyId,
                c.ReviewerId,
                c.Status.ToString(),
                c.CreatedAt,
                c.DeviceSnapshots.Count(),
                c.Results.Count()))
            .ToListAsync(cancellationToken);
        return new ListMaintenanceCampaignsResult(list);
    }
}

public record GetMaintenanceCampaignByIdQuery(Guid Id) : IRequest<GetMaintenanceCampaignByIdResult>;

public record GetMaintenanceCampaignByIdResult(bool Success, CampaignDetailDto? Detail = null);

public class GetMaintenanceCampaignByIdQueryHandler : IRequestHandler<GetMaintenanceCampaignByIdQuery, GetMaintenanceCampaignByIdResult>
{
    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _companyScope;

    public GetMaintenanceCampaignByIdQueryHandler(IApplicationDbContext context, ICompanyScopeService companyScope)
    {
        _context = context;
        _companyScope = companyScope;
    }

    public async Task<GetMaintenanceCampaignByIdResult> Handle(GetMaintenanceCampaignByIdQuery request, CancellationToken cancellationToken)
    {
        var c = await CampaignAccess.GetVisibleCampaignAsync(_context, _companyScope, request.Id);
        if (c == null)
            return new GetMaintenanceCampaignByIdResult(false);

        var detail = new CampaignDetailDto(
            c.Id,
            c.SystemInfoId,
            c.SystemInfo.Name,
            c.TemplateVersionId,
            // [MC-6] Frontend cần templateId để lấy items của version đã pin (version detail endpoint).
            c.TemplateVersion.TemplateId,
            c.TemplateVersion.VersionNumber,
            c.StartDate,
            c.EndDate,
            c.BatchNumber,
            c.CompanyId,
            c.ReviewerId,
            c.Status.ToString(),
            c.CreatedAt,
            c.Executors.Select(e => new CampaignExecutorDto(
                e.UserId,
                ((e.User.FirstName ?? "") + " " + (e.User.LastName ?? "")).Trim() is { Length: > 0 } n ? n : e.User.Username)).ToList(),
            c.DeviceSnapshots.OrderBy(s => s.SystemPositionName).ThenBy(s => s.AssetTag).Select(s => new CampaignSnapshotDto(
                s.Id, s.AssetId, s.AssetTag, s.AssetName, s.Serial, s.ModelNumber, s.SystemPositionId, s.SystemPositionName)).ToList(),
            c.Results.Select(r => new CampaignResultDto(
                r.Id, r.DeviceSnapshotId, r.ChecklistItemId, r.StandardParamId, r.MeasuredValue, r.IsPass, r.Notes)).ToList());
        return new GetMaintenanceCampaignByIdResult(true, detail);
    }
}
