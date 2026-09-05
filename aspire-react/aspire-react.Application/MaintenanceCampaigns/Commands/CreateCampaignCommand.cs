using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.MaintenanceCampaigns.Commands;

public record CreateCampaignResult(
    bool Success,
    string? ErrorCode = null,
    Guid? CampaignId = null,
    Guid? SystemInfoId = null,
    Guid? TemplateVersionId = null,
    int VersionNumber = 0,
    DateTime? StartDate = null,
    DateTime? EndDate = null,
    string? BatchNumber = null,
    Guid? CompanyId = null,
    Guid? ReviewerId = null,
    string? Status = null,
    int SnapshotCount = 0,
    int ExecutorCount = 0);

/// <summary>
/// [MC-3] POST api/v1/maintenance/campaigns (extracted verbatim from
/// MaintenanceCampaignsController.Create). Guard order verbatim: SYSTEM_INFO_REQUIRED → system 404
/// (not found OR out-of-scope — hide existence) → END_BEFORE_START → ResolvePinableVersion
/// (NOT_FOUND / TEMPLATE_SYSTEM_MISMATCH / NO_TEMPLATE / AMBIGUOUS_TEMPLATE / NO_CURRENT_VERSION) →
/// INVALID_REVIEWER → executors (distinct, INVALID_EXECUTOR, EXECUTOR_COMPANY_MISMATCH) →
/// auto device snapshot (mounted assets, immutable denormalized) →
/// ── [BUG-A] Race-safe "one InProgress campaign per system" — same FOR UPDATE pattern as
/// AssetTagGenerator (Task O/O-FIX). The old check-then-insert had a race window: 8 parallel
/// creates produced created=2/blocked=6 (audit 2026-08-30). Fix: inside ONE transaction,
/// lock the SystemInfo row FOR UPDATE FIRST (serializes concurrent creates per system —
/// different systems are unaffected), THEN re-check InProgress, THEN insert. Check+insert
/// are now atomic per system. Business rule unchanged.
/// Npgsql's retrying execution strategy requires user transactions inside
/// CreateExecutionStrategy().ExecuteAsync (Task O/O-FIX convention). ──
/// NOTE verbatim quirk: ChangeTracker.Clear() detaches the pre-read SystemInfo BEFORE the campaign
/// is added, so the Create log's TargetSystemInfoName is null (nav not fixed up) while
/// TargetSystemInfoId stays correct from the scalar — pre-existing behavior, preserved as-is.
/// Manual logging (CampaignAccess.BuildLog) — NOT ILoggableCommand (own transaction).
/// </summary>
public record CreateCampaignCommand(
    Guid SystemInfoId,
    Guid? TemplateId,
    DateTime? StartDate,
    DateTime? EndDate,
    string? BatchNumber,
    Guid? ReviewerId,
    Guid[]? ExecutorIds,
    Guid CurrentUserId)
    : IRequest<CreateCampaignResult>;

public class CreateCampaignCommandHandler : IRequestHandler<CreateCampaignCommand, CreateCampaignResult>
{
    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _companyScope;
    private readonly IActionLogService _actionLogService;

    public CreateCampaignCommandHandler(
        IApplicationDbContext context, ICompanyScopeService companyScope, IActionLogService actionLogService)
    {
        _context = context;
        _companyScope = companyScope;
        _actionLogService = actionLogService;
    }

