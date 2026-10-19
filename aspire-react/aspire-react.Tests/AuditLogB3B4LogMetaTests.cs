using System.Text.Json;
using aspire_react.Server.Application.Consumables.Commands;
using aspire_react.Server.Application.Manufacturers.Commands;
using aspire_react.Server.Application.Suppliers.Commands;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Infrastructure.Persistence;
using aspire_react.Server.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace aspire_react.Tests;

/// <summary>
/// [AUDIT-LOG B3/B4] `LogMeta.changes` cho 4 chỗ từng SAI:
/// (1) UpdateConsumableCommand — cả 2 nhánh (Confirmed/Unconfirmed) thiếu wrapper `changes`, chỉ ghi
///     3/15 field, và `minAmt` chụp old SAU khi gán ⇒ old == new (BUG-P);
/// (2) ComponentAllocationService.SetUnitStatusAsync — schema cũ {before, after, serialNo};
/// (3)+(4) UpdateManufacturerCommand / UpdateSupplierCommand — `before` chụp SAU khi Code/Name đã gán
///     ⇒ LogMeta ghi code/name old == new (BUG-P).
/// Mọi test dưới đây khẳng định "old" là giá trị CŨ THẬT.
/// </summary>
public class AuditLogB3B4LogMetaTests
{
    private static JsonElement Changes(string? logMeta)
    {
        Assert.False(string.IsNullOrWhiteSpace(logMeta), "LogMeta phải có changes");
        using var doc = JsonDocument.Parse(logMeta!);
        return doc.RootElement.GetProperty("changes").Clone();
    }

    // ==================== B3.1 UpdateConsumableCommand ====================

    [Fact]
    public async Task UpdateConsumable_Unconfirmed_LogsFullPatch_AndMinAmtOldIsReal()
    {
        await using var ctx = TestHelpers.CreateContext(nameof(UpdateConsumable_Unconfirmed_LogsFullPatch_AndMinAmtOldIsReal));
        var actor = Guid.NewGuid();
        var c = new Consumable { Name = "Giấy A4", ItemNo = "IT-01", Qty = 10, MinAmt = 3, Status = ConsumableStatus.Pending };
        ctx.Consumables.Add(c);
        await ctx.SaveChangesAsync();

        var result = await new UpdateConsumableCommandHandler(ctx, new TestHelpers.SuperUserScope(), TestHelpers.CreateActionLogService(ctx, actor))
            .Handle(new UpdateConsumableCommand(
                Id: c.Id, Name: "Giấy A3", ItemNo: null, Qty: 12, MinAmt: 7, CategoryId: null, ManufacturerId: null,
                SupplierId: null, LocationId: null, CompanyId: null, ModelNumber: null, OrderNumber: null,
                PurchaseCost: null, PurchaseDate: null, Notes: null, Image: null, CurrentUserId: actor), default);

        Assert.True(result.Success, result.Message);
        var log = await ctx.ActionLogs.SingleAsync(l => l.ItemType == ItemType.Consumable && l.ActionType == ActionType.Update);
        var changes = Changes(log.LogMeta);

        Assert.Equal("Giấy A4", changes.GetProperty("name").GetProperty("old").GetString());
        Assert.Equal("Giấy A3", changes.GetProperty("name").GetProperty("new").GetString());
        Assert.Equal(10, changes.GetProperty("qty").GetProperty("old").GetInt32());
        Assert.Equal(12, changes.GetProperty("qty").GetProperty("new").GetInt32());

        // [BUG-P] min_amt: old phải là 3 (giá trị cũ thật), KHÔNG được là 7.
        Assert.Equal(3, changes.GetProperty("min_amt").GetProperty("old").GetInt32());
        Assert.Equal(7, changes.GetProperty("min_amt").GetProperty("new").GetInt32());

        // Field không đổi / không gửi ⇒ không xuất hiện.
        Assert.False(changes.TryGetProperty("item_no", out _));
        Assert.False(changes.TryGetProperty("notes", out _));
    }

    [Fact]
    public async Task UpdateConsumable_Unconfirmed_NoFieldChanged_LogsNoChanges()
    {
        await using var ctx = TestHelpers.CreateContext(nameof(UpdateConsumable_Unconfirmed_NoFieldChanged_LogsNoChanges));
        var actor = Guid.NewGuid();
        var c = new Consumable { Name = "Giấy A4", Qty = 10, MinAmt = 3 };
        ctx.Consumables.Add(c);
        await ctx.SaveChangesAsync();

        var result = await new UpdateConsumableCommandHandler(ctx, new TestHelpers.SuperUserScope(), TestHelpers.CreateActionLogService(ctx, actor))
            .Handle(new UpdateConsumableCommand(
                Id: c.Id, Name: "Giấy A4", ItemNo: null, Qty: 10, MinAmt: 3, CategoryId: null, ManufacturerId: null,
                SupplierId: null, LocationId: null, CompanyId: null, ModelNumber: null, OrderNumber: null,
                PurchaseCost: null, PurchaseDate: null, Notes: null, Image: null, CurrentUserId: actor), default);

        Assert.True(result.Success, result.Message);
        var log = await ctx.ActionLogs.SingleAsync(l => l.ItemType == ItemType.Consumable && l.ActionType == ActionType.Update);
        Assert.Null(log.LogMeta);
    }

