using ClosedXML.Excel;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using aspire_react.Server.Infrastructure.Caching;
using aspire_react.Server.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Infrastructure.Services;

/// <summary>Row-level outcome of one imported row (best-effort import: a bad row is reported, not fatal).</summary>
public sealed record ImportRowResult(int RowNumber, bool Success, string Message);

/// <summary>Aggregated outcome of one sheet import. <c>Rows</c> = every processed row (for the
/// per-row UI report), <c>Errors</c> = the failed subset.</summary>
public sealed record ImportSheetResult(int Created, int Failed, IReadOnlyList<ImportRowResult> Rows, IReadOnlyList<ImportRowResult> Errors);

/// <summary>
/// Shared Excel (.xlsx) import machinery for the Mirats import feature (T1–T4).
/// Design decisions (approved):
///  - Sheet lookup BY NAME (not position) — mirrors the sample workbook
///    <c>docs/Mirats_DuLieuMau_VatTu_T&amp;E.xlsx</c> (1_DanhMuc…7_VatTuTieuHao).
///  - Header row is auto-detected (the sample has title rows above the header and the
///    header lives on row 3 for sheet 7 but row 4 for the others) and each column is
///    located by its header text — so column order in the file does not matter.
///  - Best-effort per row (no all-or-nothing atomicity).
///  - Reference sheets (T1) auto-create Category/Manufacturer/Location when missing;
///    inventory sheets resolve by name only and report a per-row error if missing
///    (AssetModel is NEVER auto-created — approved decision).
///  - Component serial rows are grouped by (Name + CategoryName + ModelNumber) into ONE
///    serial-tracked component; StockInAsync is reused for per-unit stock + audit logging.
///  - Every created record gets its own ActionLog (ActionType.Import) in the same SaveChanges.
///  - Company scoping (Task IMPORT-T5): ONE import = ONE company. The controller validates the
///    client-supplied target company against the acting user's real scope (never trust the client)
///    and passes it in; every created record AND every ActionLog of the batch gets that CompanyId.
///    Category/Manufacturer stay global entities (no CompanyId column) but their import ActionLogs
///    are stamped with the chosen company so the whole batch is auditable per company.
/// </summary>
public interface IExcelImportService
{
    Task<ImportSheetResult> ImportReferenceAsync(Stream xlsxStream, Guid actingUserId, Guid companyId, CancellationToken ct = default);
    Task<ImportSheetResult> ImportAssetsAsync(Stream xlsxStream, Guid actingUserId, Guid companyId, CancellationToken ct = default);
    Task<ImportSheetResult> ImportComponentsAsync(Stream xlsxStream, Guid actingUserId, Guid companyId, CancellationToken ct = default);
    Task<ImportSheetResult> ImportAccessoriesAsync(Stream xlsxStream, Guid actingUserId, Guid companyId, CancellationToken ct = default);
    Task<ImportSheetResult> ImportConsumablesAsync(Stream xlsxStream, Guid actingUserId, Guid companyId, CancellationToken ct = default);
    Task<ImportSheetResult> ImportSystemsAsync(Stream xlsxStream, Guid actingUserId, Guid companyId, CancellationToken ct = default);
    Task<ImportSheetResult> ImportSystemPositionsAsync(Stream xlsxStream, Guid actingUserId, Guid? actingUserCompanyId, CancellationToken ct = default);
}

public class ExcelImportService : IExcelImportService
{
    private const string SheetCategories = "1_DanhMuc";
    private const string SheetLocations = "2_DiaDiem";
    private const string SheetManufacturers = "3_NhaSanXuat";
    private const string SheetAssets = "4_TaiSan";
    private const string SheetComponents = "5_LinhKien";
    private const string SheetAccessories = "6_PhuKien";
    private const string SheetConsumables = "7_VatTuTieuHao";
    private const string SheetSystems = "1_HeThong";
    private const string SheetSystemPositions = "2_ViTri";

    private readonly AppDbContext _context;
    private readonly IActionLogService _actionLogService;
    private readonly IComponentAllocationService _componentAllocation;
    private readonly ICacheInvalidator _cacheInvalidator;

    public ExcelImportService(
        AppDbContext context,
        IActionLogService actionLogService,
        IComponentAllocationService componentAllocation,
        ICacheInvalidator cacheInvalidator)
    {
        _context = context;
        _actionLogService = actionLogService;
        _componentAllocation = componentAllocation;
        _cacheInvalidator = cacheInvalidator;
    }

    // ─────────────────────────── Public entry points ───────────────────────────

    public async Task<ImportSheetResult> ImportReferenceAsync(Stream xlsxStream, Guid actingUserId, Guid companyId, CancellationToken ct = default)
    {
        using var wb = new XLWorkbook(xlsxStream);
        var results = new List<ImportRowResult>();
        results.AddRange(await ImportCategoriesSheetAsync(wb, actingUserId, companyId, ct));
        results.AddRange(await ImportManufacturersSheetAsync(wb, actingUserId, companyId, ct));
        results.AddRange(await ImportLocationsSheetAsync(wb, actingUserId, companyId, ct));
        return Summarize(results);
    }

    public async Task<ImportSheetResult> ImportAssetsAsync(Stream xlsxStream, Guid actingUserId, Guid companyId, CancellationToken ct = default)
    {
        using var wb = new XLWorkbook(xlsxStream);
        return Summarize(await ImportAssetsSheetAsync(wb, actingUserId, companyId, ct));
    }

    public async Task<ImportSheetResult> ImportComponentsAsync(Stream xlsxStream, Guid actingUserId, Guid companyId, CancellationToken ct = default)
    {
        using var wb = new XLWorkbook(xlsxStream);
        return Summarize(await ImportComponentsSheetAsync(wb, actingUserId, companyId, ct));
    }

    public async Task<ImportSheetResult> ImportAccessoriesAsync(Stream xlsxStream, Guid actingUserId, Guid companyId, CancellationToken ct = default)
    {
        using var wb = new XLWorkbook(xlsxStream);
        return Summarize(await ImportAccessoriesSheetAsync(wb, actingUserId, companyId, ct));
    }

    public async Task<ImportSheetResult> ImportConsumablesAsync(Stream xlsxStream, Guid actingUserId, Guid companyId, CancellationToken ct = default)
    {
        using var wb = new XLWorkbook(xlsxStream);
        return Summarize(await ImportConsumablesSheetAsync(wb, actingUserId, companyId, ct));
    }

