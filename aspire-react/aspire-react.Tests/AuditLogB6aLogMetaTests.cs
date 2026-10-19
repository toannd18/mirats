using System.Text.Json;
using aspire_react.Server.Application.AssetMaintenances.Commands;
using aspire_react.Server.Application.MaintenanceTemplates.Commands;
using aspire_react.Server.Application.Users.Commands;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Infrastructure.Authorization;
using aspire_react.Server.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace aspire_react.Tests;

/// <summary>
/// [AUDIT-LOG B6a] Các field từng THIẾU trong `LogMeta.changes`:
/// <list type="bullet">
///   <item><c>UpdateUserCommand</c>: employee_number, job_title (trước chỉ 8/10 field) — dùng helper.</item>
///   <item><c>UpdateChecklistItemCommand</c>: position_ids (old[]/new[], so KHÔNG phân biệt thứ tự).</item>
///   <item><c>Close/Inspect/ReopenMaintenanceCommand</c>: status old→new + các trường kết quả lệnh đó ghi
///   (closed_at/closed_by_id; inspected_at/inspected_by_id).</item>
/// </list>
/// Mọi test khẳng định <c>old</c> là giá trị CŨ THẬT (chụp trước khi gán).
/// </summary>
public class AuditLogB6aLogMetaTests
{
    private static JsonElement Changes(string? logMeta)
    {
        Assert.False(string.IsNullOrWhiteSpace(logMeta), "LogMeta phải có changes");
        using var doc = JsonDocument.Parse(logMeta!);
        return doc.RootElement.GetProperty("changes").Clone();
    }

    private static UpdateUserCommandHandler UserHandler(AppDbContext ctx, Guid actorId)
        => new(ctx, new TestHelpers.FakeScope { Super = true }, new PermissionLockoutGuard(ctx),
            NullLogger<UpdateUserCommandHandler>.Instance);

    // ==================== 1. UpdateUserCommand ====================

    [Fact]
    public async Task UpdateUser_EmployeeNumberAndJobTitle_LogsRealOldValues()
    {
        await using var ctx = TestHelpers.CreateContext(nameof(UpdateUser_EmployeeNumberAndJobTitle_LogsRealOldValues));
        var actor = Guid.NewGuid();
        var user = new User
        {
            Username = "b6a-user",
            Email = "b6a@test.local",
            FirstName = "First",
            LastName = "Last",
            EmployeeNumber = "EMP-1",
            JobTitle = "Chuyên viên",
            IsActive = true
        };
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();

        var result = await UserHandler(ctx, actor).Handle(new UpdateUserCommand
        {
            Id = user.Id,
            EmployeeNumber = "EMP-2",
            JobTitle = "Trưởng nhóm",
            CurrentUserId = actor,
            ActorIsRealmSuperUser = true
        }, CancellationToken.None);

        Assert.True(result.Success, result.ErrorCode);
        var changes = Changes(result.LogMeta);
        Assert.Equal("EMP-1", changes.GetProperty("employee_number").GetProperty("old").GetString());
        Assert.Equal("EMP-2", changes.GetProperty("employee_number").GetProperty("new").GetString());
        Assert.Equal("Chuyên viên", changes.GetProperty("job_title").GetProperty("old").GetString());
        Assert.Equal("Trưởng nhóm", changes.GetProperty("job_title").GetProperty("new").GetString());
    }

    [Fact]
    public async Task UpdateUser_NoFieldChanged_LogsNoChanges()
    {
        await using var ctx = TestHelpers.CreateContext(nameof(UpdateUser_NoFieldChanged_LogsNoChanges));
        var actor = Guid.NewGuid();
        var user = new User
        {
            Username = "b6a-user2",
            Email = "b6a2@test.local",
            FirstName = "First",
            LastName = "Last",
            EmployeeNumber = "EMP-1",
            JobTitle = "Chuyên viên",
            IsActive = true
        };
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();

        var result = await UserHandler(ctx, actor).Handle(new UpdateUserCommand
        {
            Id = user.Id,
            EmployeeNumber = "EMP-1",
            JobTitle = "Chuyên viên",
            CurrentUserId = actor,
            ActorIsRealmSuperUser = true
        }, CancellationToken.None);

        Assert.True(result.Success, result.ErrorCode);
        Assert.Null(result.LogMeta);
    }