    [Fact]
    public async Task UpdateConsumable_Confirmed_LogsOnlyLocationAndNotes_WithChangesWrapper()
    {
        await using var ctx = TestHelpers.CreateContext(nameof(UpdateConsumable_Confirmed_LogsOnlyLocationAndNotes_WithChangesWrapper));
        var actor = Guid.NewGuid();
        var oldLocation = Guid.NewGuid();
        var newLocation = Guid.NewGuid();
        var c = new Consumable { Name = "Giấy A4", Qty = 10, MinAmt = 3, Status = ConsumableStatus.Confirmed, LocationId = oldLocation, Notes = "cũ" };
        ctx.Consumables.Add(c);
        await ctx.SaveChangesAsync();

        var result = await new UpdateConsumableCommandHandler(ctx, new TestHelpers.SuperUserScope(), TestHelpers.CreateActionLogService(ctx, actor))
            .Handle(new UpdateConsumableCommand(
                Id: c.Id, Name: null, ItemNo: null, Qty: null, MinAmt: null, CategoryId: null, ManufacturerId: null,
                SupplierId: null, LocationId: newLocation, CompanyId: null, ModelNumber: null, OrderNumber: null,
                PurchaseCost: null, PurchaseDate: null, Notes: "mới", Image: null, CurrentUserId: actor), default);

        Assert.True(result.Success, result.Message);
        var log = await ctx.ActionLogs.SingleAsync(l => l.ItemType == ItemType.Consumable && l.ActionType == ActionType.Update);
        var changes = Changes(log.LogMeta);

        Assert.Equal(oldLocation, changes.GetProperty("location_id").GetProperty("old").GetGuid());
        Assert.Equal(newLocation, changes.GetProperty("location_id").GetProperty("new").GetGuid());
        Assert.Equal("cũ", changes.GetProperty("notes").GetProperty("old").GetString());
        Assert.Equal("mới", changes.GetProperty("notes").GetProperty("new").GetString());
        Assert.Equal(2, changes.EnumerateObject().Count());
    }

    [Fact]
    public async Task UpdateConsumable_Confirmed_SameValues_LogsNoChanges()
    {
        await using var ctx = TestHelpers.CreateContext(nameof(UpdateConsumable_Confirmed_SameValues_LogsNoChanges));
        var actor = Guid.NewGuid();
        var location = Guid.NewGuid();
        var c = new Consumable { Name = "Giấy A4", Status = ConsumableStatus.Confirmed, LocationId = location, Notes = "cũ" };
        ctx.Consumables.Add(c);
        await ctx.SaveChangesAsync();

        var result = await new UpdateConsumableCommandHandler(ctx, new TestHelpers.SuperUserScope(), TestHelpers.CreateActionLogService(ctx, actor))
            .Handle(new UpdateConsumableCommand(
                Id: c.Id, Name: null, ItemNo: null, Qty: null, MinAmt: null, CategoryId: null, ManufacturerId: null,
                SupplierId: null, LocationId: location, CompanyId: null, ModelNumber: null, OrderNumber: null,
                PurchaseCost: null, PurchaseDate: null, Notes: "cũ", Image: null, CurrentUserId: actor), default);

        Assert.True(result.Success, result.Message);
        var log = await ctx.ActionLogs.SingleAsync(l => l.ItemType == ItemType.Consumable && l.ActionType == ActionType.Update);
        Assert.Null(log.LogMeta);
    }

    // ==================== B3.2 ComponentAllocationService.SetUnitStatusAsync ====================

    [Fact]
    public async Task SetUnitStatus_LogsChangesWrapper_WithRealOldStatus()
    {
        await using var ctx = TestHelpers.CreateContext(nameof(SetUnitStatus_LogsChangesWrapper_WithRealOldStatus));
        var actor = Guid.NewGuid();
        var component = new Component { Name = "RAM DDR5", Qty = 1 };
        ctx.Components.Add(component);
        var unit = new ComponentUnit { ComponentId = component.Id, SerialNo = "SN-QA-UNIT-1", Status = ComponentUnitStatus.InStock };
        ctx.ComponentUnits.Add(unit);
        await ctx.SaveChangesAsync();

        var service = new ComponentAllocationService(ctx, new TestHelpers.SuperUserScope(), TestHelpers.CreateActionLogService(ctx, actor));
        var result = await service.SetUnitStatusAsync(unit.Id, ComponentUnitStatus.Damaged, "hỏng do nước", actor);

        Assert.True(result.Success, result.Message);
        var log = await ctx.ActionLogs.SingleAsync(l => l.ItemType == ItemType.ComponentUnit && l.ActionType == ActionType.MarkDamaged);
        var changes = Changes(log.LogMeta);
        Assert.Equal("InStock", changes.GetProperty("status").GetProperty("old").GetString());
        Assert.Equal("Damaged", changes.GetProperty("status").GetProperty("new").GetString());
        Assert.Single(changes.EnumerateObject());
    }