    public async Task<ImportSheetResult> ImportSystemsAsync(Stream xlsxStream, Guid actingUserId, Guid companyId, CancellationToken ct = default)
    {
        using var wb = new XLWorkbook(xlsxStream);
        return Summarize(await ImportSystemsSheetAsync(wb, actingUserId, companyId, ct));
    }

    public async Task<ImportSheetResult> ImportSystemPositionsAsync(Stream xlsxStream, Guid actingUserId, Guid? actingUserCompanyId, CancellationToken ct = default)
    {
        using var wb = new XLWorkbook(xlsxStream);
        return Summarize(await ImportSystemPositionsSheetAsync(wb, actingUserId, actingUserCompanyId, ct));
    }

    // ─────────────────────────── T1: reference sheets ───────────────────────────

    private async Task<List<ImportRowResult>> ImportCategoriesSheetAsync(XLWorkbook wb, Guid actingUserId, Guid companyId, CancellationToken ct)
    {
        if (!TryGetSheet(wb, SheetCategories, out var ws, out var err)) return [err];
        int? hr = FindHeaderRow(ws, "Ten danh muc");
        if (hr == null) return [new ImportRowResult(0, false, $"Sheet '{SheetCategories}' thiếu cột 'Ten danh muc'.")];
        int colName = FindColumn(ws, hr.Value, "Ten danh muc");
        int colType = FindColumn(ws, hr.Value, "Loai");
        int colColor = FindColumn(ws, hr.Value, "Ma mau");
        int colNotes = FindColumn(ws, hr.Value, "Ghi chu");

        var results = new List<ImportRowResult>();
        for (int r = hr.Value + 1; r <= ws.LastRowUsed()?.RowNumber(); r++)
        {
            ct.ThrowIfCancellationRequested();
            var name = Cell(ws, r, colName)?.Trim();
            if (string.IsNullOrWhiteSpace(name)) continue;

            var typeRaw = Cell(ws, r, colType)?.Trim();
            if (!Enum.TryParse<CategoryType>(typeRaw, ignoreCase: true, out var categoryType))
            {
                results.Add(new ImportRowResult(r, false, $"Loại danh mục '{typeRaw}' không hợp lệ (Asset/Consumable/Accessory/Component/License)."));
                continue;
            }

            if (await _context.Categories.AnyAsync(x => x.Name == name && x.CategoryType == categoryType, ct))
            {
                results.Add(new ImportRowResult(r, true, $"Danh mục '{name}' ({categoryType}) đã tồn tại — bỏ qua."));
                continue;
            }

            var category = new Category
            {
                Name = name,
                CategoryType = categoryType,
                TagColor = Cell(ws, r, colColor)?.Trim(),
                Notes = Cell(ws, r, colNotes)?.Trim()
            };
            _context.Categories.Add(category);
            _actionLogService.Log(new ActionLogEntry
            {
                ItemType = ItemType.Category, ItemId = category.Id, ActionType = ActionType.Import,
                CreatedBy = actingUserId, CompanyId = companyId,
                Note = $"Import danh mục \"{name}\" (loại: {categoryType})"
            });
            results.Add(new ImportRowResult(r, true, $"Đã import danh mục '{name}'."));
        }

        await _context.SaveChangesAsync(ct);
        await _cacheInvalidator.InvalidateCategoriesAsync(ct);
        return results;
    }

    private async Task<List<ImportRowResult>> ImportManufacturersSheetAsync(XLWorkbook wb, Guid actingUserId, Guid companyId, CancellationToken ct)
    {
        if (!TryGetSheet(wb, SheetManufacturers, out var ws, out var err)) return [err];
        int? hr = FindHeaderRow(ws, "Ten nha san xuat");
        if (hr == null) return [new ImportRowResult(0, false, $"Sheet '{SheetManufacturers}' thiếu cột 'Ten nha san xuat'.")];
        int colName = FindColumn(ws, hr.Value, "Ten nha san xuat");

        var results = new List<ImportRowResult>();
        for (int r = hr.Value + 1; r <= ws.LastRowUsed()?.RowNumber(); r++)
        {
            ct.ThrowIfCancellationRequested();
            var name = Cell(ws, r, colName)?.Trim();
            if (string.IsNullOrWhiteSpace(name)) continue;

            if (await _context.Manufacturers.AnyAsync(x => x.Name == name, ct))
            {
                results.Add(new ImportRowResult(r, true, $"Nhà sản xuất '{name}' đã tồn tại — bỏ qua."));
                continue;
            }

            var code = await GenerateManufacturerCodeAsync(name, ct);
            var manufacturer = new Manufacturer { Name = name, Code = code };
            _context.Manufacturers.Add(manufacturer);
            _actionLogService.Log(new ActionLogEntry
            {
                ItemType = ItemType.Manufacturer, ItemId = manufacturer.Id, ActionType = ActionType.Import,
                CreatedBy = actingUserId, CompanyId = companyId,
                Note = $"Import nhà sản xuất \"{name}\" (mã: {code})"
            });
            results.Add(new ImportRowResult(r, true, $"Đã import nhà sản xuất '{name}' (mã {code})."));
        }

        await _context.SaveChangesAsync(ct);
        await _cacheInvalidator.InvalidateManufacturersAsync(ct);
        return results;
    }

