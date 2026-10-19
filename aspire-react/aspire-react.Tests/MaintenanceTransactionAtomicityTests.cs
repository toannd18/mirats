using aspire_react.Server.Application.MaintenanceCampaigns.Commands;
using aspire_react.Server.Application.MaintenanceTemplates.Commands;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Infrastructure.Persistence;
using aspire_react.Server.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace aspire_react.Tests;

/// <summary>
/// [AUDIT-LOG B5] Atomicity trên **Postgres THẬT**: dữ liệu + <c>ActionLog</c> phải nằm trong CÙNG một
/// transaction, commit đúng 1 lần (convention §3.2). Trước B5 các handler MaintenanceTemplates /
/// MaintenanceCampaigns ghi 2 bước rời (<c>SaveChanges</c> dữ liệu → <c>Log</c> → <c>SaveChanges</c>)
/// ⇒ nếu bước ghi nhật ký đổ thì DỮ LIỆU ĐÃ NẰM TRONG DB mà KHÔNG có nhật ký.
///
/// <para>
/// Mỗi nhóm có 2 ca: (a) THÀNH CÔNG ⇒ có dữ liệu VÀ có log; (b) THẤT BẠI CÓ CHỦ ĐÍCH ⇒ KHÔNG dữ liệu
/// VÀ KHÔNG log. Chỉ chạy khi có cờ <c>MIRATS_TEST_THROWAWAY_DB=1</c> + connection string DB tạm
/// (<see cref="ConnectionStringEnv"/>) — xem <see cref="ThrowawayDbFactAttribute"/>.
/// </para>
///
/// <para>
/// <b>Kỹ thuật gây lỗi</b>: EF Core <see cref="SaveChangesInterceptor"/> (<see cref="ActionLogBombInterceptor"/>)
/// ném ngay tại <c>SavingChangesAsync</c> khi change tracker có entity <see cref="ActionLog"/> ở trạng
/// thái <c>Added</c> — tức "bước ghi nhật ký" đổ. Trên code CŨ, <c>SaveChanges</c> dữ liệu đã commit
/// trước đó nên test này SẼ FAIL (dữ liệu còn, log mất); trên code mới (1 transaction) cả hai rollback.
/// </para>
/// </summary>
public class MaintenanceTransactionAtomicityTests
{
    /// <summary>Connection string tới database TẠM dùng cho hồi quy Postgres thật.</summary>
    public const string ConnectionStringEnv = "MIRATS_TEST_POSTGRES";

    // ==================== hạ tầng ====================

    private sealed class InjectedActionLogFailure : Exception
    {
        public InjectedActionLogFailure()
            : base("INJECTED_FAILURE: SaveChanges chứa ActionLog (bước ghi nhật ký đổ có chủ đích)") { }
    }

