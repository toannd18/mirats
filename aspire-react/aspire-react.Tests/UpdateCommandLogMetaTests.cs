using System.Text.Json;
using aspire_react.Server.Application.AssetMaintenances.Commands;
using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Application.Components.Commands;
using aspire_react.Server.Application.Licenses.Commands;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using aspire_react.Server.Infrastructure.Persistence;
using aspire_react.Server.Web.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace aspire_react.Tests;

/// <summary>
/// [AUDIT-LOG B2] 3 command Update từng thiếu `LogMeta.changes` (UpdateComponent / UpdateLicense /
/// UpdateMaintenance — BUG-S) giờ phải ghi đúng old→new: old là giá trị CŨ THẬT (chụp trước khi gán),
/// new là giá trị mới; không đổi gì ⇒ LogMeta null. `serial` của license là trường NHẠY CẢM nên
/// LogMeta chỉ báo "đã đổi", không chứa giá trị.
/// </summary>
public class UpdateCommandLogMetaTests
{
    private sealed class FakeCurrentUser : ICurrentUserService
    {
        public FakeCurrentUser(Guid userId) => UserId = userId;
        public Guid UserId { get; }
        public Guid GetLocalUserId() => UserId;
    }

    private static AppDbContext CreateContext(string name)
        => TestHelpers.CreateContext(name);

    private static JsonElement Changes(string? logMeta)
    {
        Assert.False(string.IsNullOrWhiteSpace(logMeta), "LogMeta phải có changes");
        using var doc = JsonDocument.Parse(logMeta!);
        return doc.RootElement.GetProperty("changes").Clone();
    }

    // ==================== UpdateComponentCommand ====================

    [Fact]
    public async Task UpdateComponent_ChangeOneField_LogsTrueOldAndNew()
    {
        await using var ctx = CreateContext(nameof(UpdateComponent_ChangeOneField_LogsTrueOldAndNew));
        var actor = Guid.NewGuid();
        var component = new Component { Name = "CPU cũ", MinAmt = 2, Qty = 5 };
        ctx.Components.Add(component);
        await ctx.SaveChangesAsync();

        var result = await new UpdateComponentCommandHandler(ctx, new TestHelpers.SuperUserScope(), TestHelpers.CreateActionLogService(ctx, actor))
            .Handle(new UpdateComponentCommand(
                Id: component.Id, Name: "CPU mới", Notes: null, SupplierId: null, ManufacturerId: null,
                ModelNumber: null, MinAmt: null, LocationId: null, OrderNumber: null, PurchaseCost: null,
                PurchaseDate: null, CategoryId: null, CompanyId: null, TrackingType: null,
                CurrentUserId: actor), default);

        Assert.True(result.Success, result.Message);
        var log = await ctx.ActionLogs.SingleAsync(l => l.ItemType == ItemType.Component && l.ActionType == ActionType.Update);
        var changes = Changes(log.LogMeta);
        Assert.Equal("CPU cũ", changes.GetProperty("name").GetProperty("old").GetString());
        Assert.Equal("CPU mới", changes.GetProperty("name").GetProperty("new").GetString());
        // Chỉ field thực đổi mới xuất hiện.
        Assert.False(changes.TryGetProperty("min_amt", out _));
        Assert.False(changes.TryGetProperty("notes", out _));
    }

    [Fact]
    public async Task UpdateComponent_NoFieldChanged_LogsNoChanges()
    {
        await using var ctx = CreateContext(nameof(UpdateComponent_NoFieldChanged_LogsNoChanges));
        var actor = Guid.NewGuid();
        var component = new Component { Name = "CPU cũ", MinAmt = 2, Qty = 5 };
        ctx.Components.Add(component);
        await ctx.SaveChangesAsync();

        var result = await new UpdateComponentCommandHandler(ctx, new TestHelpers.SuperUserScope(), TestHelpers.CreateActionLogService(ctx, actor))
            .Handle(new UpdateComponentCommand(
                Id: component.Id, Name: "CPU cũ", Notes: null, SupplierId: null, ManufacturerId: null,
                ModelNumber: null, MinAmt: 2, LocationId: null, OrderNumber: null, PurchaseCost: null,
                PurchaseDate: null, CategoryId: null, CompanyId: null, TrackingType: null,
                CurrentUserId: actor), default);

        Assert.True(result.Success, result.Message);
        var log = await ctx.ActionLogs.SingleAsync(l => l.ItemType == ItemType.Component && l.ActionType == ActionType.Update);
        Assert.Null(log.LogMeta);
    }

    // ==================== UpdateLicenseCommand ====================