    private async Task<List<ImportRowResult>> ImportLocationsSheetAsync(XLWorkbook wb, Guid actingUserId, Guid companyId, CancellationToken ct)
    {
        if (!TryGetSheet(wb, SheetLocations, out var ws, out var err)) return [err];
        int? hr = FindHeaderRow(ws, "Ten dia diem");
        if (hr == null) return [new ImportRowResult(0, false, $"Sheet '{SheetLocations}' thiếu cột 'Ten dia diem'.")];
        int colName = FindColumn(ws, hr.Value, "Ten dia diem");
        int colParent = FindColumn(ws, hr.Value, "Dia diem cha");

        var userCompanyId = (Guid?)companyId; // [Task IMPORT-T5] one import = one validated company
        var results = new List<ImportRowResult>();

        // Two passes: parents first (a child whose parent is not yet imported is an error).
        var rows = new List<(int Row, string Name, string? Parent)>();
        for (int r = hr.Value + 1; r <= ws.LastRowUsed()?.RowNumber(); r++)
        {
            ct.ThrowIfCancellationRequested();
            var name = Cell(ws, r, colName)?.Trim();
            if (string.IsNullOrWhiteSpace(name)) continue;
            rows.Add((r, name, Cell(ws, r, colParent)?.Trim()));
        }

        foreach (var row in rows.Where(x => x.Parent == null))
        {
            ct.ThrowIfCancellationRequested();
            if (await _context.Locations.AnyAsync(l => l.Name == row.Name, ct))
            {
                results.Add(new ImportRowResult(row.Row, true, $"Địa điểm '{row.Name}' đã tồn tại — bỏ qua."));
                continue;
            }
            var location = new Location { Name = row.Name, ParentId = null, CompanyId = userCompanyId };
            _context.Locations.Add(location);
            _actionLogService.Log(new ActionLogEntry
            {
                ItemType = ItemType.Location, ItemId = location.Id, ActionType = ActionType.Import,
                CreatedBy = actingUserId, CompanyId = location.CompanyId,
                Note = $"Import địa điểm \"{row.Name}\""
            });
            results.Add(new ImportRowResult(row.Row, true, $"Đã import địa điểm '{row.Name}'."));
        }
        await _context.SaveChangesAsync(ct);

        foreach (var row in rows.Where(x => x.Parent != null))
        {
            ct.ThrowIfCancellationRequested();
            if (await _context.Locations.AnyAsync(l => l.Name == row.Name, ct))
            {
                results.Add(new ImportRowResult(row.Row, true, $"Địa điểm '{row.Name}' đã tồn tại — bỏ qua."));
                continue;
            }

            var parentId = await _context.Locations.Where(l => l.Name == row.Parent).Select(l => (Guid?)l.Id).FirstOrDefaultAsync(ct);
            if (parentId == null)
            {
                results.Add(new ImportRowResult(row.Row, false, $"Địa điểm cha '{row.Parent}' chưa tồn tại (import cha trước)."));
                continue;
            }

            var location = new Location { Name = row.Name, ParentId = parentId, CompanyId = userCompanyId };
            _context.Locations.Add(location);
            _actionLogService.Log(new ActionLogEntry
            {
                ItemType = ItemType.Location, ItemId = location.Id, ActionType = ActionType.Import,
                CreatedBy = actingUserId, CompanyId = location.CompanyId,
                Note = $"Import địa điểm \"{row.Name}\""
            });
            results.Add(new ImportRowResult(row.Row, true, $"Đã import địa điểm '{row.Name}'."));
        }

        await _context.SaveChangesAsync(ct);
        return results;
    }

    // ─────────────────────────── T2: assets ───────────────────────────

    private async Task<List<ImportRowResult>> ImportAssetsSheetAsync(XLWorkbook wb, Guid actingUserId, Guid companyId, CancellationToken ct)
    {
        if (!TryGetSheet(wb, SheetAssets, out var ws, out var err)) return [err];
        int? hr = FindHeaderRow(ws, "Ma tai san");
        if (hr == null) return [new ImportRowResult(0, false, $"Sheet '{SheetAssets}' thiếu cột 'Ma tai san'.")];
        int colTag = FindColumn(ws, hr.Value, "Ma tai san");
        int colName = FindColumn(ws, hr.Value, "Ten tai san");
        int colCategory = FindColumn(ws, hr.Value, "Danh muc");
        int colSerial = FindColumn(ws, hr.Value, "Serial");
        int colModel = FindColumn(ws, hr.Value, "Model");
        int colMfr = FindColumn(ws, hr.Value, "Nha san xuat");
        int colLocation = FindColumn(ws, hr.Value, "Dia diem");
        int colStatus = FindColumn(ws, hr.Value, "Trang thai");
        int colNotes = FindColumn(ws, hr.Value, "Ghi chu");

        var userCompanyId = (Guid?)companyId; // [Task IMPORT-T5] one import = one validated company
        var results = new List<ImportRowResult>();

        for (int r = hr.Value + 1; r <= ws.LastRowUsed()?.RowNumber(); r++)
        {
            ct.ThrowIfCancellationRequested();
            var tag = Cell(ws, r, colTag)?.Trim();
            var name = Cell(ws, r, colName)?.Trim();
            if (string.IsNullOrWhiteSpace(tag) && string.IsNullOrWhiteSpace(name)) continue;
            if (string.IsNullOrWhiteSpace(tag) || string.IsNullOrWhiteSpace(name))
            {
                results.Add(new ImportRowResult(r, false, "Mã tài sản và Tên tài sản là bắt buộc."));
                continue;
            }

            if (await _context.Assets.AnyAsync(a => a.AssetTag == tag, ct))
            {
                results.Add(new ImportRowResult(r, false, $"Mã tài sản '{tag}' đã tồn tại trong hệ thống."));
                continue;
            }

            var categoryName = Cell(ws, r, colCategory)?.Trim();
            Guid? categoryId = null;
            if (categoryName != null)
            {
                categoryId = await _context.Categories
                    .Where(c => c.Name == categoryName && c.CategoryType == CategoryType.Asset)
                    .Select(c => (Guid?)c.Id).FirstOrDefaultAsync(ct);
                if (categoryId == null)
                {
                    results.Add(new ImportRowResult(r, false, $"Danh mục '{categoryName}' (loại Asset) chưa tồn tại — hãy import sheet 1_DanhMuc trước."));
                    continue;
                }
            }

            var serial = Cell(ws, r, colSerial)?.Trim();

            var modelName = Cell(ws, r, colModel)?.Trim();
            Guid? modelId = null;
            if (modelName != null)
            {
                // AssetModel is NOT auto-created (approved decision): resolve by name only.
                modelId = await _context.Models.Where(m => m.Name == modelName).Select(m => (Guid?)m.Id).FirstOrDefaultAsync(ct);
                if (modelId == null)
                {
                    results.Add(new ImportRowResult(r, false, $"Model '{modelName}' chưa tồn tại — không tự tạo model."));
                    continue;
                }
            }

            var mfrName = Cell(ws, r, colMfr)?.Trim();
            Guid? mfrId = null;
            if (mfrName != null)
            {
                mfrId = await _context.Manufacturers.Where(m => m.Name == mfrName).Select(m => (Guid?)m.Id).FirstOrDefaultAsync(ct);
                if (mfrId == null)
                {
                    results.Add(new ImportRowResult(r, false, $"Nhà sản xuất '{mfrName}' chưa tồn tại — hãy import sheet 3_NhaSanXuat trước."));
                    continue;
                }
            }

            var locationName = Cell(ws, r, colLocation)?.Trim();
            Guid? locationId = null;
            if (locationName != null)
            {
                locationId = await _context.Locations.Where(l => l.Name == locationName).Select(l => (Guid?)l.Id).FirstOrDefaultAsync(ct);
                if (locationId == null)
                {
                    results.Add(new ImportRowResult(r, false, $"Địa điểm '{locationName}' chưa tồn tại — hãy import sheet 2_DiaDiem trước."));
                    continue;
                }
            }

            var statusRaw = Cell(ws, r, colStatus)?.Trim();
            if (statusRaw != null && !IsKnownAssetStatus(statusRaw))
            {
                results.Add(new ImportRowResult(r, false, $"Trạng thái '{statusRaw}' không hợp lệ (chỉ hỗ trợ: Sẵn sàng)."));
                continue;
            }

            var asset = new Asset
            {
                AssetTag = tag, Name = name, Serial = serial, ModelId = modelId,
                LocationId = locationId, CompanyId = userCompanyId,
                Status = AssetStatus.Pending, IsConfirmed = true,
                Notes = Cell(ws, r, colNotes)?.Trim()
            };
            _context.Assets.Add(asset);
            _actionLogService.Log(new ActionLogEntry
            {
                ItemType = ItemType.Asset, ItemId = asset.Id, ActionType = ActionType.Import,
                CreatedBy = actingUserId, CompanyId = asset.CompanyId,
                Note = $"Import tài sản \"{tag} - {name}\""
            });
            results.Add(new ImportRowResult(r, true, $"Đã import tài sản '{tag}'."));
        }

        await _context.SaveChangesAsync(ct);
        return results;
    }