    // ==================== 2. UpdateChecklistItemCommand — position_ids ====================

    private static async Task<(MaintenanceChecklistTemplate Template, MaintenanceChecklistItem Item,
        SystemPosition P1, SystemPosition P2, SystemPosition P3)> SeedItemWithPositionsAsync(AppDbContext ctx, Guid actor)
    {
        var system = new SystemInfo { Code = "B6A-" + Guid.NewGuid().ToString("N")[..8], Name = "B6A system" };
        ctx.SystemInfos.Add(system);
        await ctx.SaveChangesAsync();
        var p1 = new SystemPosition { SystemInfoId = system.Id, Code = "P1", Name = "Vị trí 1" };
        var p2 = new SystemPosition { SystemInfoId = system.Id, Code = "P2", Name = "Vị trí 2" };
        var p3 = new SystemPosition { SystemInfoId = system.Id, Code = "P3", Name = "Vị trí 3" };
        ctx.SystemPositions.AddRange(p1, p2, p3);
        await ctx.SaveChangesAsync();

        var template = new MaintenanceChecklistTemplate { Name = "B6A template", SystemInfoId = system.Id, CreatedById = actor };
        var version = new MaintenanceChecklistTemplateVersion { TemplateId = template.Id, VersionNumber = 1, CreatedById = actor };
        template.Versions.Add(version);
        ctx.MaintenanceChecklistTemplates.Add(template);
        await ctx.SaveChangesAsync();

        var item = new MaintenanceChecklistItem { TemplateVersionId = version.Id, Order = 1, Name = "Hạng mục", CycleMonths = 12 };
        item.Positions.Add(new MaintenanceChecklistItemPosition { SystemPositionId = p1.Id });
        item.Positions.Add(new MaintenanceChecklistItemPosition { SystemPositionId = p2.Id });
        ctx.MaintenanceChecklistItems.Add(item);
        await ctx.SaveChangesAsync();

        return (template, item, p1, p2, p3);
    }

    [Fact]
    public async Task UpdateChecklistItem_PositionIds_Change_LogsOldAndNewSets()
    {
        await using var ctx = TestHelpers.CreateContext(nameof(UpdateChecklistItem_PositionIds_Change_LogsOldAndNewSets));
        var actor = Guid.NewGuid();
        var (template, item, p1, p2, p3) = await SeedItemWithPositionsAsync(ctx, actor);

        var result = await new UpdateChecklistItemCommandHandler(ctx, new TestHelpers.SuperUserScope(),
                TestHelpers.CreateActionLogService(ctx, actor))
            .Handle(new UpdateChecklistItemCommand(template.Id, item.TemplateVersionId, item.Id,
                null, null, null, null, null, new[] { p1.Id, p3.Id }, actor), CancellationToken.None);

        Assert.True(result.Success, result.ErrorCode);
        var log = await ctx.ActionLogs.SingleAsync(l => l.ItemType == ItemType.MaintenanceChecklistTemplate && l.ActionType == ActionType.Update);
        var positionIds = Changes(log.LogMeta).GetProperty("position_ids");

        var oldIds = positionIds.GetProperty("old").EnumerateArray().Select(x => x.GetGuid()).ToArray();
        var newIds = positionIds.GetProperty("new").EnumerateArray().Select(x => x.GetGuid()).ToArray();
        Assert.Equal(2, oldIds.Length);
        Assert.Contains(p1.Id, oldIds);
        Assert.Contains(p2.Id, oldIds);
        Assert.Equal(2, newIds.Length);
        Assert.Contains(p1.Id, newIds);
        Assert.Contains(p3.Id, newIds);
    }