    [Fact]
    public async Task UpdateLicense_ChangeNameAndSerial_LogsNameOldNew_AndHidesSerialValues()
    {
        await using var ctx = CreateContext(nameof(UpdateLicense_ChangeNameAndSerial_LogsNameOldNew_AndHidesSerialValues));
        var actor = Guid.NewGuid();
        var license = new License { Name = "Adobe", Serial = "SN-OLD-SECRET", Seats = 2, Reassignable = true };
        ctx.Licenses.Add(license);
        await ctx.SaveChangesAsync();

        var result = await new UpdateLicenseCommandHandler(ctx, new TestHelpers.SuperUserScope(), TestHelpers.CreateActionLogService(ctx, actor))
            .Handle(new UpdateLicenseCommand(
                Id: license.Id, Name: "Adobe CC", Serial: "SN-NEW-SECRET", Seats: null, Reassignable: null,
                ExpirationDate: null, TerminationDate: null, PurchaseCost: null, PurchaseDate: null,
                OrderNumber: null, MinSeats: null, Notes: null, SupplierId: null, ManufacturerId: null,
                CategoryId: null, CompanyId: null, CurrentUserId: actor), default);

        Assert.True(result.Success, result.Message);
        var log = await ctx.ActionLogs.SingleAsync(l => l.ItemType == ItemType.License && l.ActionType == ActionType.Update);
        var changes = Changes(log.LogMeta);
        Assert.Equal("Adobe", changes.GetProperty("name").GetProperty("old").GetString());
        Assert.Equal("Adobe CC", changes.GetProperty("name").GetProperty("new").GetString());

        // Serial: chỉ báo đã đổi, KHÔNG chứa giá trị cũ/mới.
        var serial = changes.GetProperty("serial");
        Assert.True(serial.GetProperty("changed").GetBoolean());
        Assert.False(serial.TryGetProperty("old", out _));
        Assert.False(serial.TryGetProperty("new", out _));
        Assert.DoesNotContain("SN-OLD-SECRET", log.LogMeta);
        Assert.DoesNotContain("SN-NEW-SECRET", log.LogMeta);
    }

    [Fact]
    public async Task UpdateLicense_ChangeSeats_LogsTrueOldAndNew()
    {
        await using var ctx = CreateContext(nameof(UpdateLicense_ChangeSeats_LogsTrueOldAndNew));
        var actor = Guid.NewGuid();
        var license = new License { Name = "Adobe", Seats = 2, Reassignable = true };
        ctx.Licenses.Add(license);
        ctx.LicenseSeats.Add(new LicenseSeat { LicenseId = license.Id, SeatNumber = 1 });
        ctx.LicenseSeats.Add(new LicenseSeat { LicenseId = license.Id, SeatNumber = 2 });
        await ctx.SaveChangesAsync();

        var result = await new UpdateLicenseCommandHandler(ctx, new TestHelpers.SuperUserScope(), TestHelpers.CreateActionLogService(ctx, actor))
            .Handle(new UpdateLicenseCommand(
                Id: license.Id, Name: null, Serial: null, Seats: 3, Reassignable: null,
                ExpirationDate: null, TerminationDate: null, PurchaseCost: null, PurchaseDate: null,
                OrderNumber: null, MinSeats: null, Notes: null, SupplierId: null, ManufacturerId: null,
                CategoryId: null, CompanyId: null, CurrentUserId: actor), default);

        Assert.True(result.Success, result.Message);
        var log = await ctx.ActionLogs.SingleAsync(l => l.ItemType == ItemType.License && l.ActionType == ActionType.Update);
        var seats = Changes(log.LogMeta).GetProperty("seats");
        Assert.Equal(2, seats.GetProperty("old").GetInt32());
        Assert.Equal(3, seats.GetProperty("new").GetInt32());
    }

    [Fact]
    public async Task UpdateLicense_NoFieldChanged_LogsNoChanges()
    {
        await using var ctx = CreateContext(nameof(UpdateLicense_NoFieldChanged_LogsNoChanges));
        var actor = Guid.NewGuid();
        var license = new License { Name = "Adobe", Serial = "SN-1", Seats = 2, Reassignable = true };
        ctx.Licenses.Add(license);
        await ctx.SaveChangesAsync();

        var result = await new UpdateLicenseCommandHandler(ctx, new TestHelpers.SuperUserScope(), TestHelpers.CreateActionLogService(ctx, actor))
            .Handle(new UpdateLicenseCommand(
                Id: license.Id, Name: "Adobe", Serial: "SN-1", Seats: 2, Reassignable: true,
                ExpirationDate: null, TerminationDate: null, PurchaseCost: null, PurchaseDate: null,
                OrderNumber: null, MinSeats: null, Notes: null, SupplierId: null, ManufacturerId: null,
                CategoryId: null, CompanyId: null, CurrentUserId: actor), default);

        Assert.True(result.Success, result.Message);
        var log = await ctx.ActionLogs.SingleAsync(l => l.ItemType == ItemType.License && l.ActionType == ActionType.Update);
        Assert.Null(log.LogMeta);
    }

