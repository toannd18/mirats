using System.Text.Json;
using aspire_react.Server.Application.Accessories.Commands;
using aspire_react.Server.Application.AssetMaintenances.Commands;
using aspire_react.Server.Application.Assets.Commands;
using aspire_react.Server.Application.Categories.Commands;
using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Application.Companies.Commands;
using aspire_react.Server.Application.Components.Commands;
using aspire_react.Server.Application.ComponentUnits.Commands;
using aspire_react.Server.Application.Consumables.Commands;
using aspire_react.Server.Application.CustomFields.Commands;
using aspire_react.Server.Application.Departments.Commands;
using aspire_react.Server.Application.Groups.Commands;
using aspire_react.Server.Application.Licenses.Commands;
using aspire_react.Server.Application.Locations.Commands;
using aspire_react.Server.Application.MaintenanceCampaigns.Commands;
using aspire_react.Server.Application.MaintenanceTemplates.Commands;
using aspire_react.Server.Application.Manufacturers.Commands;
using aspire_react.Server.Application.Models.Commands;
using aspire_react.Server.Application.Suppliers.Commands;
using aspire_react.Server.Application.SystemConfig.Commands;
using aspire_react.Server.Application.SystemInfos.Commands;
using aspire_react.Server.Application.Systems.Commands;
using aspire_react.Server.Application.Users.Commands;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using aspire_react.Server.Domain.SystemConfig;
using aspire_react.Server.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace aspire_react.Tests;

/// <summary>
/// [AUDIT-LOG B7b — GĐ2.4] BÁNH CÓC THEO HÀNH VI: chạy THẬT từng command ghi dữ liệu (Update/Upsert/Set/
/// Edit/Patch/Close/Inspect/Reopen) với DỮ LIỆU THẬT (đổi 1 field), rồi đọc `action_logs.LogMeta`, parse
/// JSON và khẳng định `changes.<field>` có `old` là giá trị CŨ THẬT và `new` là giá trị mới.
///
/// <para>
/// Thay hẳn cách cũ (test <c>ActionLogMetaRatchetTests</c> chỉ TÌM CHỮ "changes" trong file .cs — không
/// thấy được log viết trong service, không kiểm được old/new). Bánh cóc 2 chiều ở đây:
/// (1) <see cref="EveryScannedCommand_HasABehaviorSpec"/>: mọi command trong phạm vi quét PHẢI có spec
///     hành vi — thêm command mới mà quên ⇒ FAIL (kèm probe âm khi phát triển).
/// (2) <see cref="EveryWriteCommand_LogsChangesWithRealOldAndNew"/>: spec chạy thật, LogMeta phải có
///     `changes` với old/new đúng ⇒ command ghi log thiếu/sai ⇒ FAIL.
/// KHÔNG còn danh sách ngoại lệ (Exempt) nào.
/// </para>
/// </summary>
public class ActionLogBehaviorRatchetTests
{
    private static readonly Guid Actor = Guid.NewGuid();

    /// <summary>Tiền tố "ghi dữ liệu" trong phạm vi quét (giữ nguyên như bánh cóc cũ).</summary>
    private static readonly string[] Prefixes = { "Update", "Upsert", "Set", "Edit", "Patch", "Close", "Inspect", "Reopen" };

    private sealed record Spec(string Command, Func<AppDbContext, MediatR.IMediator, Task> Run);

    // ==================== bánh cóc 1: mọi command phải có spec ====================

    [Fact]
    public void EveryScannedCommand_HasABehaviorSpec()
    {
        var assembly = typeof(ILoggableCommand<>).Assembly;
        var scanned = assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && typeof(IBaseRequest).IsAssignableFrom(t))
            .Select(t => t.Name)
            .Where(n => Prefixes.Any(p => n.StartsWith(p, StringComparison.Ordinal)))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        Assert.NotEmpty(scanned);
        var speced = AllSpecs().Select(s => s.Command).ToHashSet(StringComparer.Ordinal);

        var missing = scanned.Where(n => !speced.Contains(n)).ToList();
        var stale = speced.Where(n => !scanned.Contains(n)).ToList();

