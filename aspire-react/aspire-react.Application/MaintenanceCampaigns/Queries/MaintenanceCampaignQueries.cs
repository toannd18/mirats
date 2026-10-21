using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Application.Common.Sorting;
using aspire_react.Server.Domain.Entities;
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
    string? TemplateName,
    int VersionNumber,
    DateTime StartDate,
    DateTime? EndDate,
    string? BatchNumber,
    Guid? CompanyId,
    Guid? ReviewerId,
    string? ReviewerName,
    string Status,
    DateTime CreatedAt,
    int SnapshotCount,
    int ResultsCount,
    IReadOnlyList<string> ExecutorNames);

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

public record ListMaintenanceCampaignsQuery(
    Guid? SystemInfoId,
    string? SortBy = null,
    string? SortDir = null) : IRequest<ListMaintenanceCampaignsResult>;

public record ListMaintenanceCampaignsResult(IReadOnlyList<CampaignListItemDto> Items);

/// <summary>
/// Whitelist sort cho danh sách đợt bảo dưỡng — CHỈ các field thực sự lưu trong DB (không sort theo tên
/// navigation hay field không tồn tại). <c>batchnumber</c> dùng collation tự nhiên (DOT-2 &lt; DOT-10);
/// <c>startdate</c> mặc định DESC, các key còn lại mặc định ASC; key lạ (hoặc rỗng) rơi về StartDate DESC.
/// </summary>
public static class CampaignListSort
{
    public const string NaturalCollation = NaturalSort.Collation;

    public static IOrderedQueryable<MaintenanceCampaign> Apply(
        IQueryable<MaintenanceCampaign> query, string? sortBy, string? sortDir)
    {
        var key = sortBy?.Trim().ToLowerInvariant();
        bool? explicitDesc = string.Equals(sortDir, "desc", StringComparison.OrdinalIgnoreCase)
            ? true
            : string.Equals(sortDir, "asc", StringComparison.OrdinalIgnoreCase)
                ? false
                : null;
        var desc = explicitDesc ?? (key == "startdate");

        return key switch
        {
            "batchnumber" => desc
                ? query.OrderByDescending(c => EF.Functions.Collate(c.BatchNumber!, NaturalCollation))
                : query.OrderBy(c => EF.Functions.Collate(c.BatchNumber!, NaturalCollation)),
            "startdate" => desc ? query.OrderByDescending(c => c.StartDate) : query.OrderBy(c => c.StartDate),
            "enddate" => desc ? query.OrderByDescending(c => c.EndDate) : query.OrderBy(c => c.EndDate),
            "status" => desc ? query.OrderByDescending(c => c.Status) : query.OrderBy(c => c.Status),
            "versionnumber" => desc
                ? query.OrderByDescending(c => c.TemplateVersion.VersionNumber)
                : query.OrderBy(c => c.TemplateVersion.VersionNumber),
            _ => query.OrderByDescending(c => c.StartDate).ThenBy(c => c.BatchNumber),
        };
    }
}

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

        // [list-sort] Sort whitelist chạy TRÊN DB (client không sort) — áp trước projection.
        var ordered = CampaignListSort.Apply(query, request.SortBy, request.SortDir);

        var rows = await ordered
            .Select(c => new
            {
                c.Id,
                c.SystemInfoId,
                SystemInfoName = c.SystemInfo.Name,
                c.TemplateVersionId,
                TemplateName = c.TemplateVersion.Template.Name,
                c.TemplateVersion.VersionNumber,
                c.StartDate,
                c.EndDate,
                c.BatchNumber,
                c.CompanyId,
                c.ReviewerId,
                ReviewerFirstName = c.Reviewer != null ? c.Reviewer.FirstName : null,
                ReviewerLastName = c.Reviewer != null ? c.Reviewer.LastName : null,
                ReviewerUsername = c.Reviewer != null ? c.Reviewer.Username : null,
                Status = c.Status.ToString(),
                c.CreatedAt,
                SnapshotCount = c.DeviceSnapshots.Count(),
                ResultsCount = c.Results.Count()
            })
            .ToListAsync(cancellationToken);

        // Executors resolve theo lô (tránh N+1) — tên hiển thị giống detail handler.
        var campaignIds = rows.Select(r => r.Id).ToList();
        var executors = campaignIds.Count == 0
            ? new List<ExecutorNameRow>()
            : await _context.MaintenanceCampaignExecutors.AsNoTracking()
                .Where(e => campaignIds.Contains(e.CampaignId))
                .Select(e => new ExecutorNameRow(
                    e.CampaignId,
                    e.User.FirstName,
                    e.User.LastName,
                    e.User.Username))
                .ToListAsync(cancellationToken);

        var executorNamesByCampaign = executors
            .GroupBy(e => e.CampaignId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<string>)g
                    .Select(e => DisplayName(e.FirstName, e.LastName, e.Username))
                    .OrderBy(n => n, StringComparer.CurrentCulture)
                    .ToList());

        var list = rows.Select(r => new CampaignListItemDto(
            r.Id,
            r.SystemInfoId,
            r.SystemInfoName,
            r.TemplateVersionId,
            r.TemplateName,
            r.VersionNumber,
            r.StartDate,
            r.EndDate,
            r.BatchNumber,
            r.CompanyId,
            r.ReviewerId,
            DisplayName(r.ReviewerFirstName, r.ReviewerLastName, r.ReviewerUsername),
            r.Status,
            r.CreatedAt,
            r.SnapshotCount,
            r.ResultsCount,
            executorNamesByCampaign.TryGetValue(r.Id, out var names) ? names : Array.Empty<string>())).ToList();

        return new ListMaintenanceCampaignsResult(list);
    }

    private sealed record ExecutorNameRow(Guid CampaignId, string? FirstName, string? LastName, string? Username);

    /// <summary>"Họ Tên" → fallback username → null khi cả hai rỗng.</summary>
    private static string? DisplayName(string? firstName, string? lastName, string? username)
    {
        var full = ((firstName ?? "") + " " + (lastName ?? "")).Trim();
        return full.Length > 0 ? full : (string.IsNullOrWhiteSpace(username) ? null : username);
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