    // ==================== UpdateMaintenanceCommand (BUG-S) ====================

    private static async Task<(Guid maintenanceId, User assignee)> SeedMaintenanceAsync(AppDbContext ctx)
    {
        var asset = new Asset { AssetTag = "AST-AUDIT-1", Name = "Máy chủ 1" };
        var assignee = new User { Username = "qa-audit-1", FirstName = "QA", LastName = "One" };
        ctx.Assets.Add(asset);
        ctx.Users.Add(assignee);
        await ctx.SaveChangesAsync();

        var maintenance = new AssetMaintenance
        {
            AssetId = asset.Id,
            Type = AssetMaintenanceType.Maintenance,
            Title = "Bảo trì A",
            CompanyId = Guid.Empty,
            StartDate = DateTime.SpecifyKind(new DateTime(2026, 9, 1), DateTimeKind.Unspecified),
            IsWarranty = false
        };
        ctx.AssetMaintenances.Add(maintenance);
        await ctx.SaveChangesAsync();
        return (maintenance.Id, assignee);
    }

    private static AssetMaintenancesController CreateController(AppDbContext ctx, Guid actor)
        => new(TestHelpers.BuildMediator(ctx, new TestHelpers.SuperUserScope(), actor), ctx,
            new FakeCurrentUser(actor), new TestHelpers.SuperUserScope(), TestHelpers.CreateActionLogService(ctx, actor));

    [Fact]
    public async Task UpdateMaintenance_ChangeTitleAndAssignees_LogsOldAndNew_IncludingAssigneeList()
    {
        await using var ctx = CreateContext(nameof(UpdateMaintenance_ChangeTitleAndAssignees_LogsOldAndNew_IncludingAssigneeList));
        var actor = Guid.NewGuid();
        var (maintenanceId, assignee) = await SeedMaintenanceAsync(ctx);

        var result = await CreateController(ctx, actor).Update(maintenanceId,
            new UpdateAssetMaintenanceRequest(Title: "Bảo trì B", AssigneeUserIds: new[] { assignee.Id }));

        Assert.IsType<OkObjectResult>(result);
        var log = await ctx.ActionLogs.SingleAsync(l => l.ItemType == ItemType.AssetMaintenance && l.ActionType == ActionType.Update);
        var changes = Changes(log.LogMeta);

        Assert.Equal("Bảo trì A", changes.GetProperty("title").GetProperty("old").GetString());
        Assert.Equal("Bảo trì B", changes.GetProperty("title").GetProperty("new").GetString());

        var assignees = changes.GetProperty("assignees");
        Assert.Equal(0, assignees.GetProperty("old").GetArrayLength());
        Assert.Equal(1, assignees.GetProperty("new").GetArrayLength());
        Assert.Equal(assignee.Id, assignees.GetProperty("new")[0].GetGuid());
    }

    [Fact]
    public async Task UpdateMaintenance_NoFieldChanged_LogsNoChanges()
    {
        await using var ctx = CreateContext(nameof(UpdateMaintenance_NoFieldChanged_LogsNoChanges));
        var actor = Guid.NewGuid();
        var (maintenanceId, assignee) = await SeedMaintenanceAsync(ctx);
        var controller = CreateController(ctx, actor);

        // Lần 1: đặt title + assignee.
        Assert.IsType<OkObjectResult>(await controller.Update(maintenanceId,
            new UpdateAssetMaintenanceRequest(Title: "Bảo trì B", AssigneeUserIds: new[] { assignee.Id })));
        var first = await ctx.ActionLogs.SingleAsync(l => l.ItemType == ItemType.AssetMaintenance && l.ActionType == ActionType.Update);

        // Lần 2: gửi lại đúng giá trị đang có ⇒ không có changes.
        Assert.IsType<OkObjectResult>(await controller.Update(maintenanceId,
            new UpdateAssetMaintenanceRequest(Title: "Bảo trì B", AssigneeUserIds: new[] { assignee.Id })));
        var second = await ctx.ActionLogs.SingleAsync(l =>
            l.ItemType == ItemType.AssetMaintenance && l.ActionType == ActionType.Update && l.Id != first.Id);

        Assert.Null(second.LogMeta);
    }
}