    // ─────────────────────────── T3: components ───────────────────────────

    private async Task<List<ImportRowResult>> ImportComponentsSheetAsync(XLWorkbook wb, Guid actingUserId, Guid companyId, CancellationToken ct)
    {
        if (!TryGetSheet(wb, SheetComponents, out var ws, out var err)) return [err];
        int? hr = FindHeaderRow(ws, "Ten linh kien");
        if (hr == null) return [new ImportRowResult(0, false, $"Sheet '{SheetComponents}' thiếu cột 'Ten linh kien'.")];
        int colName = FindColumn(ws, hr.Value, "Ten linh kien");
        int colCategory = FindColumn(ws, hr.Value, "Danh muc");
        int colTracking = FindColumn(ws, hr.Value, "Kieu theo doi");
        int colSerial = FindColumn(ws, hr.Value, "Serial");
        int colMin = FindColumn(ws, hr.Value, "Nguong canh bao");
        int colModel = FindColumn(ws, hr.Value, "Model");
        int colMfr = FindColumn(ws, hr.Value, "Nha san xuat");
        int colLocation = FindColumn(ws, hr.Value, "Dia diem");

        var userCompanyId = (Guid?)companyId; // [Task IMPORT-T5] one import = one validated company
        var results = new List<ImportRowResult>();

        // Group rows by (Name, CategoryName, ModelNumber) — serial rows of the same group are merged
        // into ONE serial-tracked component (approved decision).
        var groups = new Dictionary<string, ComponentGroup>(StringComparer.OrdinalIgnoreCase);
        for (int r = hr.Value + 1; r <= ws.LastRowUsed()?.RowNumber(); r++)
        {
            ct.ThrowIfCancellationRequested();
            var name = Cell(ws, r, colName)?.Trim();
            if (string.IsNullOrWhiteSpace(name)) continue;

            var categoryName = Cell(ws, r, colCategory)?.Trim();
            var trackingRaw = Cell(ws, r, colTracking)?.Trim();
            var serial = Cell(ws, r, colSerial)?.Trim();
            var modelNumber = Cell(ws, r, colModel)?.Trim();
            var mfrName = Cell(ws, r, colMfr)?.Trim();
            var locationName = Cell(ws, r, colLocation)?.Trim();
            var min = ParseInt(Cell(ws, r, colMin));

            var key = $"{name}|{categoryName}|{modelNumber}";
            if (!groups.TryGetValue(key, out var g))
            {
                g = new ComponentGroup(name, categoryName, modelNumber, mfrName, locationName, min);
                groups[key] = g;
            }
            g.RowNumbers.Add(r);
            if (!string.IsNullOrWhiteSpace(serial)) g.Serials.Add(serial);
        }

        foreach (var g in groups.Values)
        {
            ct.ThrowIfCancellationRequested();
            var firstRow = g.RowNumbers[0];

            if (string.IsNullOrWhiteSpace(g.CategoryName))
            {
                results.Add(new ImportRowResult(firstRow, false, "Danh mục (Category) là bắt buộc khi tạo linh kiện."));
                continue;
            }
            var categoryId = await _context.Categories
                .Where(c => c.Name == g.CategoryName && c.CategoryType == CategoryType.Component)
                .Select(c => (Guid?)c.Id).FirstOrDefaultAsync(ct);
            if (categoryId == null)
            {
                results.Add(new ImportRowResult(firstRow, false, $"Danh mục '{g.CategoryName}' (loại Component) chưa tồn tại — hãy import sheet 1_DanhMuc trước."));
                continue;
            }

            Guid? mfrId = null;
            if (!string.IsNullOrWhiteSpace(g.MfrName))
            {
                mfrId = await _context.Manufacturers.Where(m => m.Name == g.MfrName).Select(m => (Guid?)m.Id).FirstOrDefaultAsync(ct);
                if (mfrId == null)
                {
                    results.Add(new ImportRowResult(firstRow, false, $"Nhà sản xuất '{g.MfrName}' chưa tồn tại — hãy import sheet 3_NhaSanXuat trước."));
                    continue;
                }
            }

            Guid? locationId = null;
            if (!string.IsNullOrWhiteSpace(g.LocationName))
            {
                locationId = await _context.Locations.Where(l => l.Name == g.LocationName).Select(l => (Guid?)l.Id).FirstOrDefaultAsync(ct);
                if (locationId == null)
                {
                    results.Add(new ImportRowResult(firstRow, false, $"Địa điểm '{g.LocationName}' chưa tồn tại — hãy import sheet 2_DiaDiem trước."));
                    continue;
                }
            }

            var serials = g.Serials.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var isSerial = serials.Count > 0;
            var qty = isSerial ? serials.Count : g.RowNumbers.Count;

            var component = new Component
            {
                Name = g.Name, CategoryId = categoryId, ManufacturerId = mfrId, LocationId = locationId,
                CompanyId = userCompanyId, ModelNumber = g.ModelNumber,
                MinAmt = g.MinAmt ?? 0, Qty = 0,
                TrackingType = isSerial ? TrackingType.Serial : TrackingType.Bulk
            };
            _context.Components.Add(component);
            _actionLogService.Log(new ActionLogEntry
            {
                ItemType = ItemType.Component, ItemId = component.Id, ActionType = ActionType.Import,
                CreatedBy = actingUserId, CompanyId = component.CompanyId,
                Note = $"Import linh kiện \"{g.Name}\" ({(isSerial ? $"{serials.Count} serial" : $"{qty} cái")})"
            });

            if (isSerial)
            {
                // Persist component first (StockInAsync needs its id), then stock the serials via the
                // shared allocation service (per-unit ActionLog, duplicate-serial guard) — same SaveChanges.
                await _context.SaveChangesAsync(ct);
                var stockIn = await _componentAllocation.StockInAsync(component.Id, serials, "Import linh kiện (serial)", actingUserId, ct);
                if (!stockIn.Success)
                {
                    results.Add(new ImportRowResult(firstRow, false, $"Linh kiện '{g.Name}': {stockIn.Message}"));
                    _context.Components.Remove(component);
                    await _context.SaveChangesAsync(ct);
                    continue;
                }
                results.Add(new ImportRowResult(firstRow, true, $"Đã import linh kiện '{g.Name}' ({serials.Count} serial)."));
            }
            else
            {
                component.Qty = qty;
                results.Add(new ImportRowResult(firstRow, true, $"Đã import linh kiện '{g.Name}' ({qty} cái, bulk)."));
            }
        }

        await _context.SaveChangesAsync(ct);
        return results;
    }