    /// <summary>Gửi lại CÙNG tập vị trí nhưng KHÁC THỨ TỰ ⇒ không phải thay đổi (so tập, không so mảng).</summary>
    [Fact]
    public async Task UpdateChecklistItem_PositionIds_SameSetDifferentOrder_IsNotAChange()
    {
        await using var ctx = TestHelpers.CreateContext(nameof(UpdateChecklistItem_PositionIds_SameSetDifferentOrder_IsNotAChange));
        var actor = Guid.NewGuid();
        var (template, item, p1, p2, _) = await SeedItemWithPositionsAsync(ctx, actor);

        var result = await new UpdateChecklistItemCommandHandler(ctx, new TestHelpers.SuperUserScope(),
                TestHelpers.CreateActionLogService(ctx, actor))
            .Handle(new UpdateChecklistItemCommand(template.Id, item.TemplateVersionId, item.Id,
                null, null, null, null, null, new[] { p2.Id, p1.Id }, actor), CancellationToken.None);

        Assert.True(result.Success, result.ErrorCode);
        var log = await ctx.ActionLogs.SingleAsync(l => l.ItemType == ItemType.MaintenanceChecklistTemplate && l.ActionType == ActionType.Update);
        using var doc = JsonDocument.Parse(log.LogMeta!);
        var changes = doc.RootElement.GetProperty("changes");
        Assert.False(changes.TryGetProperty("position_ids", out _), "Cùng tập vị trí (khác thứ tự) không được coi là thay đổi");
    }

    // ==================== 3. Close / Inspect / Reopen ====================

    private static async Task<AssetMaintenance> SeedMaintenanceAsync(
        AppDbContext ctx, bool closed = false, Guid? inspectedById = null)
    {
        var asset = new Asset { AssetTag = "B6A-" + Guid.NewGuid().ToString("N")[..8], Name = "B6A asset" };
        ctx.Assets.Add(asset);
        await ctx.SaveChangesAsync();

        var maintenance = new AssetMaintenance
        {
            AssetId = asset.Id,
            Type = AssetMaintenanceType.Maintenance,
            Title = "Bảo trì B6A",
            CompanyId = Guid.Empty,
            StartDate = DateTime.UtcNow.AddDays(-2),
            CompletionDate = DateTime.UtcNow.AddDays(-1),
            InspectedById = inspectedById,
            InspectedAt = inspectedById.HasValue ? DateTime.UtcNow.AddHours(-1) : null,
            IsClosed = closed,
            ClosedAt = closed ? DateTime.UtcNow.AddMinutes(-30) : null,
            ClosedById = closed ? inspectedById : null
        };
        ctx.AssetMaintenances.Add(maintenance);
        await ctx.SaveChangesAsync();
        return maintenance;
    }

    [Fact]
    public async Task CloseMaintenance_LogsStatusAndResultFields()
    {
        await using var ctx = TestHelpers.CreateContext(nameof(CloseMaintenance_LogsStatusAndResultFields));
        var actor = Guid.NewGuid();
        var inspector = Guid.NewGuid();
        var maintenance = await SeedMaintenanceAsync(ctx, inspectedById: inspector);

        var mediator = TestHelpers.BuildMediator(ctx, new TestHelpers.SuperUserScope(), actor);
        var result = await mediator.Send(new CloseMaintenanceCommand(maintenance.Id, actor));

        Assert.True(result.Success, result.ErrorCode);
        var log = await ctx.ActionLogs.SingleAsync(l => l.ItemType == ItemType.AssetMaintenance && l.ActionType == ActionType.Close);
        var changes = Changes(log.LogMeta);

        Assert.Equal("Open", changes.GetProperty("status").GetProperty("old").GetString());
        Assert.Equal("Closed", changes.GetProperty("status").GetProperty("new").GetString());
        Assert.Equal(JsonValueKind.Null, changes.GetProperty("closed_at").GetProperty("old").ValueKind);
        Assert.NotEqual(JsonValueKind.Null, changes.GetProperty("closed_at").GetProperty("new").ValueKind);
        Assert.Equal(JsonValueKind.Null, changes.GetProperty("closed_by_id").GetProperty("old").ValueKind);
        Assert.Equal(actor, changes.GetProperty("closed_by_id").GetProperty("new").GetGuid());
    }

