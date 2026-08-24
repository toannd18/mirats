using System.Security.Claims;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using aspire_react.Server.Infrastructure.Persistence;
using aspire_react.Server.Infrastructure.Services;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Web.Controllers;

/// <summary>
/// Import/Export — Excel (.xlsx) only (CSV retired: it mangled Vietnamese diacritics in Excel).
/// Import reads sheets BY NAME from a workbook structured like
/// <c>docs/Mirats_DuLieuMau_VatTu_T&amp;E.xlsx</c> (1_DanhMuc…7_VatTuTieuHao), best-effort per row.
/// Every imported record gets its own ActionLog (ItemType.Import) in the same SaveChanges.
/// </summary>
[ApiController]
[Route("api/v1")]
public class ImportExportController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly ICompanyScopeService _companyScope;
    private readonly IActionLogService _actionLogService;
    private readonly IExcelImportService _excelImport;

    public ImportExportController(
        AppDbContext context,
        ICompanyScopeService companyScope,
        IActionLogService actionLogService,
        IExcelImportService excelImport)
    {
        _context = context;
        _companyScope = companyScope;
        _actionLogService = actionLogService;
        _excelImport = excelImport;
    }

    // ================================================================
    // IMPORT — .xlsx (multipart/form-data file field "file")
    // ================================================================

    /// <summary>Import reference data (categories, manufacturers, locations) from one workbook.</summary>
    [HttpPost("import/reference")]
    [Authorize(Policy = "categories.create")]
    public async Task<IActionResult> ImportReference(IFormFile? file, [FromForm] Guid companyId)
    {
        var badCompany = await ResolveImportCompanyIdAsync(companyId);
        if (badCompany != null) return badCompany;
        if (!ValidateFile(file, out var bad)) return bad;
        var result = await _excelImport.ImportReferenceAsync(file!.OpenReadStream(), GetCurrentUserId(), companyId);
        return Ok(new { status = "success", created = result.Created, failed = result.Failed, rows = result.Rows, errors = result.Errors });
    }

    /// <summary>
    /// Import AssetModel from sheet 3_Model. AssetModel is GLOBAL master data (no CompanyId column) —
    /// the chosen companyId ONLY stamps the import ActionLogs. Category/Manufacturer are resolved BY NAME
    /// and NEVER auto-created (a missing reference errors only that row) — so assets must be imported
    /// AFTER the models/sheets they reference exist. Import models BEFORE sheet 4_TaiSan.
    /// </summary>
    [HttpPost("import/asset-models")]
    [Authorize(Policy = "models.create")]
    public async Task<IActionResult> ImportAssetModels(IFormFile? file, [FromForm] Guid companyId)
    {
        var badCompany = await ResolveImportCompanyIdAsync(companyId);
        if (badCompany != null) return badCompany;
        if (!ValidateFile(file, out var bad)) return bad;
        var result = await _excelImport.ImportAssetModelsAsync(file!.OpenReadStream(), GetCurrentUserId(), companyId);
        return Ok(new { status = "success", created = result.Created, failed = result.Failed, rows = result.Rows, errors = result.Errors });
    }

    /// <summary>Import assets from sheet 4_TaiSan.</summary>
    [HttpPost("import/assets")]
    [Authorize(Policy = "assets.create")]
    public async Task<IActionResult> ImportAssets(IFormFile? file, [FromForm] Guid companyId)
    {
        var badCompany = await ResolveImportCompanyIdAsync(companyId);
        if (badCompany != null) return badCompany;
        if (!ValidateFile(file, out var bad)) return bad;
        var result = await _excelImport.ImportAssetsAsync(file!.OpenReadStream(), GetCurrentUserId(), companyId);
        return Ok(new { status = "success", created = result.Created, failed = result.Failed, rows = result.Rows, errors = result.Errors });
    }

    /// <summary>Import components from sheet 5_LinhKien (serial rows grouped by Name+Category+ModelNumber).</summary>
    [HttpPost("import/components")]
    [Authorize(Policy = "components.create")]
    public async Task<IActionResult> ImportComponents(IFormFile? file, [FromForm] Guid companyId)
    {
        var badCompany = await ResolveImportCompanyIdAsync(companyId);
        if (badCompany != null) return badCompany;
        if (!ValidateFile(file, out var bad)) return bad;
        var result = await _excelImport.ImportComponentsAsync(file!.OpenReadStream(), GetCurrentUserId(), companyId);
        return Ok(new { status = "success", created = result.Created, failed = result.Failed, rows = result.Rows, errors = result.Errors });
    }

    /// <summary>Import accessories from sheet 6_PhuKien.</summary>
    [HttpPost("import/accessories")]
    [Authorize(Policy = "accessories.create")]
    public async Task<IActionResult> ImportAccessories(IFormFile? file, [FromForm] Guid companyId)
    {
        var badCompany = await ResolveImportCompanyIdAsync(companyId);
        if (badCompany != null) return badCompany;
        if (!ValidateFile(file, out var bad)) return bad;
        var result = await _excelImport.ImportAccessoriesAsync(file!.OpenReadStream(), GetCurrentUserId(), companyId);
        return Ok(new { status = "success", created = result.Created, failed = result.Failed, rows = result.Rows, errors = result.Errors });
    }

    /// <summary>Import consumables from sheet 7_VatTuTieuHao.</summary>
    [HttpPost("import/consumables")]
    [Authorize(Policy = "consumables.create")]
    public async Task<IActionResult> ImportConsumables(IFormFile? file, [FromForm] Guid companyId)
    {
        var badCompany = await ResolveImportCompanyIdAsync(companyId);
        if (badCompany != null) return badCompany;
        if (!ValidateFile(file, out var bad)) return bad;
        var result = await _excelImport.ImportConsumablesAsync(file!.OpenReadStream(), GetCurrentUserId(), companyId);
        return Ok(new { status = "success", created = result.Created, failed = result.Failed, rows = result.Rows, errors = result.Errors });
    }

    /// <summary>Import SystemInfo from sheet 1_HeThong (company is the chosen import target).</summary>
    [HttpPost("import/systems")]
    [Authorize(Policy = "systems.create")]
    public async Task<IActionResult> ImportSystems(IFormFile? file, [FromForm] Guid companyId)
    {
        var badCompany = await ResolveImportCompanyIdAsync(companyId);
        if (badCompany != null) return badCompany;
        if (!ValidateFile(file, out var bad)) return bad;
        var result = await _excelImport.ImportSystemsAsync(file!.OpenReadStream(), GetCurrentUserId(), companyId);
        return Ok(new { status = "success", created = result.Created, failed = result.Failed, rows = result.Rows, errors = result.Errors });
    }

    /// <summary>
    /// Import SystemPosition from sheet 2_ViTri. The parent SystemInfo is resolved BY NAME and the
    /// position inherits the parent's CompanyId — so NO separate companyId is chosen by the client
    /// (B0.4 confirmed inheritance). The server still derives the acting user's company scope and
    /// validates every referenced parent against it: a regular user may only attach positions to
    /// systems of their own company (or company-less systems); Superuser may attach to any.
    /// </summary>
    [HttpPost("import/system-positions")]
    [Authorize(Policy = "systems.create")]
    public async Task<IActionResult> ImportSystemPositions(IFormFile? file)
    {
        if (!ValidateFile(file, out var bad)) return bad;
        var actingUserCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        var result = await _excelImport.ImportSystemPositionsAsync(file!.OpenReadStream(), GetCurrentUserId(), actingUserCompanyId);
        return Ok(new { status = "success", created = result.Created, failed = result.Failed, rows = result.Rows, errors = result.Errors });
    }

    // ================================================================
    // EXPORT — .xlsx (was CSV; switched for consistent Vietnamese-safe output)
    // ================================================================

    [HttpGet("export/assets")]
    [Authorize(Policy = "assets.view")]
    public async Task<IActionResult> ExportAssets()
    {
        var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        var assets = await _context.Assets
            .Include(a => a.Location)
            .Include(a => a.Model).ThenInclude(m => m != null ? m.Category : null)
            .Where(a => userCompanyId == null || a.CompanyId == null || a.CompanyId == userCompanyId.Value)
            .AsNoTracking().Take(1000).ToListAsync();

        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("TaiSan");
        string[] headers = ["AssetTag", "Name", "Serial", "Model", "Category", "Status", "Location", "PurchaseCost", "PurchaseDate"];
        for (int c = 0; c < headers.Length; c++) ws.Cell(1, c + 1).Value = headers[c];
        int r = 2;
        foreach (var a in assets)
        {
            ws.Cell(r, 1).Value = a.AssetTag; ws.Cell(r, 2).Value = a.Name; ws.Cell(r, 3).Value = a.Serial ?? "";
            ws.Cell(r, 4).Value = a.Model?.Name ?? ""; ws.Cell(r, 5).Value = a.Model?.Category?.Name ?? "";
            ws.Cell(r, 6).Value = a.Status.ToString(); ws.Cell(r, 7).Value = a.Location?.Name ?? "";
            ws.Cell(r, 8).Value = a.PurchaseCost.HasValue ? a.PurchaseCost.Value.ToString() : "";
            ws.Cell(r, 9).Value = a.PurchaseDate?.ToString("yyyy-MM-dd") ?? "";
            r++;
        }
        return File(ToBytes(wb), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"assets-export-{DateTime.UtcNow:yyyyMMdd}.xlsx");
    }

    [HttpGet("export/consumables")]
    [Authorize(Policy = "consumables.view")]
    public async Task<IActionResult> ExportConsumables()
    {
        var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        var items = await _context.Consumables.Include(c => c.Checkouts).AsNoTracking()
            .Where(c => userCompanyId == null || c.CompanyId == null || c.CompanyId == userCompanyId.Value)
            .Take(1000).ToListAsync();

        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("VatTuTieuHao");
        string[] headers = ["Name", "ItemNo", "Qty", "MinAmt", "Remaining"];
        for (int c = 0; c < headers.Length; c++) ws.Cell(1, c + 1).Value = headers[c];
        int r = 2;
        foreach (var c in items)
        {
            ws.Cell(r, 1).Value = c.Name; ws.Cell(r, 2).Value = c.ItemNo ?? "";
            ws.Cell(r, 3).Value = c.Qty; ws.Cell(r, 4).Value = c.MinAmt;
            ws.Cell(r, 5).Value = (c.Qty - c.Checkouts.Sum(ch => ch.Quantity)).ToString();
            r++;
        }
        return File(ToBytes(wb), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"consumables-export-{DateTime.UtcNow:yyyyMMdd}.xlsx");
    }

    // ================================================================
    // TEMPLATE — .xlsx matching the sample workbook structure
    // ================================================================

    [HttpGet("import/templates/assets")]
    [Authorize] // empty skeleton — no data, any authenticated user may download
    public IActionResult DownloadTemplate()
    {
        using var wb = new XLWorkbook();
        AddSheet(wb, "1_DanhMuc", ["Ten danh muc", "Loai (categoryType)", "Ma mau (tagColor)", "Ghi chu"]);
        AddSheet(wb, "2_DiaDiem", ["Ten dia diem", "Dia diem cha", "Ghi chu"]);
        AddSheet(wb, "3_NhaSanXuat", ["Ten nha san xuat"]);
        AddSheet(wb, "3_Model", ["Ten model", "So model", "Ten danh muc", "Ten nha san xuat", "Ghi chu"]);
        AddSheet(wb, "4_TaiSan", ["Ma tai san (Asset Tag)", "Ten tai san", "Danh muc", "Serial", "Model", "Nha san xuat", "Dia diem", "Trang thai", "Ghi chu"]);
        AddSheet(wb, "5_LinhKien", ["Ten linh kien", "Danh muc", "Kieu theo doi", "Serial", "So luong", "Nguong canh bao", "Model", "Nha san xuat", "Dia diem", "Ghi chu"]);
        AddSheet(wb, "6_PhuKien", ["Ten phu kien", "Danh muc", "So luong", "Nguong canh bao", "Ma / Model", "Nha san xuat", "Dia diem", "Ghi chu"]);
        AddSheet(wb, "7_VatTuTieuHao", ["Ten vat tu", "Danh muc", "So luong", "Nguong canh bao", "Ma / Model", "Nha san xuat", "Dia diem", "Ghi chu"]);
        return File(ToBytes(wb), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "import-template.xlsx");
    }

    /// <summary>Systems/Positions template (.xlsx) — mirrors the BDKT-CNTT workbook structure.</summary>
    [HttpGet("import/templates/systems")]
    [Authorize] // empty skeleton — no data, any authenticated user may download
    public IActionResult DownloadSystemsTemplate()
    {
        using var wb = new XLWorkbook();
        AddSheet(wb, "1_HeThong", ["Ten he thong", "Vi tri khai thac (tham khao)"]);
        AddSheet(wb, "2_ViTri", [
            "He thong cha (ten)", "Ten vi tri / thiet bi", "Hang san xuat", "P/N", "S/N",
            "Vi tri khai thac", "Nam SX", "Thanh phan / Vai tro", "Nam dua vao KT",
            "So nam su dung", "Tinh trang khai thac", "Ghi chu"
        ]);
        return File(ToBytes(wb), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "systems-import-template.xlsx");
    }

    // ================================================================
    // helpers
    // ================================================================

    /// <summary>
    /// [Task IMPORT-T5] Validates the client-supplied target company BEFORE any import happens —
    /// never trust the client (Task L2 principle). Rules:
    ///  - companyId is MANDATORY (approved decision: superuser must pick a company too; no floater).
    ///  - The id must lie inside the acting user's real scope
    ///    (<see cref="ICompanyScopeService.IsCompanyIdInUserScopeAsync"/>): a regular user may only
    ///    target their own company or its descendants; a company in another branch → 403.
    /// </summary>
    private async Task<IActionResult?> ResolveImportCompanyIdAsync(Guid companyId)
    {
        if (companyId == Guid.Empty)
        {
            return BadRequest(new { status = "error", message = "Phải chọn công ty cho lần import này.", error_code = "COMPANY_REQUIRED" });
        }
        if (!await _companyScope.IsCompanyIdInUserScopeAsync(companyId))
        {
            // 403 (not 404) per Task IMPORT-T5 acceptance: out-of-scope company is an authorization
            // violation; also covers non-existent ids (a user may never import into an unknown company).
            return Forbid();
        }
        return null;
    }

    private bool ValidateFile(IFormFile? file, out IActionResult bad)
    {
        if (file == null || file.Length == 0)
        {
            bad = BadRequest(new { status = "error", message = "No file provided." });
            return false;
        }
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (ext != ".xlsx")
        {
            bad = BadRequest(new { status = "error", message = "Chỉ hỗ trợ file .xlsx." });
            return false;
        }
        bad = null!;
        return true;
    }

    private static void AddSheet(XLWorkbook wb, string name, string[] headers)
    {
        var ws = wb.AddWorksheet(name);
        for (int c = 0; c < headers.Length; c++)
        {
            ws.Cell(1, c + 1).Value = headers[c];
            ws.Cell(1, c + 1).Style.Font.Bold = true;
        }
    }

    private static byte[] ToBytes(XLWorkbook wb)
    {
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private Guid GetCurrentUserId()
    {
        // [SEC-FIX CLAIM-CLEANUP, 2026-08-23] ONLY "local_user_id" (JIT-stamped). Keycloak
        // sub/preferred_username are never a user identity source (bug-class 1). Absent → Guid.Empty.
        if (Guid.TryParse(User.FindFirstValue("local_user_id"), out var local)) return local;
        return Guid.Empty;
    }
}