    // ─────────────────────────── T4: accessories & consumables ───────────────────────────

    private async Task<List<ImportRowResult>> ImportAccessoriesSheetAsync(XLWorkbook wb, Guid actingUserId, Guid companyId, CancellationToken ct)
    {
        if (!TryGetSheet(wb, SheetAccessories, out var ws, out var err)) return [err];
        int? hr = FindHeaderRow(ws, "Ten phu kien");
        if (hr == null) return [new ImportRowResult(0, false, $"Sheet '{SheetAccessories}' thiếu cột 'Ten phu kien'.")];
        int colName = FindColumn(ws, hr.Value, "Ten phu kien");
        int colCategory = FindColumn(ws, hr.Value, "Danh muc");
        int colQty = FindColumn(ws, hr.Value, "So luong");
        int colMin = FindColumn(ws, hr.Value, "Nguong canh bao");
        int colModel = FindColumn(ws, hr.Value, "Ma / Model");
        int colMfr = FindColumn(ws, hr.Value, "Nha san xuat");
        int colLocation = FindColumn(ws, hr.Value, "Dia diem");
        int colNotes = FindColumn(ws, hr.Value, "Ghi chu");

        var userCompanyId = (Guid?)companyId; // [Task IMPORT-T5] one import = one validated company
        var results = new List<ImportRowResult>();

        for (int r = hr.Value + 1; r <= ws.LastRowUsed()?.RowNumber(); r++)
        {
            ct.ThrowIfCancellationRequested();
            var name = Cell(ws, r, colName)?.Trim();
            if (string.IsNullOrWhiteSpace(name)) continue;

            var categoryName = Cell(ws, r, colCategory)?.Trim();
            if (string.IsNullOrWhiteSpace(categoryName))
            {
                results.Add(new ImportRowResult(r, false, "Danh mục là bắt buộc."));
                continue;
            }
            var categoryId = await _context.Categories
                .Where(c => c.Name == categoryName && c.CategoryType == CategoryType.Accessory)
                .Select(c => (Guid?)c.Id).FirstOrDefaultAsync(ct);
            if (categoryId == null)
            {
                results.Add(new ImportRowResult(r, false, $"Danh mục '{categoryName}' (loại Accessory) chưa tồn tại — hãy import sheet 1_DanhMuc trước."));
                continue;
            }

            var qty = ParseInt(Cell(ws, r, colQty)) ?? 1;
            var min = ParseInt(Cell(ws, r, colMin)) ?? 0;
            var modelNumber = Cell(ws, r, colModel)?.Trim();
            var mfrName = Cell(ws, r, colMfr)?.Trim();
            var locationName = Cell(ws, r, colLocation)?.Trim();
            var notes = Cell(ws, r, colNotes)?.Trim();

            Guid? mfrId = null;
            if (!string.IsNullOrWhiteSpace(mfrName))
            {
                mfrId = await _context.Manufacturers.Where(m => m.Name == mfrName).Select(m => (Guid?)m.Id).FirstOrDefaultAsync(ct);
                if (mfrId == null)
                {
                    results.Add(new ImportRowResult(r, false, $"Nhà sản xuất '{mfrName}' chưa tồn tại — hãy import sheet 3_NhaSanXuat trước."));
                    continue;
                }
            }

            Guid? locationId = null;
            if (!string.IsNullOrWhiteSpace(locationName))
            {
                locationId = await _context.Locations.Where(l => l.Name == locationName).Select(l => (Guid?)l.Id).FirstOrDefaultAsync(ct);
                if (locationId == null)
                {
                    results.Add(new ImportRowResult(r, false, $"Địa điểm '{locationName}' chưa tồn tại — hãy import sheet 2_DiaDiem trước."));
                    continue;
                }
            }

            var accessory = new Accessory
            {
                Name = name, CategoryId = categoryId, ManufacturerId = mfrId, LocationId = locationId,
                CompanyId = userCompanyId, ModelNumber = modelNumber, Qty = qty, MinAmt = min, Notes = notes
            };
            _context.Accessories.Add(accessory);
            _actionLogService.Log(new ActionLogEntry
            {
                ItemType = ItemType.Accessory, ItemId = accessory.Id, ActionType = ActionType.Import,
                CreatedBy = actingUserId, CompanyId = accessory.CompanyId,
                Note = $"Import phụ kiện \"{name}\" ({qty} cái)"
            });
            results.Add(new ImportRowResult(r, true, $"Đã import phụ kiện '{name}'."));
        }

        await _context.SaveChangesAsync(ct);
        return results;
    }