    [Fact]
    public async Task InspectMaintenance_SecondInspect_LogsPreviousInspectorAsOld()
    {
        await using var ctx = TestHelpers.CreateContext(nameof(InspectMaintenance_SecondInspect_LogsPreviousInspectorAsOld));
        var firstInspector = Guid.NewGuid();
        var secondInspector = Guid.NewGuid();
        var maintenance = await SeedMaintenanceAsync(ctx);

        var first = await TestHelpers.BuildMediator(ctx, new TestHelpers.SuperUserScope(), firstInspector)
            .Send(new InspectMaintenanceCommand(maintenance.Id, firstInspector));
        Assert.True(first.Success, first.ErrorCode);
        var firstLog = await ctx.ActionLogs.SingleAsync(l => l.ItemType == ItemType.AssetMaintenance && l.ActionType == ActionType.Inspect);

        var second = await TestHelpers.BuildMediator(ctx, new TestHelpers.SuperUserScope(), secondInspector)
            .Send(new InspectMaintenanceCommand(maintenance.Id, secondInspector));
        Assert.True(second.Success, second.ErrorCode);
        var secondLog = await ctx.ActionLogs.SingleAsync(l =>
            l.ItemType == ItemType.AssetMaintenance && l.ActionType == ActionType.Inspect && l.Id != firstLog.Id);

        var firstChanges = Changes(firstLog.LogMeta);
        Assert.Equal(JsonValueKind.Null, firstChanges.GetProperty("inspected_by_id").GetProperty("old").ValueKind);
        Assert.Equal(firstInspector, firstChanges.GetProperty("inspected_by_id").GetProperty("new").GetGuid());

        // Lượt 2: old là người kiểm tra THẬT của lượt trước (không phải null, không phải chính mình).
        var secondChanges = Changes(secondLog.LogMeta);
        Assert.Equal(firstInspector, secondChanges.GetProperty("inspected_by_id").GetProperty("old").GetGuid());
        Assert.Equal(secondInspector, secondChanges.GetProperty("inspected_by_id").GetProperty("new").GetGuid());
        Assert.NotEqual(JsonValueKind.Null, secondChanges.GetProperty("inspected_at").GetProperty("old").ValueKind);
    }

    [Fact]
    public async Task ReopenMaintenance_LogsOnlyStatusClosedToOpen()
    {
        await using var ctx = TestHelpers.CreateContext(nameof(ReopenMaintenance_LogsOnlyStatusClosedToOpen));
        var actor = Guid.NewGuid();
        var maintenance = await SeedMaintenanceAsync(ctx, closed: true, inspectedById: actor);

        var result = await TestHelpers.BuildMediator(ctx, new TestHelpers.SuperUserScope(), actor)
            .Send(new ReopenMaintenanceCommand(maintenance.Id, actor));

        Assert.True(result.Success, result.ErrorCode);
        var log = await ctx.ActionLogs.SingleAsync(l => l.ItemType == ItemType.AssetMaintenance && l.ActionType == ActionType.Reopen);
        var changes = Changes(log.LogMeta);

        Assert.Equal("Closed", changes.GetProperty("status").GetProperty("old").GetString());
        Assert.Equal("Open", changes.GetProperty("status").GetProperty("new").GetString());
        // ClosedAt/ClosedById CỐ Ý giữ nguyên ⇒ không phải thay đổi ⇒ không nằm trong changes.
        Assert.Single(changes.EnumerateObject());
    }
}