    /// <summary>
    /// Interceptor "bom": ném khi SaveChanges đang cố ghi một <see cref="ActionLog"/> mới.
    /// Chỉ mô phỏng đúng kịch bản cần chứng minh — KHÔNG ảnh hưởng các SaveChanges chỉ có dữ liệu.
    /// </summary>
    private sealed class ActionLogBombInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            var ctx = eventData.Context;
            if (ctx != null && ctx.ChangeTracker.Entries<ActionLog>().Any(e => e.State == EntityState.Added))
                throw new InjectedActionLogFailure();
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private static async Task<AppDbContext> OpenAsync(bool withActionLogBomb)
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionStringEnv)?.Trim();
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException(
                $"Thiếu {ConnectionStringEnv} (connection string tới DB TẠM). " +
                "VD: Host=127.0.0.1;Port=<host-port>;Database=aspire-react-db;Username=postgres;Password=<pw>");

        var builder = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly("aspire-react.Server"));
        if (withActionLogBomb)
            builder.AddInterceptors(new ActionLogBombInterceptor());

        var db = new AppDbContext(builder.Options, new TestHelpers.SuperUserScope());
        await ThrowawayDbMigrator.MigrateOnceAsync(db, connectionString);
        return db;
    }

    private static string Tag() => "txb" + Guid.NewGuid().ToString("N")[..10];

    private static ActionLogService Logs(AppDbContext db, Guid actor) => TestHelpers.CreateActionLogService(db, actor);

    // ==================== seed ====================

    private static async Task<User> SeedActorAsync(AppDbContext db)
    {
        var tag = Tag();
        var user = new User
        {
            Username = tag,
            FirstName = "Txn",
            LastName = "Atomicity",
            Email = $"{tag}@test.local",
            IsActive = true
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    private static async Task<(SystemInfo System, SystemPosition Position)> SeedSystemAsync(AppDbContext db)
    {
        var tag = Tag();
        var system = new SystemInfo { Code = tag.ToUpperInvariant(), Name = $"TXN system {tag}" };
        db.SystemInfos.Add(system);
        await db.SaveChangesAsync();

        var position = new SystemPosition { SystemInfoId = system.Id, Code = $"P-{tag}", Name = $"TXN position {tag}" };
        db.SystemPositions.Add(position);
        await db.SaveChangesAsync();
        return (system, position);
    }

    /// <summary>Template + version 1. published=true ⇒ version hiện hành đã publish (đủ điều kiện để pin campaign).</summary>
    private static async Task<MaintenanceChecklistTemplate> SeedTemplateAsync(
        AppDbContext db, Guid systemInfoId, Guid actor, bool published)
    {
        var tag = Tag();
        var now = DateTime.UtcNow;
        var template = new MaintenanceChecklistTemplate
        {
            Name = $"TXN template {tag}",
            SystemInfoId = systemInfoId,
            CreatedById = actor,
            CreatedAt = now,
            UpdatedAt = now
        };
        var version = new MaintenanceChecklistTemplateVersion
        {
            TemplateId = template.Id,
            VersionNumber = 1,
            CreatedById = actor,
            CreatedAt = now,
            UpdatedAt = now
        };
        if (published)
        {
            version.PublishedAt = now;
            version.EffectiveFrom = now;
            version.IsCurrent = true;
        }
        template.Versions.Add(version);
        db.MaintenanceChecklistTemplates.Add(template);
        await db.SaveChangesAsync();
        return template;
    }

    private static async Task<MaintenanceChecklistTemplateVersion> SeedDraftVersionAsync(
        AppDbContext db, Guid templateId, Guid actor, int versionNumber)
    {
        var now = DateTime.UtcNow;
        var version = new MaintenanceChecklistTemplateVersion
        {
            TemplateId = templateId,
            VersionNumber = versionNumber,
            CreatedById = actor,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.MaintenanceChecklistTemplateVersions.Add(version);
        await db.SaveChangesAsync();
        return version;
    }

    private static async Task<MaintenanceChecklistItem> SeedItemAsync(AppDbContext db, Guid versionId, int order = 1)
    {
        var now = DateTime.UtcNow;
        var item = new MaintenanceChecklistItem
        {
            TemplateVersionId = versionId,
            Order = order,
            Name = $"TXN item {Tag()}",
            CycleMonths = 12,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.MaintenanceChecklistItems.Add(item);
        await db.SaveChangesAsync();
        return item;
    }

    private static async Task<MaintenanceStandardParam> SeedParamAsync(AppDbContext db, Guid itemId)
    {
        var now = DateTime.UtcNow;
        var param = new MaintenanceStandardParam
        {
            ChecklistItemId = itemId,
            ParamName = $"TXN param {Tag()}",
            ThresholdOperator = MaintenanceThresholdOperator.LessOrEqual,
            ThresholdValue = 60m,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.MaintenanceStandardParams.Add(param);
        await db.SaveChangesAsync();
        return param;
    }

    /// <summary>Asset đã GẮN vị trí hệ thống (điều kiện để CreateCampaign snapshot nó).</summary>
    private static async Task<Asset> SeedMountedAssetAsync(AppDbContext db, Guid systemPositionId)
    {
        var tag = Tag();
        var asset = new Asset
        {
            AssetTag = $"TXN-{tag}",
            Name = $"TXN asset {tag}",
            SystemPositionId = systemPositionId
        };
        db.Assets.Add(asset);
        await db.SaveChangesAsync();
        return asset;
    }

    private static async Task<(MaintenanceCampaign Campaign, MaintenanceCampaignDeviceSnapshot Snapshot)> SeedCampaignAsync(
        AppDbContext db, Guid systemInfoId, Guid versionId)
    {
        var tag = Tag();
        var now = DateTime.UtcNow;
        var asset = new Asset { AssetTag = $"TXN-{tag}", Name = $"TXN asset {tag}" };
        db.Assets.Add(asset);
        await db.SaveChangesAsync();

        var campaign = new MaintenanceCampaign
        {
            SystemInfoId = systemInfoId,
            TemplateVersionId = versionId,
            StartDate = now,
            Status = MaintenanceCampaignStatus.InProgress,
            CreatedAt = now,
            UpdatedAt = now
        };
        campaign.DeviceSnapshots.Add(new MaintenanceCampaignDeviceSnapshot
        {
            AssetId = asset.Id,
            AssetTag = asset.AssetTag,
            AssetName = asset.Name,
            CreatedAt = now,
            UpdatedAt = now
        });
        db.MaintenanceCampaigns.Add(campaign);
        await db.SaveChangesAsync();
        return (campaign, campaign.DeviceSnapshots.First());
    }

    private static async Task<MaintenanceChecklistResult> SeedResultAsync(
        AppDbContext db, Guid campaignId, Guid snapshotId, Guid itemId)
    {
        var now = DateTime.UtcNow;
        var result = new MaintenanceChecklistResult
        {
            CampaignId = campaignId,
            DeviceSnapshotId = snapshotId,
            ChecklistItemId = itemId,
            MeasuredValue = "40",
            IsPass = true,
            Notes = "seed",
            CreatedAt = now,
            UpdatedAt = now
        };
        db.MaintenanceChecklistResults.Add(result);
        await db.SaveChangesAsync();
        return result;
    }

    // ==================== 1. TEMPLATE (create) ====================

    [ThrowawayDbFact]
    public async Task TemplateCreate_WritesTemplate_AndLog_OnRealPostgres()
    {
        await using var db = await OpenAsync(withActionLogBomb: false);
        var actor = await SeedActorAsync(db);
        var (system, _) = await SeedSystemAsync(db);
        var name = $"TXN create {Tag()}";

        var result = await new CreateMaintenanceTemplateCommandHandler(db, new TestHelpers.SuperUserScope(), Logs(db, actor.Id))
            .Handle(new CreateMaintenanceTemplateCommand(name, system.Id, null, actor.Id), default);

        Assert.True(result.Success, result.ErrorCode);
        Assert.NotNull(await db.MaintenanceChecklistTemplates.FirstOrDefaultAsync(t => t.Id == result.TemplateId));
        Assert.Equal(1, await db.ActionLogs.CountAsync(l => l.ItemId == result.TemplateId && l.ActionType == ActionType.Create));
    }

    [ThrowawayDbFact]
    public async Task TemplateCreate_ActionLogSaveFails_LeavesNoTemplateAndNoLog_OnRealPostgres()
    {
        await using var db = await OpenAsync(withActionLogBomb: true);
        var actor = await SeedActorAsync(db);
        var (system, _) = await SeedSystemAsync(db);
        var name = $"TXN create-bomb {Tag()}";

        await Assert.ThrowsAsync<InjectedActionLogFailure>(() =>
            new CreateMaintenanceTemplateCommandHandler(db, new TestHelpers.SuperUserScope(), Logs(db, actor.Id))
                .Handle(new CreateMaintenanceTemplateCommand(name, system.Id, null, actor.Id), default));

        await using var check = await OpenAsync(withActionLogBomb: false);
        Assert.Null(await check.MaintenanceChecklistTemplates.FirstOrDefaultAsync(t => t.Name == name));
        Assert.Equal(0, await check.ActionLogs.CountAsync(l => l.Note != null && l.Note.Contains(name)));
    }

    // ==================== 2. TEMPLATE (delete) ====================

    [ThrowawayDbFact]
    public async Task TemplateDelete_RemovesTemplate_AndLogs_OnRealPostgres()
    {
        await using var db = await OpenAsync(withActionLogBomb: false);
        var actor = await SeedActorAsync(db);
        var (system, _) = await SeedSystemAsync(db);
        var template = await SeedTemplateAsync(db, system.Id, actor.Id, published: false);

        var result = await new DeleteMaintenanceTemplateCommandHandler(db, new TestHelpers.SuperUserScope(), Logs(db, actor.Id))
            .Handle(new DeleteMaintenanceTemplateCommand(template.Id, actor.Id), default);

        Assert.True(result.Success, result.ErrorCode);
        Assert.Null(await db.MaintenanceChecklistTemplates.FirstOrDefaultAsync(t => t.Id == template.Id));
        Assert.Equal(1, await db.ActionLogs.CountAsync(l => l.ItemId == template.Id && l.ActionType == ActionType.Delete));
    }

    [ThrowawayDbFact]
    public async Task TemplateDelete_ActionLogSaveFails_KeepsTemplateAndNoLog_OnRealPostgres()
    {
        await using var db = await OpenAsync(withActionLogBomb: true);
        var actor = await SeedActorAsync(db);
        var (system, _) = await SeedSystemAsync(db);
        var template = await SeedTemplateAsync(db, system.Id, actor.Id, published: false);

        await Assert.ThrowsAsync<InjectedActionLogFailure>(() =>
            new DeleteMaintenanceTemplateCommandHandler(db, new TestHelpers.SuperUserScope(), Logs(db, actor.Id))
                .Handle(new DeleteMaintenanceTemplateCommand(template.Id, actor.Id), default));

        await using var check = await OpenAsync(withActionLogBomb: false);
        Assert.NotNull(await check.MaintenanceChecklistTemplates.FirstOrDefaultAsync(t => t.Id == template.Id));
        Assert.Equal(0, await check.ActionLogs.CountAsync(l => l.ItemId == template.Id && l.ActionType == ActionType.Delete));
    }

    // ==================== 3. VERSION (publish — 2 lần ghi dữ liệu TRƯỚC bước log) ====================

    [ThrowawayDbFact]
    public async Task VersionPublish_FlipsCurrentVersion_AndLogs_OnRealPostgres()
    {
        await using var db = await OpenAsync(withActionLogBomb: false);
        var actor = await SeedActorAsync(db);
        var (system, _) = await SeedSystemAsync(db);
        var template = await SeedTemplateAsync(db, system.Id, actor.Id, published: true);
        var currentId = template.Versions.First().Id;
        var draft = await SeedDraftVersionAsync(db, template.Id, actor.Id, versionNumber: 2);

        var result = await new PublishTemplateVersionCommandHandler(db, new TestHelpers.SuperUserScope(), Logs(db, actor.Id))
            .Handle(new PublishTemplateVersionCommand(template.Id, draft.Id, actor.Id), default);

        Assert.True(result.Success, result.ErrorCode);
        var promoted = await db.MaintenanceChecklistTemplateVersions.AsNoTracking().FirstAsync(v => v.Id == draft.Id);
        var demoted = await db.MaintenanceChecklistTemplateVersions.AsNoTracking().FirstAsync(v => v.Id == currentId);
        Assert.True(promoted.IsCurrent);
        Assert.NotNull(promoted.PublishedAt);
        Assert.False(demoted.IsCurrent);
        Assert.Equal(1, await db.ActionLogs.CountAsync(l => l.ItemId == draft.Id && l.ActionType == ActionType.Publish));
    }

    /// <summary>
    /// Ca mạnh nhất của bộ test: dữ liệu (demote + promote) được ghi bằng HAI SaveChanges TRƯỚC bước log
    /// — nếu không cùng transaction thì sau lỗi, version đã bị đảo trong DB mà nhật ký mất.
    /// </summary>
    [ThrowawayDbFact]
    public async Task VersionPublish_ActionLogSaveFails_RollsBackVersionFlip_OnRealPostgres()
    {
        await using var db = await OpenAsync(withActionLogBomb: true);
        var actor = await SeedActorAsync(db);
        var (system, _) = await SeedSystemAsync(db);
        var template = await SeedTemplateAsync(db, system.Id, actor.Id, published: true);
        var currentId = template.Versions.First().Id;
        var draft = await SeedDraftVersionAsync(db, template.Id, actor.Id, versionNumber: 2);

        await Assert.ThrowsAsync<InjectedActionLogFailure>(() =>
            new PublishTemplateVersionCommandHandler(db, new TestHelpers.SuperUserScope(), Logs(db, actor.Id))
                .Handle(new PublishTemplateVersionCommand(template.Id, draft.Id, actor.Id), default));

        await using var check = await OpenAsync(withActionLogBomb: false);
        var stillDraft = await check.MaintenanceChecklistTemplateVersions.AsNoTracking().FirstAsync(v => v.Id == draft.Id);
        var stillCurrent = await check.MaintenanceChecklistTemplateVersions.AsNoTracking().FirstAsync(v => v.Id == currentId);
        Assert.Null(stillDraft.PublishedAt);      // promote bị rollback
        Assert.False(stillDraft.IsCurrent);
        Assert.True(stillCurrent.IsCurrent);      // demote bị rollback
        Assert.Equal(0, await check.ActionLogs.CountAsync(l => l.ItemId == draft.Id && l.ActionType == ActionType.Publish));
    }

    // ==================== 4. ITEM (add) ====================

    [ThrowawayDbFact]
    public async Task ItemAdd_WritesItem_AndLog_OnRealPostgres()
    {
        await using var db = await OpenAsync(withActionLogBomb: false);
        var actor = await SeedActorAsync(db);
        var (system, _) = await SeedSystemAsync(db);
        var template = await SeedTemplateAsync(db, system.Id, actor.Id, published: false);
        var versionId = template.Versions.First().Id;
        var name = $"TXN item add {Tag()}";

        var result = await new AddChecklistItemCommandHandler(db, new TestHelpers.SuperUserScope(), Logs(db, actor.Id))
            .Handle(new AddChecklistItemCommand(template.Id, versionId, 1, name, 12, null, null, null, actor.Id), default);

        Assert.True(result.Success, result.ErrorCode);
        Assert.NotNull(await db.MaintenanceChecklistItems.FirstOrDefaultAsync(i => i.Id == result.ItemId));
        // Log của item dùng ItemId = TEMPLATE (Build không truyền itemId — hành vi verbatim giữ nguyên).
        Assert.Equal(1, await db.ActionLogs.CountAsync(l =>
            l.ItemId == template.Id && l.ActionType == ActionType.Create && l.Note != null && l.Note.Contains(name)));
    }

    [ThrowawayDbFact]
    public async Task ItemAdd_ActionLogSaveFails_LeavesNoItemAndNoLog_OnRealPostgres()
    {
        await using var db = await OpenAsync(withActionLogBomb: true);
        var actor = await SeedActorAsync(db);
        var (system, _) = await SeedSystemAsync(db);
        var template = await SeedTemplateAsync(db, system.Id, actor.Id, published: false);
        var versionId = template.Versions.First().Id;
        var name = $"TXN item bomb {Tag()}";

        await Assert.ThrowsAsync<InjectedActionLogFailure>(() =>
            new AddChecklistItemCommandHandler(db, new TestHelpers.SuperUserScope(), Logs(db, actor.Id))
                .Handle(new AddChecklistItemCommand(template.Id, versionId, 1, name, 12, null, null, null, actor.Id), default));

        await using var check = await OpenAsync(withActionLogBomb: false);
        Assert.Null(await check.MaintenanceChecklistItems.FirstOrDefaultAsync(i => i.Name == name));
        Assert.Equal(0, await check.ActionLogs.CountAsync(l =>
            l.ItemId == template.Id && l.ActionType == ActionType.Create && l.Note != null && l.Note.Contains(name)));
    }

    // ==================== 5. STANDARD PARAM (update) ====================

    [ThrowawayDbFact]
    public async Task ParamUpdate_WritesParam_AndLog_OnRealPostgres()
    {
        await using var db = await OpenAsync(withActionLogBomb: false);
        var actor = await SeedActorAsync(db);
        var (system, _) = await SeedSystemAsync(db);
        var template = await SeedTemplateAsync(db, system.Id, actor.Id, published: false);
        var versionId = template.Versions.First().Id;
        var item = await SeedItemAsync(db, versionId);
        var param = await SeedParamAsync(db, item.Id);
        var newName = $"TXN param updated {Tag()}";

        var result = await new UpdateStandardParamCommandHandler(db, new TestHelpers.SuperUserScope(), Logs(db, actor.Id))
            .Handle(new UpdateStandardParamCommand(template.Id, versionId, item.Id, param.Id,
                newName, null, null, null, null, null, actor.Id), default);

        Assert.True(result.Success, result.ErrorCode);
        var saved = await db.MaintenanceStandardParams.AsNoTracking().FirstAsync(p => p.Id == param.Id);
        Assert.Equal(newName, saved.ParamName);
        // Log của param cũng dùng ItemId = TEMPLATE (Build không truyền itemId).
        Assert.Equal(1, await db.ActionLogs.CountAsync(l => l.ItemId == template.Id && l.ActionType == ActionType.Update));
    }

    [ThrowawayDbFact]
    public async Task ParamUpdate_ActionLogSaveFails_KeepsOldValueAndNoLog_OnRealPostgres()
    {
        await using var db = await OpenAsync(withActionLogBomb: true);
        var actor = await SeedActorAsync(db);
        var (system, _) = await SeedSystemAsync(db);
        var template = await SeedTemplateAsync(db, system.Id, actor.Id, published: false);
        var versionId = template.Versions.First().Id;
        var item = await SeedItemAsync(db, versionId);
        var param = await SeedParamAsync(db, item.Id);
        var oldName = param.ParamName;
        var newName = $"TXN param bombed {Tag()}";

        await Assert.ThrowsAsync<InjectedActionLogFailure>(() =>
            new UpdateStandardParamCommandHandler(db, new TestHelpers.SuperUserScope(), Logs(db, actor.Id))
                .Handle(new UpdateStandardParamCommand(template.Id, versionId, item.Id, param.Id,
                    newName, null, null, null, null, null, actor.Id), default));

        await using var check = await OpenAsync(withActionLogBomb: false);
        var reloaded = await check.MaintenanceStandardParams.AsNoTracking().FirstAsync(p => p.Id == param.Id);
        Assert.Equal(oldName, reloaded.ParamName); // thay đổi bị rollback
        Assert.Equal(0, await check.ActionLogs.CountAsync(l => l.ItemId == template.Id && l.ActionType == ActionType.Update));
    }

    // ==================== 6. CAMPAIGN (create) ====================

    [ThrowawayDbFact]
    public async Task CampaignCreate_WritesCampaignAndSnapshot_AndLog_OnRealPostgres()
    {
        await using var db = await OpenAsync(withActionLogBomb: false);
        var actor = await SeedActorAsync(db);
        var (system, position) = await SeedSystemAsync(db);
        var template = await SeedTemplateAsync(db, system.Id, actor.Id, published: true);
        await SeedMountedAssetAsync(db, position.Id);

        var result = await new CreateCampaignCommandHandler(db, new TestHelpers.SuperUserScope(), Logs(db, actor.Id), new RowLockService(db))
            .Handle(new CreateCampaignCommand(system.Id, template.Id, null, null, $"BATCH-{Tag()}", null, null, actor.Id), default);

        Assert.True(result.Success, result.ErrorCode);
        var campaign = await db.MaintenanceCampaigns.AsNoTracking()
            .Include(c => c.DeviceSnapshots).FirstAsync(c => c.Id == result.CampaignId);
        Assert.Single(campaign.DeviceSnapshots);
        Assert.Equal(1, await db.ActionLogs.CountAsync(l => l.ItemId == result.CampaignId && l.ActionType == ActionType.Create));
    }

    [ThrowawayDbFact]
    public async Task CampaignCreate_ActionLogSaveFails_LeavesNoCampaignAndNoLog_OnRealPostgres()
    {
        await using var db = await OpenAsync(withActionLogBomb: true);
        var actor = await SeedActorAsync(db);
        var (system, position) = await SeedSystemAsync(db);
        var template = await SeedTemplateAsync(db, system.Id, actor.Id, published: true);
        await SeedMountedAssetAsync(db, position.Id);
        var batch = $"BATCH-{Tag()}";

        await Assert.ThrowsAsync<InjectedActionLogFailure>(() =>
            new CreateCampaignCommandHandler(db, new TestHelpers.SuperUserScope(), Logs(db, actor.Id), new RowLockService(db))
                .Handle(new CreateCampaignCommand(system.Id, template.Id, null, null, batch, null, null, actor.Id), default));

        await using var check = await OpenAsync(withActionLogBomb: false);
        Assert.Null(await check.MaintenanceCampaigns.FirstOrDefaultAsync(c => c.BatchNumber == batch));
    }

    // ==================== 7. CAMPAIGN (complete) ====================

    [ThrowawayDbFact]
    public async Task CampaignComplete_SetsStatus_AndLogs_OnRealPostgres()
    {
        await using var db = await OpenAsync(withActionLogBomb: false);
        var actor = await SeedActorAsync(db);
        var (system, _) = await SeedSystemAsync(db);
        var template = await SeedTemplateAsync(db, system.Id, actor.Id, published: true);
        var (campaign, _) = await SeedCampaignAsync(db, system.Id, template.Versions.First().Id);

        var result = await new CompleteCampaignCommandHandler(db, new TestHelpers.SuperUserScope(), Logs(db, actor.Id))
            .Handle(new CompleteCampaignCommand(campaign.Id, actor.Id), default);

        Assert.True(result.Success, result.ErrorCode);
        var saved = await db.MaintenanceCampaigns.AsNoTracking().FirstAsync(c => c.Id == campaign.Id);
        Assert.Equal(MaintenanceCampaignStatus.Completed, saved.Status);
        Assert.NotNull(saved.EndDate);
        Assert.Equal(1, await db.ActionLogs.CountAsync(l => l.ItemId == campaign.Id && l.ActionType == ActionType.Complete));
    }

    [ThrowawayDbFact]
    public async Task CampaignComplete_ActionLogSaveFails_KeepsInProgressAndNoLog_OnRealPostgres()
    {
        await using var db = await OpenAsync(withActionLogBomb: true);
        var actor = await SeedActorAsync(db);
        var (system, _) = await SeedSystemAsync(db);
        var template = await SeedTemplateAsync(db, system.Id, actor.Id, published: true);
        var (campaign, _) = await SeedCampaignAsync(db, system.Id, template.Versions.First().Id);

        await Assert.ThrowsAsync<InjectedActionLogFailure>(() =>
            new CompleteCampaignCommandHandler(db, new TestHelpers.SuperUserScope(), Logs(db, actor.Id))
                .Handle(new CompleteCampaignCommand(campaign.Id, actor.Id), default));

        await using var check = await OpenAsync(withActionLogBomb: false);
        var reloaded = await check.MaintenanceCampaigns.AsNoTracking().FirstAsync(c => c.Id == campaign.Id);
        Assert.Equal(MaintenanceCampaignStatus.InProgress, reloaded.Status); // trạng thái bị rollback
        Assert.Null(reloaded.EndDate);
        Assert.Equal(0, await check.ActionLogs.CountAsync(l => l.ItemId == campaign.Id && l.ActionType == ActionType.Complete));
    }

    // ==================== 8. RESULT (upsert) ====================

    [ThrowawayDbFact]
    public async Task ResultUpsert_InsertsResult_AndLogs_OnRealPostgres()
    {
        await using var db = await OpenAsync(withActionLogBomb: false);
        var actor = await SeedActorAsync(db);
        var (system, _) = await SeedSystemAsync(db);
        var template = await SeedTemplateAsync(db, system.Id, actor.Id, published: false);
        var versionId = template.Versions.First().Id;
        var item = await SeedItemAsync(db, versionId);
        var (campaign, snapshot) = await SeedCampaignAsync(db, system.Id, versionId);

        var outcome = await new UpsertCampaignResultCommandHandler(db, new TestHelpers.SuperUserScope(), Logs(db, actor.Id))
            .Handle(new UpsertCampaignResultCommand(campaign.Id, snapshot.Id, item.Id, "50", true, "g1", null, actor.Id), default);

        Assert.True(outcome.Success, outcome.ErrorCode);
        Assert.True(outcome.IsCreate);
        Assert.NotNull(await db.MaintenanceChecklistResults.AsNoTracking()
            .FirstOrDefaultAsync(r => r.CampaignId == campaign.Id && r.ChecklistItemId == item.Id));
        Assert.Equal(1, await db.ActionLogs.CountAsync(l => l.ItemId == campaign.Id && l.ActionType == ActionType.Create));
    }

    [ThrowawayDbFact]
    public async Task ResultUpsert_ActionLogSaveFails_LeavesNoResultAndNoLog_OnRealPostgres()
    {
        await using var db = await OpenAsync(withActionLogBomb: true);
        var actor = await SeedActorAsync(db);
        var (system, _) = await SeedSystemAsync(db);
        var template = await SeedTemplateAsync(db, system.Id, actor.Id, published: false);
        var versionId = template.Versions.First().Id;
        var item = await SeedItemAsync(db, versionId);
        var (campaign, snapshot) = await SeedCampaignAsync(db, system.Id, versionId);

        await Assert.ThrowsAsync<InjectedActionLogFailure>(() =>
            new UpsertCampaignResultCommandHandler(db, new TestHelpers.SuperUserScope(), Logs(db, actor.Id))
                .Handle(new UpsertCampaignResultCommand(campaign.Id, snapshot.Id, item.Id, "50", true, "g1", null, actor.Id), default));

        await using var check = await OpenAsync(withActionLogBomb: false);
        Assert.Null(await check.MaintenanceChecklistResults.AsNoTracking()
            .FirstOrDefaultAsync(r => r.CampaignId == campaign.Id && r.ChecklistItemId == item.Id));
    }

    // ==================== 9. RESULT (delete) ====================

    [ThrowawayDbFact]
    public async Task ResultDelete_RemovesRow_AndLogs_OnRealPostgres()
    {
        await using var db = await OpenAsync(withActionLogBomb: false);
        var actor = await SeedActorAsync(db);
        var (system, _) = await SeedSystemAsync(db);
        var template = await SeedTemplateAsync(db, system.Id, actor.Id, published: false);
        var versionId = template.Versions.First().Id;
        var item = await SeedItemAsync(db, versionId);
        var (campaign, snapshot) = await SeedCampaignAsync(db, system.Id, versionId);
        var result = await SeedResultAsync(db, campaign.Id, snapshot.Id, item.Id);

        var outcome = await new DeleteCampaignResultCommandHandler(db, new TestHelpers.SuperUserScope(), Logs(db, actor.Id))
            .Handle(new DeleteCampaignResultCommand(campaign.Id, snapshot.Id, item.Id, null, actor.Id), default);

        Assert.True(outcome.Success, outcome.ErrorCode);
        Assert.Null(await db.MaintenanceChecklistResults.AsNoTracking().FirstOrDefaultAsync(r => r.Id == result.Id));
        Assert.Equal(1, await db.ActionLogs.CountAsync(l => l.ItemId == campaign.Id && l.ActionType == ActionType.Delete));
    }

    [ThrowawayDbFact]
    public async Task ResultDelete_ActionLogSaveFails_KeepsRowAndNoLog_OnRealPostgres()
    {
        await using var db = await OpenAsync(withActionLogBomb: true);
        var actor = await SeedActorAsync(db);
        var (system, _) = await SeedSystemAsync(db);
        var template = await SeedTemplateAsync(db, system.Id, actor.Id, published: false);
        var versionId = template.Versions.First().Id;
        var item = await SeedItemAsync(db, versionId);
        var (campaign, snapshot) = await SeedCampaignAsync(db, system.Id, versionId);
        var result = await SeedResultAsync(db, campaign.Id, snapshot.Id, item.Id);

        await Assert.ThrowsAsync<InjectedActionLogFailure>(() =>
            new DeleteCampaignResultCommandHandler(db, new TestHelpers.SuperUserScope(), Logs(db, actor.Id))
                .Handle(new DeleteCampaignResultCommand(campaign.Id, snapshot.Id, item.Id, null, actor.Id), default));

        await using var check = await OpenAsync(withActionLogBomb: false);
        Assert.NotNull(await check.MaintenanceChecklistResults.AsNoTracking().FirstOrDefaultAsync(r => r.Id == result.Id));
        Assert.Equal(0, await check.ActionLogs.CountAsync(l => l.ItemId == campaign.Id && l.ActionType == ActionType.Delete));
    }
}