    private async Task<List<ImportRowResult>> ImportConsumablesSheetAsync(XLWorkbook wb, Guid actingUserId, Guid companyId, CancellationToken ct)
    {
        if (!TryGetSheet(wb, SheetConsumables, out var ws, out var err)) return [err];
        int? hr = FindHeaderRow(ws, "Ten vat tu");
        if (hr == null) return [new ImportRowResult(0, false, $"Sheet '{SheetConsumables}' thiếu cột 'Ten vat tu'.")];
        int colName = FindColumn(ws, hr.Value, "Ten vat tu");
        int colCategory = FindColumn(ws, hr.Value, "Danh muc");
        int colQty = FindColumn(ws, hr.Value, "So luong");
        int colMin = FindColumn(ws, hr.Value, "Nguong canh bao");
        int colModel = FindColumn(ws, hr.Value, "Ma / Model");
        int colMfr = FindColumn(ws, hr.Value, "Nha san xuat");
        int colLocation = FindColumn(ws, hr.Value, "Dia diem");
        int colNotes = FindColumn(ws, hr.Value, "Ghi chu");

        var userCompanyId = (Guid?)companyId; // [Task IMPORT-T5] one import = one validated company
        var results = new List<ImportRowResult>();

        for (int r = hr.Value + 1; r <= ws.LastRowUsed()?.RowNumber(); r++)
        {
            ct.ThrowIfCancellationRequested();
            var name = Cell(ws, r, colName)?.Trim();
            if (string.IsNullOrWhiteSpace(name)) continue;

            var categoryName = Cell(ws, r, colCategory)?.Trim();
            if (string.IsNullOrWhiteSpace(categoryName))
            {
                results.Add(new ImportRowResult(r, false, "Danh mục là bắt buộc."));
                continue;
            }
            var categoryId = await _context.Categories
                .Where(c => c.Name == categoryName && c.CategoryType == CategoryType.Consumable)
                .Select(c => (Guid?)c.Id).FirstOrDefaultAsync(ct);
            if (categoryId == null)
            {
                results.Add(new ImportRowResult(r, false, $"Danh mục '{categoryName}' (loại Consumable) chưa tồn tại — hãy import sheet 1_DanhMuc trước."));
                continue;
            }

            var qty = ParseInt(Cell(ws, r, colQty)) ?? 1;
            var min = ParseInt(Cell(ws, r, colMin)) ?? 0;
            var modelNumber = Cell(ws, r, colModel)?.Trim();
            var mfrName = Cell(ws, r, colMfr)?.Trim();
            var locationName = Cell(ws, r, colLocation)?.Trim();
            var notes = Cell(ws, r, colNotes)?.Trim();

            Guid? mfrId = null;
            if (!string.IsNullOrWhiteSpace(mfrName))
            {
                mfrId = await _context.Manufacturers.Where(m => m.Name == mfrName).Select(m => (Guid?)m.Id).FirstOrDefaultAsync(ct);
                if (mfrId == null)
                {
                    results.Add(new ImportRowResult(r, false, $"Nhà sản xuất '{mfrName}' chưa tồn tại — hãy import sheet 3_NhaSanXuat trước."));
                    continue;
                }
            }

            Guid? locationId = null;
            if (!string.IsNullOrWhiteSpace(locationName))
            {
                locationId = await _context.Locations.Where(l => l.Name == locationName).Select(l => (Guid?)l.Id).FirstOrDefaultAsync(ct);
                if (locationId == null)
                {
                    results.Add(new ImportRowResult(r, false, $"Địa điểm '{locationName}' chưa tồn tại — hãy import sheet 2_DiaDiem trước."));
                    continue;
                }
            }

            var consumable = new Consumable
            {
                Name = name, CategoryId = categoryId, ManufacturerId = mfrId, LocationId = locationId,
                CompanyId = userCompanyId, ModelNumber = modelNumber, Qty = qty, MinAmt = min, Notes = notes,
                Status = ConsumableStatus.Pending
            };
            _context.Consumables.Add(consumable);
            _actionLogService.Log(new ActionLogEntry
            {
                ItemType = ItemType.Consumable, ItemId = consumable.Id, ActionType = ActionType.Import,
                CreatedBy = actingUserId, CompanyId = consumable.CompanyId,
                Note = $"Import vật tư tiêu hao \"{name}\" ({qty} cái)"
            });
            results.Add(new ImportRowResult(r, true, $"Đã import vật tư tiêu hao '{name}'."));
        }

        await _context.SaveChangesAsync(ct);
        return results;
    }

    // ─────────────────────────── SystemInfo (Hệ thống) ───────────────────────────

    /// <summary>Import SystemInfo from sheet 1_HeThong (Ten he thong + Vi tri khai thac tham khao).</summary>
    private async Task<List<ImportRowResult>> ImportSystemsSheetAsync(XLWorkbook wb, Guid actingUserId, Guid companyId, CancellationToken ct)
    {
        if (!TryGetSheet(wb, SheetSystems, out var ws, out var err)) return [err];
        int? hr = FindHeaderRow(ws, "Ten he thong");
        if (hr == null) return [new ImportRowResult(0, false, $"Sheet '{SheetSystems}' thiếu cột 'Ten he thong'.")];
        int colName = FindColumn(ws, hr.Value, "Ten he thong");
        int colDesc = FindColumn(ws, hr.Value, "Vi tri khai thac");

        var results = new List<ImportRowResult>();
        var year = DateTime.Now.Year;
        var batchCodes = new HashSet<string>(StringComparer.Ordinal);

        for (int r = hr.Value + 1; r <= ws.LastRowUsed()?.RowNumber(); r++)
        {
            ct.ThrowIfCancellationRequested();
            var name = Cell(ws, r, colName)?.Trim();
            if (string.IsNullOrWhiteSpace(name)) continue;

            // Duplicate by name in DB (per-company uniqueness is NOT enforced — a system name
            // may exist under another company; skip only when the same (name + company) exists).
            if (await _context.SystemInfos.AnyAsync(s => s.Name == name && s.CompanyId == companyId, ct))
            {
                results.Add(new ImportRowResult(r, true, $"Hệ thống '{name}' đã tồn tại — bỏ qua."));
                continue;
            }

            var code = await GenerateSystemCodeAsync("SYS", year, batchCodes, ct);
            var description = Cell(ws, r, colDesc)?.Trim();
            var sys = new SystemInfo { Code = code, Name = name, Description = description, CompanyId = companyId };
            _context.SystemInfos.Add(sys);
            _actionLogService.Log(new ActionLogEntry
            {
                ItemType = ItemType.SystemInfo, ItemId = sys.Id, ActionType = ActionType.Import,
                CreatedBy = actingUserId, CompanyId = companyId,
                Note = $"Import hệ thống \"{name}\" (mã: {code})"
            });
            results.Add(new ImportRowResult(r, true, $"Đã import hệ thống '{name}' (mã {code})."));
        }

        await _context.SaveChangesAsync(ct);
        return results;
    }

    // ─────────────────────────── SystemPosition (Vị trí trong hệ thống) ───────────────────────────