    // ==================== B4 UpdateManufacturerCommand / UpdateSupplierCommand ====================

    [Fact]
    public async Task UpdateManufacturer_CodeAndName_LogRealOldValues()
    {
        await using var ctx = TestHelpers.CreateContext(nameof(UpdateManufacturer_CodeAndName_LogRealOldValues));
        var actor = Guid.NewGuid();
        var m = new Manufacturer { Code = "OLDM", Name = "Nhà cũ", Url = "https://old.example" };
        ctx.Manufacturers.Add(m);
        await ctx.SaveChangesAsync();

        var result = await new UpdateManufacturerCommandHandler(ctx)
            .Handle(new UpdateManufacturerCommand(m.Id, Code: "NEWM", Name: "Nhà mới", Url: "https://new.example",
                SupportUrl: null, SupportEmail: null, CurrentUserId: actor), default);

        Assert.True(result.Success, result.Message);
        var changes = Changes(result.LogMeta);
        Assert.Equal("OLDM", changes.GetProperty("code").GetProperty("old").GetString());
        Assert.Equal("NEWM", changes.GetProperty("code").GetProperty("new").GetString());
        Assert.Equal("Nhà cũ", changes.GetProperty("name").GetProperty("old").GetString());
        Assert.Equal("Nhà mới", changes.GetProperty("name").GetProperty("new").GetString());
        Assert.Equal("https://old.example", changes.GetProperty("url").GetProperty("old").GetString());
        Assert.False(changes.TryGetProperty("support_url", out _));
        Assert.False(changes.TryGetProperty("support_email", out _));
    }

    [Fact]
    public async Task UpdateManufacturer_NoChange_LogsNoChanges()
    {
        await using var ctx = TestHelpers.CreateContext(nameof(UpdateManufacturer_NoChange_LogsNoChanges));
        var actor = Guid.NewGuid();
        var m = new Manufacturer { Code = "OLDM", Name = "Nhà cũ" };
        ctx.Manufacturers.Add(m);
        await ctx.SaveChangesAsync();

        var result = await new UpdateManufacturerCommandHandler(ctx)
            .Handle(new UpdateManufacturerCommand(m.Id, Code: "OLDM", Name: "Nhà cũ", Url: null,
                SupportUrl: null, SupportEmail: null, CurrentUserId: actor), default);

        Assert.True(result.Success, result.Message);
        Assert.Null(result.LogMeta);
    }

    [Fact]
    public async Task UpdateSupplier_CodeAndName_LogRealOldValues()
    {
        await using var ctx = TestHelpers.CreateContext(nameof(UpdateSupplier_CodeAndName_LogRealOldValues));
        var actor = Guid.NewGuid();
        var s = new Supplier { Code = "OLDS", Name = "NCC cũ", City = "Hà Nội" };
        ctx.Suppliers.Add(s);
        await ctx.SaveChangesAsync();

        var result = await new UpdateSupplierCommandHandler(ctx)
            .Handle(new UpdateSupplierCommand(s.Id, Code: "NEWS", Name: "NCC mới",
                Url: null, Address: null, City: "TP.HCM", State: null, Country: null, Zip: null,
                Phone: null, Fax: null, ContactName: null, ContactEmail: null, CurrentUserId: actor), default);

        Assert.True(result.Success, result.Message);
        var changes = Changes(result.LogMeta);
        Assert.Equal("OLDS", changes.GetProperty("code").GetProperty("old").GetString());
        Assert.Equal("NEWS", changes.GetProperty("code").GetProperty("new").GetString());
        Assert.Equal("NCC cũ", changes.GetProperty("name").GetProperty("old").GetString());
        Assert.Equal("NCC mới", changes.GetProperty("name").GetProperty("new").GetString());
        Assert.Equal("Hà Nội", changes.GetProperty("city").GetProperty("old").GetString());
        Assert.Equal("TP.HCM", changes.GetProperty("city").GetProperty("new").GetString());
        Assert.False(changes.TryGetProperty("address", out _));
    }

    [Fact]
    public async Task UpdateSupplier_NoChange_LogsNoChanges()
    {
        await using var ctx = TestHelpers.CreateContext(nameof(UpdateSupplier_NoChange_LogsNoChanges));
        var actor = Guid.NewGuid();
        var s = new Supplier { Code = "OLDS", Name = "NCC cũ" };
        ctx.Suppliers.Add(s);
        await ctx.SaveChangesAsync();

        var result = await new UpdateSupplierCommandHandler(ctx)
            .Handle(new UpdateSupplierCommand(s.Id, Code: "OLDS", Name: "NCC cũ",
                Url: null, Address: null, City: null, State: null, Country: null, Zip: null,
                Phone: null, Fax: null, ContactName: null, ContactEmail: null, CurrentUserId: actor), default);

        Assert.True(result.Success, result.Message);
        Assert.Null(result.LogMeta);
    }
}
