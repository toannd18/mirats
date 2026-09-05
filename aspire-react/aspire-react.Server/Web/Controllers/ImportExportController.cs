using System.Security.Claims;
using aspire_react.Server.Application.ImportExport.Commands;
using aspire_react.Server.Application.ImportExport.Queries;
using aspire_react.Server.Domain.Interfaces;
using aspire_react.Server.Infrastructure.Persistence;
using ClosedXML.Excel;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace aspire_react.Server.Web.Controllers;

/// <summary>
/// Import/Export — Excel (.xlsx) only (CSV retired: it mangled Vietnamese diacritics in Excel).
/// Import reads sheets BY NAME from a workbook structured like
/// <c>docs/Mirats_DuLieuMau_VatTu_T&amp;E.xlsx</c> (1_DanhMuc…7_VatTuTieuHao), best-effort per row.
/// Every imported record gets its own ActionLog (ItemType.Import) in the same SaveChanges.
/// <para>
/// [Giai đoạn 3 — ImportExport] MediatR migration: 8 import endpoints → commands (guards verbatim
/// trong handlers: company-scope TRƯỚC file cho 7 endpoint chọn company; system-positions ngược
/// lại — file TRƯỚC, không chọn company theo B0.4 inheritance). Export ×2 → queries (row-DTOs;
/// workbook rendering + File() filename GIỮ ở controller — ClosedXML là Web presentation concern).
/// Template ×2 GIỮ INLINE — NGOẠI LỆ CÓ CHỦ ĐÍCH: 0 business logic (static skeleton workbook,
/// zero EF), ép qua Query rỗng chỉ tăng file không tăng giá trị (cùng tinh thần ghi chú lý do
/// không tạo interface cho ActionLogs IsItemVisibleAsync).
/// Import response giữ cấu trúc PHẲNG đặc thù (status/created/failed/rows/errors — KHÔNG wrapper
/// "data", khác mọi section khác — verbatim).
/// </para>
/// </summary>
[ApiController]
[Route("api/v1")]
public class ImportExportController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly AppDbContext _context;
    private readonly ICompanyScopeService _companyScope;
    private readonly IActionLogService _actionLogService;

    public ImportExportController(
        IMediator mediator,
        AppDbContext context,
        ICompanyScopeService companyScope,
        IActionLogService actionLogService)
    {
        _mediator = mediator;
        _context = context;
        _companyScope = companyScope;
        _actionLogService = actionLogService;
    }

    // _context/_companyScope/_actionLogService không còn dùng cho import/export nhưng GIỮ ctor
    // signature (tests + DI construct đủ tham số; giữ là chi phí zero, xóa là churn tests).

    private Guid GetCurrentUserId()
    {
        // [SEC-FIX CLAIM-CLEANUP, 2026-08-23] ONLY "local_user_id" (JIT-stamped). Keycloak
        // sub/preferred_username are never a user identity source (bug-class 1). Absent → Guid.Empty.
        if (Guid.TryParse(User?.FindFirstValue("local_user_id"), out var local)) return local;
        return Guid.Empty;
    }

    // ================================================================
    // IMPORT — .xlsx (multipart/form-data file field "file")
    // ================================================================

    /// <summary>Import reference data (categories, manufacturers, locations) from one workbook.</summary>
    [HttpPost("import/reference")]
    [Authorize(Policy = "categories.create")]
    public async Task<IActionResult> ImportReference(IFormFile? file, [FromForm] Guid companyId)
        => ImportViaCommand(await _mediator.Send(new ImportReferenceCommand(file?.OpenReadStream(), file?.FileName, companyId, GetCurrentUserId())));

    /// <summary>
    /// Import AssetModel from sheet 3_Model. AssetModel is GLOBAL master data (no CompanyId column) —
    /// the chosen companyId ONLY stamps the import ActionLogs. Category/Manufacturer are resolved BY NAME
    /// and NEVER auto-created (a missing reference errors only that row) — so assets must be imported
    /// AFTER the models/sheets they reference exist. Import models BEFORE sheet 4_TaiSan.
    /// </summary>
    [HttpPost("import/asset-models")]
    [Authorize(Policy = "models.create")]
    public async Task<IActionResult> ImportAssetModels(IFormFile? file, [FromForm] Guid companyId)
        => ImportViaCommand(await _mediator.Send(new ImportAssetModelsCommand(file?.OpenReadStream(), file?.FileName, companyId, GetCurrentUserId())));

    /// <summary>Import assets from sheet 4_TaiSan.</summary>
    [HttpPost("import/assets")]
    [Authorize(Policy = "assets.create")]
    public async Task<IActionResult> ImportAssets(IFormFile? file, [FromForm] Guid companyId)
        => ImportViaCommand(await _mediator.Send(new ImportAssetsCommand(file?.OpenReadStream(), file?.FileName, companyId, GetCurrentUserId())));

    /// <summary>Import components from sheet 5_LinhKien (serial rows grouped by Name+Category+ModelNumber).</summary>
    [HttpPost("import/components")]
    [Authorize(Policy = "components.create")]
    public async Task<IActionResult> ImportComponents(IFormFile? file, [FromForm] Guid companyId)
        => ImportViaCommand(await _mediator.Send(new ImportComponentsCommand(file?.OpenReadStream(), file?.FileName, companyId, GetCurrentUserId())));

    /// <summary>Import accessories from sheet 6_PhuKien.</summary>
    [HttpPost("import/accessories")]
    [Authorize(Policy = "accessories.create")]
    public async Task<IActionResult> ImportAccessories(IFormFile? file, [FromForm] Guid companyId)
        => ImportViaCommand(await _mediator.Send(new ImportAccessoriesCommand(file?.OpenReadStream(), file?.FileName, companyId, GetCurrentUserId())));

    /// <summary>Import consumables from sheet 7_VatTuTieuHao.</summary>
    [HttpPost("import/consumables")]
    [Authorize(Policy = "consumables.create")]
    public async Task<IActionResult> ImportConsumables(IFormFile? file, [FromForm] Guid companyId)
        => ImportViaCommand(await _mediator.Send(new ImportConsumablesCommand(file?.OpenReadStream(), file?.FileName, companyId, GetCurrentUserId())));

    /// <summary>Import SystemInfo from sheet 1_HeThong (company is the chosen import target).</summary>
    [HttpPost("import/systems")]
    [Authorize(Policy = "systems.create")]
    public async Task<IActionResult> ImportSystems(IFormFile? file, [FromForm] Guid companyId)
        => ImportViaCommand(await _mediator.Send(new ImportSystemsCommand(file?.OpenReadStream(), file?.FileName, companyId, GetCurrentUserId())));

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
        => ImportViaCommand(await _mediator.Send(new ImportSystemPositionsCommand(file?.OpenReadStream(), file?.FileName, GetCurrentUserId())));

    /// <summary>
    /// Shared import mapping — the response CONTRACT verbatim: the import body is FLAT
    /// (status/created/failed/rows/errors — NO "data" wrapper, unlike every other section) and the
    /// two file-guard 400s carry NO error_code. Forbidden → Forbid() 403 empty.
    /// </summary>
    private static IActionResult ImportViaCommand(ImportOutcome outcome) => outcome switch
    {
        _ when outcome.Forbidden => new ForbidResult(),
        _ when !outcome.Success && outcome.ErrorCode != null =>
            new BadRequestObjectResult(new { status = "error", message = outcome.ErrorMessage, error_code = outcome.ErrorCode }),
        _ when !outcome.Success =>
            new BadRequestObjectResult(new { status = "error", message = outcome.ErrorMessage }),
        _ => new OkObjectResult(new
        {
            status = "success",
            created = outcome.Result!.Created,
            failed = outcome.Result.Failed,
            rows = outcome.Result.Rows,
            errors = outcome.Result.Errors
        })
    };

    // ================================================================
    // EXPORT — .xlsx (was CSV; switched for consistent Vietnamese-safe output)
    // [Giai đoạn 3] Data qua queries (scope + Take(1000) verbatim trong handlers); workbook
    // rendering + filename giữ ở controller (ClosedXML = Web presentation concern).
    // ================================================================

    [HttpGet("export/assets")]
    [Authorize(Policy = "assets.view")]
    public async Task<IActionResult> ExportAssets()
    {
        var assets = await _mediator.Send(new ExportAssetsQuery());

        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("TaiSan");
        string[] headers = ["AssetTag", "Name", "Serial", "Model", "Category", "Status", "Location", "PurchaseCost", "PurchaseDate"];
        for (int c = 0; c < headers.Length; c++) ws.Cell(1, c + 1).Value = headers[c];
        int r = 2;
        foreach (var a in assets)
        {
            ws.Cell(r, 1).Value = a.AssetTag; ws.Cell(r, 2).Value = a.Name; ws.Cell(r, 3).Value = a.Serial ?? "";
            ws.Cell(r, 4).Value = a.ModelName ?? ""; ws.Cell(r, 5).Value = a.CategoryName ?? "";
            ws.Cell(r, 6).Value = a.Status; ws.Cell(r, 7).Value = a.LocationName ?? "";
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
        var items = await _mediator.Send(new ExportConsumablesQuery());

        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("VatTuTieuHao");
        string[] headers = ["Name", "ItemNo", "Qty", "MinAmt", "Remaining"];
        for (int c = 0; c < headers.Length; c++) ws.Cell(1, c + 1).Value = headers[c];
        int r = 2;
        foreach (var c in items)
        {
            ws.Cell(r, 1).Value = c.Name; ws.Cell(r, 2).Value = c.ItemNo ?? "";
            ws.Cell(r, 3).Value = c.Qty; ws.Cell(r, 4).Value = c.MinAmt;
            ws.Cell(r, 5).Value = c.Remaining.ToString();
            r++;
        }
        return File(ToBytes(wb), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"consumables-export-{DateTime.UtcNow:yyyyMMdd}.xlsx");
    }

    // ================================================================
    // TEMPLATE — .xlsx matching the sample workbook structure
    // [Giai đoạn 3 — NGOẠI LỆ CÓ CHỦ ĐÍCH: giữ INLINE, KHÔNG qua MediatR — 0 business logic
    // (static skeleton workbook, zero EF), Query rỗng chỉ tăng file không tăng giá trị.]
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
    // helpers (template/export rendering only)
    // ================================================================

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
}