    /// <summary>
    /// Import SystemPosition from sheet 2_ViTri. Column "He thong cha (ten)" resolves the parent
    /// SystemInfo BY NAME (never auto-created — a missing parent errors that row only). Position
    /// inherits CompanyId from its parent SystemInfo. No separate companyId is chosen for positions
    /// (B0.4 confirmed inheritance); instead the acting user's scope (<paramref name="actingUserCompanyId"/>,
    /// null for Superuser) is validated against each referenced parent — a regular user may only attach
    /// positions to systems of their own company (or company-less systems). Extra reference columns
    /// (Hang SX, P/N, S/N, Nam SX, Tinh trang, Ghi chu...) are merged into Description.
    /// </summary>
    private async Task<List<ImportRowResult>> ImportSystemPositionsSheetAsync(XLWorkbook wb, Guid actingUserId, Guid? actingUserCompanyId, CancellationToken ct)
    {
        if (!TryGetSheet(wb, SheetSystemPositions, out var ws, out var err)) return [err];
        int? hr = FindHeaderRow(ws, "He thong cha");
        if (hr == null) return [new ImportRowResult(0, false, $"Sheet '{SheetSystemPositions}' thiếu cột 'He thong cha (ten)'.")];
        int colParent = FindColumn(ws, hr.Value, "He thong cha");
        int colName = FindColumn(ws, hr.Value, "Ten vi tri");
        int colMfr = FindColumn(ws, hr.Value, "Hang san xuat");
        int colPn = FindColumn(ws, hr.Value, "P/N");
        int colSn = FindColumn(ws, hr.Value, "S/N");
        int colLocation = FindColumn(ws, hr.Value, "Vi tri khai thac");
        int colYear = FindColumn(ws, hr.Value, "Nam SX");
        int colRole = FindColumn(ws, hr.Value, "Thanh phan");
        int colStatus = FindColumn(ws, hr.Value, "Tinh trang khai thac");
        int colNotes = FindColumn(ws, hr.Value, "Ghi chu");

        var results = new List<ImportRowResult>();
        var year = DateTime.Now.Year;
        var batchCodes = new HashSet<string>(StringComparer.Ordinal);

        for (int r = hr.Value + 1; r <= ws.LastRowUsed()?.RowNumber(); r++)
        {
            ct.ThrowIfCancellationRequested();
            var parentName = Cell(ws, r, colParent)?.Trim();
            var name = Cell(ws, r, colName)?.Trim();
            if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(parentName)) continue;
            if (string.IsNullOrWhiteSpace(name))
            {
                results.Add(new ImportRowResult(r, false, "Tên vị trí là bắt buộc."));
                continue;
            }
            if (string.IsNullOrWhiteSpace(parentName))
            {
                results.Add(new ImportRowResult(r, false, "Hệ thống cha là bắt buộc."));
                continue;
            }

            // Resolve parent SystemInfo BY NAME. A regular user may only attach positions to systems
            // in their own company scope (or company-less systems); Superuser (null) sees all.
            var parentQuery = _context.SystemInfos.Where(s => s.Name == parentName);
            if (actingUserCompanyId.HasValue)
                parentQuery = parentQuery.Where(s => s.CompanyId == null || s.CompanyId == actingUserCompanyId.Value);
            var parent = await parentQuery.FirstOrDefaultAsync(ct);
            if (parent == null)
            {
                results.Add(new ImportRowResult(r, false, $"Hệ thống cha '{parentName}' chưa tồn tại hoặc nằm ngoài phạm vi công ty của bạn — hãy import sheet 1_HeThong trước."));
                continue;
            }

            // Duplicate by (parent + name) within the same parent.
            if (await _context.SystemPositions.AnyAsync(p => p.SystemInfoId == parent.Id && p.Name == name, ct))
            {
                results.Add(new ImportRowResult(r, true, $"Vị trí '{name}' trong hệ thống '{parentName}' đã tồn tại — bỏ qua."));
                continue;
            }

            var code = await GenerateSystemCodeAsync("POS", year, batchCodes, ct);
            var position = new SystemPosition { SystemInfoId = parent.Id, Code = code, Name = name, Description = BuildPositionDescription(ws, r, colMfr, colPn, colSn, colLocation, colYear, colRole, colStatus, colNotes) };
            _context.SystemPositions.Add(position);
            _actionLogService.Log(new ActionLogEntry
            {
                ItemType = ItemType.SystemPosition, ItemId = position.Id, ActionType = ActionType.Import,
                CreatedBy = actingUserId, CompanyId = parent.CompanyId, // inherits from parent SystemInfo
                Note = $"Import vị trí \"{name}\" (mã: {code}) trong hệ thống \"{parentName}\""
            });
            results.Add(new ImportRowResult(r, true, $"Đã import vị trí '{name}' trong hệ thống '{parentName}' (mã {code})."));
        }

