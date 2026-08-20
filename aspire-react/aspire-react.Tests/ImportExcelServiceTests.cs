using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Infrastructure.Persistence;
using aspire_react.Server.Infrastructure.Services;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace aspire_react.Tests;

/// <summary>
/// Task IMPORT-T6 — Excel import business-rule tests using REAL ClosedXML workbooks in-memory
/// (mirrors the sheet structure the sample workbook uses: sheet names + Vietnamese headers):
///  - Best-effort per-row: one bad row does NOT block other rows (partial success semantics).
///  - Serial component grouping by (Name + Category + ModelNumber) into ONE serial component.
///  - AssetModel is NEVER auto-created (approved decision) — a missing model errors the row only.
///  - Every created record gets its own ActionLog (ActionType.Import) with the target company.
/// </summary>
public class ImportExcelServiceTests
{
    // ─────────────────────────── workbook builder ───────────────────────────

    private static MemoryStream AssetsWorkbook(params (string Tag, string Name, string? Model, string? Category)[] rows)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("4_TaiSan");
        string[] h = ["Ma tai san", "Ten tai san", "Danh muc", "Serial", "Model", "Nha san xuat", "Dia diem", "Trang thai", "Ghi chu"];
        for (int c = 0; c < h.Length; c++) ws.Cell(1, c + 1).Value = h[c];
        for (int i = 0; i < rows.Length; i++)
        {
            ws.Cell(i + 2, 1).Value = rows[i].Tag;
            ws.Cell(i + 2, 2).Value = rows[i].Name;
            if (rows[i].Category != null) ws.Cell(i + 2, 3).Value = rows[i].Category;
            if (rows[i].Model != null) ws.Cell(i + 2, 5).Value = rows[i].Model;
        }
        var ms = new MemoryStream();
        wb.SaveAs(ms);
        ms.Position = 0;
        return ms;
    }

    private static MemoryStream ComponentsWorkbook((string Name, string Category, string Tracking, string Serial, string Model)[] rows)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("5_LinhKien");
        string[] h = ["Ten linh kien", "Danh muc", "Kieu theo doi", "Serial", "Nguong canh bao", "Model", "Nha san xuat", "Dia diem"];
        for (int c = 0; c < h.Length; c++) ws.Cell(1, c + 1).Value = h[c];
        for (int i = 0; i < rows.Length; i++)
        {
            ws.Cell(i + 2, 1).Value = rows[i].Name;
            ws.Cell(i + 2, 2).Value = rows[i].Category;
            ws.Cell(i + 2, 3).Value = rows[i].Tracking;
            if (!string.IsNullOrEmpty(rows[i].Serial)) ws.Cell(i + 2, 4).Value = rows[i].Serial;
            if (!string.IsNullOrEmpty(rows[i].Model)) ws.Cell(i + 2, 6).Value = rows[i].Model;
        }
        var ms = new MemoryStream();
        wb.SaveAs(ms);
        ms.Position = 0;
        return ms;
    }

    private static async Task<(AppDbContext ctx, Guid companyId, Guid actingUserId)> SeedAsync(
        string dbName, string? assetCategory = null, string? componentCategory = null)
    {
        var ctx = TestHelpers.CreateContext(dbName);
        var company = new Company { Name = "CO-A" };
        ctx.Companies.Add(company);
        var user = new User { Username = "imp", Email = "imp@t.local", FirstName = "I", LastName = "I", CompanyId = company.Id };
        ctx.Users.Add(user);

        if (assetCategory != null)
            ctx.Categories.Add(new Category { Name = assetCategory, CategoryType = CategoryType.Asset });
        if (componentCategory != null)
            ctx.Categories.Add(new Category { Name = componentCategory, CategoryType = CategoryType.Component });

        await ctx.SaveChangesAsync();
        return (ctx, company.Id, user.Id);
    }

    private static ExcelImportService BuildService(AppDbContext ctx)
    {
        var actionLog = TestHelpers.CreateActionLogService(ctx);
        var allocation = new ComponentAllocationService(ctx, new TestHelpers.SuperUserScope(), actionLog);
        return new ExcelImportService(ctx, actionLog, allocation, new TestHelpers.NullCacheInvalidator());
    }

    // ─────────────────────────── best-effort per-row ───────────────────────────

    [Fact]
    public async Task AssetsImport_OneBadRow_DoesNotBlockOtherRows()
    {
        var s = await SeedAsync(nameof(AssetsImport_OneBadRow_DoesNotBlockOtherRows), assetCategory: "PC");
        await using var ctx = s.ctx;
        var svc = BuildService(ctx);

        // Row 2 valid (no model) → created. Row 3 references a NON-existent model → error only for that row.
        (string Tag, string Name, string? Model, string? Category)[] rows =
        {
            ("AST-OK", "PC tot", null, "PC"),
            ("AST-BAD", "PC loi", "NO-SUCH-MODEL", "PC"),
        };
        using var ms = AssetsWorkbook(rows);

        var result = await svc.ImportAssetsAsync(ms, s.actingUserId, s.companyId);

        Assert.Equal(1, result.Created);
        Assert.Equal(1, result.Failed);
        Assert.Single(result.Errors);
        Assert.Contains("NO-SUCH-MODEL", result.Errors[0].Message);
        Assert.Contains("không tự tạo model", result.Errors[0].Message);
        // The valid row persisted, the bad row did not.
        Assert.True(await ctx.Assets.AnyAsync(a => a.AssetTag == "AST-OK"));
        Assert.False(await ctx.Assets.AnyAsync(a => a.AssetTag == "AST-BAD"));
    }

    [Fact]
    public async Task AssetsImport_GoodRowsAllCreate_EvenWhenOneFails()
    {
        var s = await SeedAsync(nameof(AssetsImport_GoodRowsAllCreate_EvenWhenOneFails), assetCategory: "PC");
        await using var ctx = s.ctx;
        var svc = BuildService(ctx);

        (string Tag, string Name, string? Model, string? Category)[] rows =
        {
            ("AST-1", "May 1", null, "PC"),
            ("AST-2", "May 2", "MISSING-MODEL", "PC"),
            ("AST-3", "May 3", null, "PC"),
        };
        using var ms = AssetsWorkbook(rows);

        var result = await svc.ImportAssetsAsync(ms, s.actingUserId, s.companyId);

        Assert.Equal(2, result.Created);
        Assert.Equal(1, result.Failed);
        Assert.Equal(2, await ctx.Assets.CountAsync(a => a.CompanyId == s.companyId));
        Assert.False(await ctx.Assets.AnyAsync(a => a.AssetTag == "AST-2"));
    }

    [Fact]
    public async Task AssetsImport_CompanyIdAssignedToEveryCreatedAsset_AndActionLogged()
    {
        var s = await SeedAsync(nameof(AssetsImport_CompanyIdAssignedToEveryCreatedAsset_AndActionLogged), assetCategory: "PC");
        await using var ctx = s.ctx;
        var svc = BuildService(ctx);

        (string Tag, string Name, string? Model, string? Category)[] rows = { ("AST-CO", "May co", null, "PC") };
        using var ms = AssetsWorkbook(rows);
        var result = await svc.ImportAssetsAsync(ms, s.actingUserId, s.companyId);

        Assert.Equal(1, result.Created);
        var asset = await ctx.Assets.SingleAsync(a => a.AssetTag == "AST-CO");
        Assert.Equal(s.companyId, asset.CompanyId);
        // One ActionLog (Import) for the created asset, stamped with the same company.
        var log = await ctx.ActionLogs.SingleAsync(l => l.ItemType == ItemType.Asset && l.ItemId == asset.Id);
        Assert.Equal(ActionType.Import, log.ActionType);
        Assert.Equal(s.companyId, log.CompanyId);
        Assert.Equal(s.actingUserId, log.CreatedBy);
    }

    // ─────────────────────────── AssetModel never auto-created ───────────────────────────

    [Fact]
    public async Task AssetsImport_MissingModel_ErrorsRow_AndDoesNotCreateModel()
    {
        var s = await SeedAsync(nameof(AssetsImport_MissingModel_ErrorsRow_AndDoesNotCreateModel), assetCategory: "PC");
        await using var ctx = s.ctx;
        var svc = BuildService(ctx);

        (string Tag, string Name, string? Model, string? Category)[] rows = { ("AST-M", "May model", "NONEXISTENT-MODEL", "PC") };
        using var ms = AssetsWorkbook(rows);
        var result = await svc.ImportAssetsAsync(ms, s.actingUserId, s.companyId);

        Assert.Equal(0, result.Created);
        Assert.Equal(1, result.Failed);
        Assert.Contains("chưa tồn tại", result.Errors[0].Message);
        Assert.Contains("không tự tạo model", result.Errors[0].Message);
        // No AssetModel was auto-created.
        Assert.Empty(await ctx.Models.ToListAsync());
        Assert.Empty(await ctx.Assets.ToListAsync());
    }

    // ─────────────────────────── serial grouping ───────────────────────────

    [Fact]
    public async Task ComponentsImport_SerialRowsGroupedByNameCategoryModel_IntoOneComponent()
    {
        var s = await SeedAsync(nameof(ComponentsImport_SerialRowsGroupedByNameCategoryModel_IntoOneComponent), componentCategory: "RAM");
        await using var ctx = s.ctx;
        var svc = BuildService(ctx);

        // 3 serial rows sharing (Name, Category, Model) → ONE serial-tracked component + 3 units.
        using var ms = ComponentsWorkbook(new[]
        {
            ("RAM 8GB", "RAM", "Serial", "SN-001", "M8"),
            ("RAM 8GB", "RAM", "Serial", "SN-002", "M8"),
            ("RAM 8GB", "RAM", "Serial", "SN-003", "M8"),
        });

        var result = await svc.ImportComponentsAsync(ms, s.actingUserId, s.companyId);

        Assert.Equal(1, result.Created);
        Assert.Equal(0, result.Failed);
        var comp = await ctx.Components.SingleAsync(c => c.Name == "RAM 8GB");
        Assert.Equal(TrackingType.Serial, comp.TrackingType);
        Assert.Equal(s.companyId, comp.CompanyId);
        Assert.Equal(3, comp.Qty);
        Assert.Equal(3, await ctx.ComponentUnits.CountAsync(u => u.ComponentId == comp.Id));
        // Each serial unit got its own ActionLog (ItemType.ComponentUnit); ItemId = the UNIT id.
        Assert.Equal(3, await ctx.ActionLogs.CountAsync(l => l.ItemType == ItemType.ComponentUnit && l.CompanyId == s.companyId));
    }

    [Fact]
    public async Task ComponentsImport_DifferentNameOrModel_GroupsSeparately()
    {
        var s = await SeedAsync(nameof(ComponentsImport_DifferentNameOrModel_GroupsSeparately), componentCategory: "RAM");
        await using var ctx = s.ctx;
        var svc = BuildService(ctx);

        // Two distinct (Name,Category,Model) keys → two components.
        using var ms = ComponentsWorkbook(new[]
        {
            ("RAM 8GB", "RAM", "Serial", "SN-001", "M8"),
            ("RAM 16GB", "RAM", "Serial", "SN-002", "M16"),
        });

        var result = await svc.ImportComponentsAsync(ms, s.actingUserId, s.companyId);

        Assert.Equal(2, result.Created);
        Assert.Equal(2, await ctx.Components.CountAsync(c => c.CompanyId == s.companyId));
        Assert.Equal(2, await ctx.ComponentUnits.CountAsync());
    }
}