    public async Task<CreateCampaignResult> Handle(CreateCampaignCommand request, CancellationToken cancellationToken)
    {
        if (request.SystemInfoId == Guid.Empty)
            return new CreateCampaignResult(false, "SYSTEM_INFO_REQUIRED");

        // Company scope on the system (hide existence out-of-scope).
        // [BUG-A] Read WITHOUT AsNoTracking: this tracked SystemInfo row is re-fetched FOR UPDATE
        // below to serialize concurrent CreateCampaign calls against the SAME system.
        var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        var sys = await _context.SystemInfos
            .FirstOrDefaultAsync(s => s.Id == request.SystemInfoId, cancellationToken);
        if (sys == null)
            return new CreateCampaignResult(false, "SYSTEM_NOT_FOUND");
        if (userCompanyId.HasValue && sys.CompanyId.HasValue && sys.CompanyId.Value != userCompanyId.Value)
            return new CreateCampaignResult(false, "SYSTEM_NOT_FOUND");

        if (request.EndDate.HasValue && request.StartDate.HasValue && request.EndDate.Value < request.StartDate.Value)
            return new CreateCampaignResult(false, "END_BEFORE_START");

        var (template, version, pinError, _) = await CampaignAccess.ResolvePinableVersionAsync(
            _context, _companyScope, request.SystemInfoId, request.TemplateId);
        if (pinError != null)
            return new CreateCampaignResult(false, pinError);

        if (request.ReviewerId.HasValue && !await _context.Users.AsNoTracking().AnyAsync(u => u.Id == request.ReviewerId.Value, cancellationToken))
            return new CreateCampaignResult(false, "INVALID_REVIEWER");

        // [MC-6] Executor UI (hoãn từ MC-3): nhiều người thực hiện. Mirror ValidateAssigneesAsync
        // (AssetMaintenancesController): distinct, tồn tại, cùng công ty với hệ thống.
        var executorIds = request.ExecutorIds?.Distinct().ToArray() ?? Array.Empty<Guid>();
        if (executorIds.Length > 0)
        {
            var executors = await _context.Users.AsNoTracking()
                .Where(u => executorIds.Contains(u.Id))
                .Select(u => new { u.Id, u.CompanyId })
                .ToListAsync(cancellationToken);
            if (executors.Count != executorIds.Length)
                return new CreateCampaignResult(false, "INVALID_EXECUTOR");
            if (userCompanyId.HasValue && sys.CompanyId != null && sys.CompanyId.Value != Guid.Empty
                && executors.Any(u => u.CompanyId != sys.CompanyId))
                return new CreateCampaignResult(false, "EXECUTOR_COMPANY_MISMATCH");
        }

        var userId = request.CurrentUserId;
        var startDate = request.StartDate.HasValue ? CampaignAccess.ToUtc(request.StartDate.Value) : DateTime.UtcNow;
        var endDate = request.EndDate.HasValue ? CampaignAccess.ToUtc(request.EndDate.Value) : (DateTime?)null;

        var mountedAssets = await _context.Assets.AsNoTracking()
            .Include(a => a.Model)
            .Include(a => a.SystemPosition)
            .Where(a => a.SystemPositionId != null && a.SystemPosition!.SystemInfoId == request.SystemInfoId)
            .ToListAsync(cancellationToken);

        var campaign = new MaintenanceCampaign
        {
            SystemInfoId = request.SystemInfoId,
            TemplateVersionId = version!.Id,
            StartDate = startDate,
            EndDate = endDate,
            BatchNumber = string.IsNullOrWhiteSpace(request.BatchNumber) ? null : request.BatchNumber.Trim(),
            CompanyId = sys.CompanyId, // server-set from SystemInfo (floater = null)
            ReviewerId = request.ReviewerId,
            Status = MaintenanceCampaignStatus.InProgress
        };
        foreach (var a in mountedAssets)
        {
            campaign.DeviceSnapshots.Add(new MaintenanceCampaignDeviceSnapshot
            {
                AssetId = a.Id,
                AssetTag = a.AssetTag,
                AssetName = a.Name,
                Serial = a.Serial,
                ModelNumber = a.Model?.ModelNumber,
                SystemPositionId = a.SystemPositionId,
                SystemPositionName = a.SystemPosition?.Name
            });
        }
        foreach (var uid in executorIds)
        {
            campaign.Executors.Add(new MaintenanceCampaignExecutor { UserId = uid });
        }

        string? raceError = null;
        var strategy = _context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _context.Database.BeginTransactionAsync(cancellationToken);
            // FOR UPDATE lock chỉ chạy trên relational provider — InMemory (unit tests) không
            // dịch được raw SQL; unit tests chạy check+insert không lock (tuần tự, an toàn —
            // cùng quy ước TestHelpers đã ghi nhận cho Checkout/Checkin handlers).
            if (_context.Database.IsRelational())
            {
                var sysParam = new Npgsql.NpgsqlParameter("sysId", NpgsqlTypes.NpgsqlDbType.Uuid) { Value = request.SystemInfoId };
                await _context.SystemInfos
                    .FromSqlRaw(@"SELECT * FROM public.""system_infos"" WHERE ""Id"" = @sysId FOR UPDATE", sysParam)
                    .FirstOrDefaultAsync(cancellationToken);
                _context.ChangeTracker.Clear(); // drop the FOR UPDATE snapshot — sys state stays from the pre-read
            }

            // Re-check INSIDE the lock: at this moment no other transaction holds the row, so this
            // read sees every campaign committed by earlier creators.
            var hasInProgress = await _context.MaintenanceCampaigns.AsNoTracking()
                .AnyAsync(c => c.SystemInfoId == request.SystemInfoId && c.Status == MaintenanceCampaignStatus.InProgress, cancellationToken);
            if (hasInProgress)
            {
                raceError = "CAMPAIGN_ALREADY_IN_PROGRESS";
                return;
            }

            _context.MaintenanceCampaigns.Add(campaign);
            await _context.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
        });
        if (raceError != null)
            return new CreateCampaignResult(false, raceError);

        _actionLogService.Log(CampaignAccess.BuildLog(
            ActionType.Create, campaign, userId,
            $"Tạo đợt bảo dưỡng cho hệ thống \"{sys.Name}\" — version {version.VersionNumber}",
            new
            {
                templateVersionId = version.Id,
                versionNumber = version.VersionNumber,
                batchNumber = campaign.BatchNumber,
                startDate = campaign.StartDate,
                endDate = campaign.EndDate,
                snapshotCount = campaign.DeviceSnapshots.Count,
                executorIds = executorIds
            }));
        await _context.SaveChangesAsync(cancellationToken);

        return new CreateCampaignResult(true,
            CampaignId: campaign.Id,
            SystemInfoId: campaign.SystemInfoId,
            TemplateVersionId: campaign.TemplateVersionId,
            VersionNumber: version.VersionNumber,
            StartDate: campaign.StartDate,
            EndDate: campaign.EndDate,
            BatchNumber: campaign.BatchNumber,
            CompanyId: campaign.CompanyId,
            ReviewerId: campaign.ReviewerId,
            Status: campaign.Status.ToString(),
            SnapshotCount: campaign.DeviceSnapshots.Count,
            ExecutorCount: executorIds.Length);
    }
}