        await _context.SaveChangesAsync(ct);
        return results;
    }

    /// <summary>Builds a readable "Label: value | Label: value" Description from the optional
    /// equipment-reference columns, skipping empty cells (no trailing "S/N: " when absent).</summary>
    private static string? BuildPositionDescription(IXLWorksheet ws, int row,
        int colMfr, int colPn, int colSn, int colLocation, int colYear, int colRole, int colStatus, int colNotes)
    {
        var parts = new List<string>();
        void Add(string label, int col)
        {
            var v = Cell(ws, row, col)?.Trim();
            if (!string.IsNullOrWhiteSpace(v)) parts.Add($"{label}: {v}");
        }
        Add("Hãng SX", colMfr);
        Add("P/N", colPn);
        Add("S/N", colSn);
        Add("Vị trí khai thác", colLocation);
        Add("Năm SX", colYear);
        Add("Thành phần / Vai trò", colRole);
        Add("Tình trạng khai thác", colStatus);
        Add("Ghi chú", colNotes);
        return parts.Count == 0 ? null : string.Join(" | ", parts);
    }

    // ─────────────────────────── helpers ───────────────────────────

    private sealed class ComponentGroup
    {
        public ComponentGroup(string name, string? categoryName, string? modelNumber, string? mfrName, string? locationName, int? minAmt)
        {
            Name = name; CategoryName = categoryName; ModelNumber = modelNumber;
            MfrName = mfrName; LocationName = locationName; MinAmt = minAmt;
        }
        public string Name { get; }
        public string? CategoryName { get; }
        public string? ModelNumber { get; }
        public string? MfrName { get; }
        public string? LocationName { get; }
        public int? MinAmt { get; }
        public List<string> Serials { get; } = new();
        public List<int> RowNumbers { get; } = new();
    }

    private static ImportSheetResult Summarize(List<ImportRowResult> rows)
        => new(rows.Count(x => x.Success), rows.Count(x => !x.Success), rows, rows.Where(x => !x.Success).ToList());

    private static bool TryGetSheet(XLWorkbook wb, string name, out IXLWorksheet ws, out ImportRowResult error)
    {
        if (wb.TryGetWorksheet(name, out var sheet))
        {
            ws = sheet; error = null!; return true;
        }
        ws = null!;
        error = new ImportRowResult(0, false, $"Sheet '{name}' không tồn tại trong file.");
        return false;
    }

    /// <summary>Reads a cell's display text (numbers/dates become their text representation).</summary>
    private static string? Cell(IXLWorksheet ws, int row, int col)
    {
        if (col < 1) return null;
        var cell = ws.Cell(row, col);
        if (cell.IsEmpty()) return null;
        return cell.GetString().Trim();
    }

    private static int? ParseInt(string? s)
        => int.TryParse(s, out var v) ? v : (int?)null;

    private static bool IsKnownAssetStatus(string raw)
        => raw.Equals("Sang sang", StringComparison.OrdinalIgnoreCase)
           || raw.Equals("Sẵn sàng", StringComparison.OrdinalIgnoreCase)
           || raw.Equals("Pending", StringComparison.OrdinalIgnoreCase);

    /// <summary>Normalizes a header label: lower-case, trimmed, whitespace removed, diacritics stripped.</summary>
    private static string Normalize(string s)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var ch in s.Trim().ToLowerInvariant())
        {
            if (char.IsWhiteSpace(ch)) continue;
            sb.Append(ch);
        }
        return sb.ToString();
    }

    /// <summary>Finds the header row — the first row (1..10) whose first cell exactly matches the
    /// expected first header (trusted even for single-column sheets such as 3_NhaSanXuat). A partial
    /// (StartsWith) match is only accepted when its 2nd column is non-empty, so descriptive instruction
    /// rows that merely begin with the header text (e.g. "Ma tai san da sinh san theo quy uoc…" and only
    /// fill column A) are skipped.</summary>
    private static int? FindHeaderRow(IXLWorksheet ws, string expectedFirstHeader)
    {
        var target = Normalize(expectedFirstHeader);
        for (int r = 1; r <= Math.Min(ws.LastRowUsed()?.RowNumber() ?? 1, 10); r++)
        {
            var first = Normalize(ws.Cell(r, 1).GetString());
            if (first.Length == 0) continue;
            var exact = first.Equals(target, StringComparison.OrdinalIgnoreCase);
            if (!exact && !first.StartsWith(target, StringComparison.OrdinalIgnoreCase))
                continue;
            // Guard against instruction rows that START WITH the header text but are really
            // descriptive sentences (e.g. "Ma tai san da sinh san theo quy uoc…") — such rows fill
            // only column A. An EXACT header match is trusted even for single-column sheets (e.g. the
            // 3_NhaSanXuat sheet legitimately has only one column), so the 2nd-column check only
            // applies to partial (StartsWith) matches.
            if (!exact)
            {
                var second = Normalize(ws.Cell(r, 2).GetString());
                if (second.Length == 0) continue;
            }
            return r;
        }
        return null;
    }

    /// <summary>Finds a column whose header (normalized) equals or starts with any candidate.</summary>
    private static int FindColumn(IXLWorksheet ws, int headerRow, params string[] candidates)
    {
        var normalized = candidates.Select(Normalize).ToArray();
        for (int c = 1; c <= ws.LastColumnUsed()?.ColumnNumber(); c++)
        {
            var h = Normalize(ws.Cell(headerRow, c).GetString());
            if (h.Length == 0) continue;
            if (normalized.Any(n => h.Equals(n, StringComparison.OrdinalIgnoreCase)
                                    || h.StartsWith(n, StringComparison.OrdinalIgnoreCase)))
                return c;
        }
        return -1;
    }

    private async Task<string> GenerateManufacturerCodeAsync(string name, CancellationToken ct)
    {
        var baseCode = new string(name.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        if (baseCode.Length > 5) baseCode = baseCode[..5];
        if (baseCode.Length < 2) baseCode = (baseCode + "XXXX")[..2];

        var candidate = baseCode;
        var suffix = 2;
        while (await _context.Manufacturers.AnyAsync(x => x.Code == candidate, ct))
        {
            var suffixStr = suffix.ToString();
            var prefixLen = Math.Max(0, 5 - suffixStr.Length);
            candidate = baseCode[..Math.Min(baseCode.Length, prefixLen)] + suffixStr;
            suffix++;
        }
        return candidate;
    }

    /// <summary>
    /// Generates a unique Code in the approved format <c>XXX-YYYY-ZZZ</c> (Task IMPORT-T7):
    /// <c>{prefix}-{year}-{sequence:000}</c> (e.g. SYS-2026-001, POS-2026-001). The 4-digit year is the
    /// CURRENT year (the import year — NOT the equipment's manufacturing year). The 3-digit sequence
    /// resets each year (queried by prefix, not cumulative across years). Uniqueness is guaranteed by
    /// scanning upward until the generated Code does not already exist in the DB OR in the running
    /// batch (<paramref name="batchCodes"/>) — so re-importing the same workbook (or many rows in one
    /// import) never collides, even before SaveChanges flushes the batch to the DB.
    /// </summary>
    private async Task<string> GenerateSystemCodeAsync(string prefix, int year, HashSet<string> batchCodes, CancellationToken ct)
    {
        var prefixWithYear = $"{prefix}-{year}-";
        var existing = await _context.SystemInfos.AsNoTracking()
            .Where(s => s.Code.StartsWith(prefixWithYear))
            .Select(s => s.Code)
            .ToListAsync(ct);
        // POS codes live in the system_positions table, not system_infos.
        if (prefix == "POS")
        {
            existing = existing.Concat(await _context.SystemPositions.AsNoTracking()
                .Where(p => p.Code.StartsWith(prefixWithYear))
                .Select(p => p.Code)
                .ToListAsync(ct)).ToList();
        }

        var taken = new HashSet<string>(existing, StringComparer.Ordinal);
        taken.UnionWith(batchCodes); // codes already handed out within THIS import batch

        var seq = 1;
        while (taken.Contains($"{prefixWithYear}{seq:D3}")) seq++;
        var code = $"{prefixWithYear}{seq:D3}";
        batchCodes.Add(code);
        return code;
    }
}