        Assert.True(missing.Count == 0,
            "Command ghi dữ liệu CHƯA có spec hành vi (thêm vào AllSpecs): " + string.Join(", ", missing));
        Assert.True(stale.Count == 0,
            "Spec không còn là command trong phạm vi quét (xóa khỏi AllSpecs): " + string.Join(", ", stale));
    }

    // ==================== bánh cóc 2: hành vi thật ====================

    [Fact]
    public async Task EveryWriteCommand_LogsChangesWithRealOldAndNew()
    {
        var failures = new List<string>();
        foreach (var spec in AllSpecs())
        {
            try
            {
                await using var ctx = NewContext(spec.Command);
                await spec.Run(ctx, TestHelpers.BuildMediator(ctx, new TestHelpers.SuperUserScope(), Actor));
            }
            catch (Exception ex)
            {
                failures.Add($"{spec.Command}: {ex.Message}");
            }
        }

        Assert.True(failures.Count == 0,
            "Command ghi dữ liệu nhưng LogMeta.changes SAI (old phải là giá trị CŨ thật):\n" + string.Join("\n", failures));
    }

    // ==================== hạ tầng ====================

    private static AppDbContext NewContext(string name) => TestHelpers.CreateContext("behavior-" + name);

    private static async Task<ActionLog> LatestLogAsync(AppDbContext ctx, ItemType itemType, Guid itemId)
    {
        var log = await ctx.ActionLogs.AsNoTracking()
            .Where(l => l.ItemType == itemType && l.ItemId == itemId)
            .OrderByDescending(l => l.ActionDate)
            .FirstOrDefaultAsync();
        Assert.True(log != null, $"Không có ActionLog nào cho {itemType}/{itemId}");
        return log!;
    }

    private static JsonElement ChangesOf(ActionLog log)
    {
        Assert.False(string.IsNullOrWhiteSpace(log.LogMeta), $"LogMeta NULL cho {log.ItemType}/{log.ItemId}");
        using var doc = JsonDocument.Parse(log.LogMeta!);
        Assert.True(doc.RootElement.TryGetProperty("changes", out var changes),
            $"LogMeta thiếu wrapper 'changes' (meta={log.LogMeta})");
        return changes.Clone();
    }

    private static string? Raw(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        JsonValueKind.String => element.GetString(),
        _ => element.GetRawText()
    };

    /// <summary>changes.field phải có old/new ĐÚNG như mong đợi (old = giá trị cũ thật).</summary>
    private static async Task AssertChangeAsync(
        AppDbContext ctx, ItemType itemType, Guid itemId, string field, string? expectedOld, string? expectedNew)
    {
        var changes = ChangesOf(await LatestLogAsync(ctx, itemType, itemId));
        Assert.True(changes.TryGetProperty(field, out var entry),
            $"changes thiếu field '{field}' (có: {string.Join(",", changes.EnumerateObject().Select(p => p.Name))})");
        Assert.Equal(expectedOld, Raw(entry.GetProperty("old")));
        Assert.Equal(expectedNew, Raw(entry.GetProperty("new")));
    }

    /// <summary>changes.field phải TỒN TẠI và old KHÁC new (dùng cho tập hợp: permissions/groupIds).</summary>
    private static async Task AssertChangeDiffersAsync(AppDbContext ctx, ItemType itemType, Guid itemId, string field)
    {
        var changes = ChangesOf(await LatestLogAsync(ctx, itemType, itemId));
        Assert.True(changes.TryGetProperty(field, out var entry), $"changes thiếu field '{field}'");
        Assert.NotEqual(Raw(entry.GetProperty("old")), Raw(entry.GetProperty("new")));
    }

    private static DateTime Unspecified(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Unspecified);

    private static async Task<AssetMaintenance> SeedMaintenanceAsync(AppDbContext ctx, bool closed = false, Guid? inspectedBy = null)
    {
        var asset = new Asset { AssetTag = "RATCHET-" + Guid.NewGuid().ToString("N")[..8], Name = "ratchet asset" };
        ctx.Assets.Add(asset);
        await ctx.SaveChangesAsync();

        var now = Unspecified(DateTime.UtcNow);
        var m = new AssetMaintenance
        {
            AssetId = asset.Id,
            Type = AssetMaintenanceType.Maintenance,
            Title = "Bảo trì gốc",
            CompanyId = Guid.Empty,
            StartDate = now.AddDays(-2),
            CompletionDate = now.AddDays(-1),
            InspectedById = inspectedBy,
            InspectedAt = inspectedBy.HasValue ? now.AddHours(-1) : null,
            IsClosed = closed,
            ClosedAt = closed ? now.AddMinutes(-30) : null,
            ClosedById = closed ? inspectedBy : null,
            CreatedById = Actor,
            CreatedAt = now,
            UpdatedAt = now
        };
        ctx.AssetMaintenances.Add(m);
        await ctx.SaveChangesAsync();
        return m;
    }

    private static async Task<(MaintenanceChecklistTemplate Template, MaintenanceChecklistTemplateVersion Version)> SeedTemplateAsync(
        AppDbContext ctx, bool published = false)
    {
        var system = new SystemInfo { Code = "RAT-" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant(), Name = "ratchet system" };
        ctx.SystemInfos.Add(system);
        await ctx.SaveChangesAsync();

        var now = DateTime.UtcNow;
        var template = new MaintenanceChecklistTemplate
        {
            Name = "Template gốc",
            SystemInfoId = system.Id,
            CreatedById = Actor,
            CreatedAt = now,
            UpdatedAt = now
        };
        var version = new MaintenanceChecklistTemplateVersion
        {
            TemplateId = template.Id,
            VersionNumber = 1,
            CreatedById = Actor,
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
        ctx.MaintenanceChecklistTemplates.Add(template);
        await ctx.SaveChangesAsync();
        return (template, version);
    }

    // ==================== SPECS (chạy thật từng command) ====================

    private static List<Spec> AllSpecs() => new()
    {
        new("UpdateCategoryCommand", async (ctx, m) =>
        {
            var e = new Category { Name = "Danh mục gốc", CategoryType = CategoryType.Component };
            ctx.Categories.Add(e);
            await ctx.SaveChangesAsync();
            await m.Send(new UpdateCategoryCommand(e.Id, "Danh mục mới", null, null, null, null, null, Actor));
            await AssertChangeAsync(ctx, ItemType.Category, e.Id, "name", "Danh mục gốc", "Danh mục mới");
        }),

        new("UpdateCompanyCommand", async (ctx, m) =>
        {
            var e = new Company { Name = "Công ty gốc", Code = "RATC" };
            ctx.Companies.Add(e);
            await ctx.SaveChangesAsync();
            await m.Send(new UpdateCompanyCommand(e.Id, "Công ty mới", null, "RATC", Actor));
            await AssertChangeAsync(ctx, ItemType.Company, e.Id, "name", "Công ty gốc", "Công ty mới");
        }),

        new("UpdateCustomFieldCommand", async (ctx, m) =>
        {
            var e = new CustomField { Name = "Trường gốc", Slug = "truong-goc" };
            ctx.CustomFields.Add(e);
            await ctx.SaveChangesAsync();
            await m.Send(new UpdateCustomFieldCommand(e.Id, "Trường mới", null, null, null, null, null, null, null, Actor));
            await AssertChangeAsync(ctx, ItemType.CustomField, e.Id, "name", "Trường gốc", "Trường mới");
        }),

        new("UpdateDepartmentCommand", async (ctx, m) =>
        {
            var e = new Department { Name = "Phòng gốc" };
            ctx.Departments.Add(e);
            await ctx.SaveChangesAsync();
            await m.Send(new UpdateDepartmentCommand(e.Id, "Phòng mới", null, null, null, null, Actor));
            await AssertChangeAsync(ctx, ItemType.Department, e.Id, "name", "Phòng gốc", "Phòng mới");
        }),

        new("UpdateGroupCommand", async (ctx, m) =>
        {
            var e = new PermissionGroup { Name = "Nhóm gốc", Description = "mô tả" };
            ctx.PermissionGroups.Add(e);
            await ctx.SaveChangesAsync();
            await m.Send(new UpdateGroupCommand(e.Id, "Nhóm mới", null, Actor));
            await AssertChangeAsync(ctx, ItemType.PermissionGroup, e.Id, "name", "Nhóm gốc", "Nhóm mới");
        }),

        new("UpdateGroupPermissionsCommand", async (ctx, m) =>
        {
            var e = new PermissionGroup { Name = "Nhóm quyền" };
            ctx.PermissionGroups.Add(e);
            await ctx.SaveChangesAsync();
            await m.Send(new UpdateGroupPermissionsCommand(e.Id,
                new[] { new GroupPermissionEntry("assets.view", PermissionValue.Grant) }, Actor, true));
            await AssertChangeDiffersAsync(ctx, ItemType.PermissionGroup, e.Id, "permissions");
        }),

        new("UpdateLocationCommand", async (ctx, m) =>
        {
            var e = new Location { Name = "Vị trí gốc" };
            ctx.Locations.Add(e);
            await ctx.SaveChangesAsync();
            await m.Send(new UpdateLocationCommand(e.Id, "Vị trí mới", null, null, null, null, null, null, null, null, Actor));
            await AssertChangeAsync(ctx, ItemType.Location, e.Id, "name", "Vị trí gốc", "Vị trí mới");
        }),

        new("UpdateManufacturerCommand", async (ctx, m) =>
        {
            var e = new Manufacturer { Code = "OLDM", Name = "NSX gốc" };
            ctx.Manufacturers.Add(e);
            await ctx.SaveChangesAsync();
            await m.Send(new UpdateManufacturerCommand(e.Id, "NEWM", "NSX mới", null, null, null, Actor));
            await AssertChangeAsync(ctx, ItemType.Manufacturer, e.Id, "code", "OLDM", "NEWM");
        }),

        new("UpdateModelCommand", async (ctx, m) =>
        {
            var e = new AssetModel { Name = "Model gốc" };
            ctx.Models.Add(e);
            await ctx.SaveChangesAsync();
            await m.Send(new UpdateModelCommand(e.Id, "Model mới", null, null, null, null, null, null, null, null, Actor));
            await AssertChangeAsync(ctx, ItemType.Model, e.Id, "name", "Model gốc", "Model mới");
        }),

        new("UpdateSupplierCommand", async (ctx, m) =>
        {
            var e = new Supplier { Code = "OLDS", Name = "NCC gốc" };
            ctx.Suppliers.Add(e);
            await ctx.SaveChangesAsync();
            await m.Send(new UpdateSupplierCommand(e.Id, "NEWS", "NCC mới", null, null, null, null, null, null, null, null, null, null, Actor));
            await AssertChangeAsync(ctx, ItemType.Supplier, e.Id, "code", "OLDS", "NEWS");
        }),

        new("UpdateSystemInfoCommand", async (ctx, m) =>
        {
            var e = new SystemInfo { Code = "RAT-0001-001", Name = "Hệ thống gốc" };
            ctx.SystemInfos.Add(e);
            await ctx.SaveChangesAsync();
            await m.Send(new UpdateSystemInfoCommand(e.Id, "RAT-0001-001", "Hệ thống mới", null, null, Actor));
            await AssertChangeAsync(ctx, ItemType.SystemInfo, e.Id, "name", "Hệ thống gốc", "Hệ thống mới");
        }),

        new("UpdateSystemPositionCommand", async (ctx, m) =>
        {
            var sys = new SystemInfo { Code = "RAT-0002-002", Name = "sys" };
            ctx.SystemInfos.Add(sys);
            await ctx.SaveChangesAsync();
            var e = new SystemPosition { SystemInfoId = sys.Id, Code = "POS-0002-001", Name = "Vị trí gốc" };
            ctx.SystemPositions.Add(e);
            await ctx.SaveChangesAsync();
            await m.Send(new UpdateSystemPositionCommand(sys.Id, e.Id, "POS-0002-001", "Vị trí mới", null, Actor));
            await AssertChangeAsync(ctx, ItemType.SystemPosition, e.Id, "name", "Vị trí gốc", "Vị trí mới");
        }),

        new("UpdateAccessoryCommand", async (ctx, m) =>
        {
            var e = new Accessory { Name = "Phụ kiện gốc", Qty = 5, MinAmt = 1 };
            ctx.Accessories.Add(e);
            await ctx.SaveChangesAsync();
            await m.Send(new UpdateAccessoryCommand { Id = e.Id, Name = "Phụ kiện mới", CurrentUserId = Actor });
            await AssertChangeAsync(ctx, ItemType.Accessory, e.Id, "name", "Phụ kiện gốc", "Phụ kiện mới");
        }),

        new("UpdateAssetCommand", async (ctx, m) =>
        {
            var e = new Asset { AssetTag = "RATCHET-ASSET-1", Name = "Thiết bị gốc" };
            ctx.Assets.Add(e);
            await ctx.SaveChangesAsync();
            await m.Send(new UpdateAssetCommand
            {
                Id = e.Id,
                AssetTag = e.AssetTag,
                Name = "Thiết bị mới",
                CurrentUserId = Actor
            });
            await AssertChangeAsync(ctx, ItemType.Asset, e.Id, "name", "Thiết bị gốc", "Thiết bị mới");
        }),

        new("UpdateComponentCommand", async (ctx, m) =>
        {
            var e = new Component { Name = "Linh kiện gốc", Qty = 3 };
            ctx.Components.Add(e);
            await ctx.SaveChangesAsync();
            await m.Send(new UpdateComponentCommand(e.Id, "Linh kiện mới", null, null, null, null, null, null, null, null, null, null, null, null, Actor));
            await AssertChangeAsync(ctx, ItemType.Component, e.Id, "name", "Linh kiện gốc", "Linh kiện mới");
        }),

        new("UpdateConsumableCommand", async (ctx, m) =>
        {
            var e = new Consumable { Name = "Vật tư gốc", Qty = 10, MinAmt = 1, Status = ConsumableStatus.Pending };
            ctx.Consumables.Add(e);
            await ctx.SaveChangesAsync();
            await m.Send(new UpdateConsumableCommand(e.Id, "Vật tư mới", null, null, null, null, null, null, null, null, null, null, null, null, null, null, Actor));
            await AssertChangeAsync(ctx, ItemType.Consumable, e.Id, "name", "Vật tư gốc", "Vật tư mới");
        }),

        new("UpdateLicenseCommand", async (ctx, m) =>
        {
            var e = new License { Name = "License gốc", Seats = 2 };
            ctx.Licenses.Add(e);
            await ctx.SaveChangesAsync();
            await m.Send(new UpdateLicenseCommand(e.Id, "License mới", null, null, null, null, null, null, null, null, null, null, null, null, null, null, Actor));
            await AssertChangeAsync(ctx, ItemType.License, e.Id, "name", "License gốc", "License mới");
        }),

        new("UpdateUserCommand", async (ctx, m) =>
        {
            var e = new User
            {
                Username = "ratchet-user", Email = "ratchet@test.local", FirstName = "R", LastName = "U",
                JobTitle = "Chức danh gốc", IsActive = true
            };
            ctx.Users.Add(e);
            await ctx.SaveChangesAsync();
            await m.Send(new UpdateUserCommand
            {
                Id = e.Id, JobTitle = "Chức danh mới", CurrentUserId = Actor, ActorIsRealmSuperUser = true
            });
            await AssertChangeAsync(ctx, ItemType.User, e.Id, "job_title", "Chức danh gốc", "Chức danh mới");
        }),

        new("UpdateUserGroupsCommand", async (ctx, m) =>
        {
            var g1 = new PermissionGroup { Name = "Nhóm A" };
            var g2 = new PermissionGroup { Name = "Nhóm B" };
            ctx.PermissionGroups.AddRange(g1, g2);
            var u = new User { Username = "ratchet-ug", Email = "ug@test.local", FirstName = "U", LastName = "G", IsActive = true };
            ctx.Users.Add(u);
            await ctx.SaveChangesAsync();
            ctx.UserGroups.Add(new UserGroup { UserId = u.Id, GroupId = g1.Id });
            await ctx.SaveChangesAsync();

            await m.Send(new UpdateUserGroupsCommand(u.Id, new[] { g2.Id }, Actor, true));
            await AssertChangeDiffersAsync(ctx, ItemType.User, u.Id, "group_ids");
        }),

        new("UpdateChecklistItemCommand", async (ctx, m) =>
        {
            var (template, version) = await SeedTemplateAsync(ctx);
            var item = new MaintenanceChecklistItem { TemplateVersionId = version.Id, Order = 1, Name = "Hạng mục gốc", CycleMonths = 12 };
            ctx.MaintenanceChecklistItems.Add(item);
            await ctx.SaveChangesAsync();

            await m.Send(new UpdateChecklistItemCommand(template.Id, version.Id, item.Id, null, "Hạng mục mới", null, null, null, null, Actor));
            await AssertChangeAsync(ctx, ItemType.MaintenanceChecklistTemplate, template.Id, "name", "Hạng mục gốc", "Hạng mục mới");
        }),

        new("UpdateStandardParamCommand", async (ctx, m) =>
        {
            var (template, version) = await SeedTemplateAsync(ctx);
            var item = new MaintenanceChecklistItem { TemplateVersionId = version.Id, Order = 1, Name = "Hạng mục", CycleMonths = 12 };
            ctx.MaintenanceChecklistItems.Add(item);
            await ctx.SaveChangesAsync();
            var param = new MaintenanceStandardParam
            {
                ChecklistItemId = item.Id, ParamName = "Thông số gốc",
                ThresholdOperator = MaintenanceThresholdOperator.LessOrEqual, ThresholdValue = 60m
            };
            ctx.MaintenanceStandardParams.Add(param);
            await ctx.SaveChangesAsync();

            await m.Send(new UpdateStandardParamCommand(template.Id, version.Id, item.Id, param.Id,
                "Thông số mới", null, null, null, null, null, Actor));
            await AssertChangeAsync(ctx, ItemType.MaintenanceChecklistTemplate, template.Id, "param_name", "Thông số gốc", "Thông số mới");
        }),

        new("UpdateMaintenanceTemplateCommand", async (ctx, m) =>
        {
            var (template, _) = await SeedTemplateAsync(ctx);
            await m.Send(new UpdateMaintenanceTemplateCommand(template.Id, "Template mới", null, null, null, Actor));
            await AssertChangeAsync(ctx, ItemType.MaintenanceChecklistTemplate, template.Id, "name", "Template gốc", "Template mới");
        }),

        new("UpdateTemplateVersionCommand", async (ctx, m) =>
        {
            var (template, version) = await SeedTemplateAsync(ctx);
            await m.Send(new UpdateTemplateVersionCommand(template.Id, version.Id, new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc), Actor));
            await AssertChangeDiffersAsync(ctx, ItemType.MaintenanceChecklistTemplate, version.Id, "effective_from");
        }),

        new("UpdateMaintenanceCommand", async (ctx, m) =>
        {
            var maintenance = await SeedMaintenanceAsync(ctx);
            await m.Send(new UpdateMaintenanceCommand(maintenance.Id, "Bảo trì mới", null, null, null, null, null, null, null, null, Actor));
            await AssertChangeAsync(ctx, ItemType.AssetMaintenance, maintenance.Id, "title", "Bảo trì gốc", "Bảo trì mới");
        }),

        new("UpdateComponentUnitStatusCommand", async (ctx, m) =>
        {
            var component = new Component { Name = "Linh kiện serial", Qty = 1 };
            ctx.Components.Add(component);
            await ctx.SaveChangesAsync();
            var unit = new ComponentUnit { ComponentId = component.Id, SerialNo = "RATCHET-SN-1", Status = ComponentUnitStatus.InStock };
            ctx.ComponentUnits.Add(unit);
            await ctx.SaveChangesAsync();

            await m.Send(new UpdateComponentUnitStatusCommand(unit.Id, ComponentUnitStatus.Damaged, "hỏng", Actor));
            await AssertChangeAsync(ctx, ItemType.ComponentUnit, unit.Id, "status", "InStock", "Damaged");
        }),

        new("CloseMaintenanceCommand", async (ctx, m) =>
        {
            var maintenance = await SeedMaintenanceAsync(ctx, inspectedBy: Guid.NewGuid());
            await m.Send(new CloseMaintenanceCommand(maintenance.Id, Actor));
            await AssertChangeAsync(ctx, ItemType.AssetMaintenance, maintenance.Id, "status", "Open", "Closed");
        }),

        new("InspectMaintenanceCommand", async (ctx, m) =>
        {
            var maintenance = await SeedMaintenanceAsync(ctx);
            await m.Send(new InspectMaintenanceCommand(maintenance.Id, Actor));
            await AssertChangeAsync(ctx, ItemType.AssetMaintenance, maintenance.Id, "inspected_by_id", null, Actor.ToString());
        }),

        new("ReopenMaintenanceCommand", async (ctx, m) =>
        {
            var maintenance = await SeedMaintenanceAsync(ctx, closed: true, inspectedBy: Actor);
            await m.Send(new ReopenMaintenanceCommand(maintenance.Id, Actor));
            await AssertChangeAsync(ctx, ItemType.AssetMaintenance, maintenance.Id, "status", "Closed", "Open");
        }),

        new("UpsertCampaignResultCommand", async (ctx, m) =>
        {
            var (template, version) = await SeedTemplateAsync(ctx, published: true);
            var item = new MaintenanceChecklistItem { TemplateVersionId = version.Id, Order = 1, Name = "Hạng mục", CycleMonths = 12 };
            ctx.MaintenanceChecklistItems.Add(item);
            await ctx.SaveChangesAsync();

            var now = DateTime.UtcNow;
            var campaign = new MaintenanceCampaign
            {
                SystemInfoId = template.SystemInfoId, TemplateVersionId = version.Id,
                StartDate = now, Status = MaintenanceCampaignStatus.InProgress, CreatedAt = now, UpdatedAt = now
            };
            campaign.DeviceSnapshots.Add(new MaintenanceCampaignDeviceSnapshot
            {
                AssetId = Guid.NewGuid(), AssetTag = "RATCHET-SNAP", AssetName = "snap", CreatedAt = now, UpdatedAt = now
            });
            ctx.MaintenanceCampaigns.Add(campaign);
            await ctx.SaveChangesAsync();
            var snapshot = campaign.DeviceSnapshots.First();

            ctx.MaintenanceChecklistResults.Add(new MaintenanceChecklistResult
            {
                CampaignId = campaign.Id, DeviceSnapshotId = snapshot.Id, ChecklistItemId = item.Id,
                MeasuredValue = "40", IsPass = true, CreatedAt = now, UpdatedAt = now
            });
            await ctx.SaveChangesAsync();

            await m.Send(new UpsertCampaignResultCommand(campaign.Id, snapshot.Id, item.Id, "55", null, null, null, Actor));
            await AssertChangeAsync(ctx, ItemType.MaintenanceCampaign, campaign.Id, "measured_value", "40", "55");
        }),

        new("UpdateSystemSparePartCommand", async (ctx, m) =>
        {
            var company = new Company { Name = "Spare ratchet", Code = "SPR-" + Guid.NewGuid().ToString("N")[..8] };
            var system = new SystemInfo { Code = "SYS-" + Guid.NewGuid().ToString("N")[..8], Name = "Spare system", CompanyId = company.Id };
            var component = new Component { Name = "Spare part", CompanyId = company.Id };
            ctx.AddRange(company, system, component);
            await ctx.SaveChangesAsync();
            var req = new SystemSparePartRequirement
            {
                CompanyId = company.Id, SystemInfoId = system.Id, ComponentId = component.Id, MinQty = 1,
            };
            ctx.SystemSparePartRequirements.Add(req);
            await ctx.SaveChangesAsync();

            await m.Send(new UpdateSystemSparePartCommand(system.Id, req.Id, 5, null, null, Actor));
            await AssertChangeAsync(ctx, ItemType.SystemInfo, req.Id, "min_qty", "1", "5");
        }),

        new("SetAssetTagFormatCommand", async (ctx, m) =>
        {
            var setting = new SystemSetting
            {
                Key = AssetTagFormat.SettingKey,
                Value = AssetTagFormat.DefaultFormat,
                Description = AssetTagFormat.Description
            };
            ctx.SystemSettings.Add(setting);
            await ctx.SaveChangesAsync();

            await m.Send(new SetAssetTagFormatCommand("TAG-{SEQ:4}", Actor));
            // ItemId của log = Id dòng SystemSetting (xem BuildLogEntry của command).
            await AssertChangeAsync(ctx, ItemType.SystemSetting, setting.Id, "format", AssetTagFormat.DefaultFormat, "TAG-{SEQ:4}");
        }),

        new("SetPasskeysEnabledCommand", async (ctx, m) =>
        {
            await m.Send(new SetPasskeysEnabledCommand(true, Actor));
            var setting = await ctx.SystemSettings.AsNoTracking().SingleAsync(s => s.Key == IWebAuthnService.PasskeysEnabledSettingKey);
            await AssertChangeAsync(ctx, ItemType.SystemSetting, setting.Id, "enabled", "false", "true");
        }),
    };
}
