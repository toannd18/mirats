using System.Globalization;
using System.Security.Claims;
using CsvHelper;
using CsvHelper.Configuration;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using aspire_react.Server.Infrastructure.Persistence;
using aspire_react.Server.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Web.Controllers;

[ApiController]
[Route("api/v1")]
public class ImportExportController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly ICompanyScopeService _companyScope;
    private readonly IActionLogService _actionLogService;
    public ImportExportController(AppDbContext context, ICompanyScopeService companyScope, IActionLogService actionLogService)
    {
        _context = context;
        _companyScope = companyScope;
        _actionLogService = actionLogService;
    }

    // === Import Assets ===
    [HttpPost("import/assets")]
    [Authorize(Policy = "import")]
    public async Task<IActionResult> ImportAssets(IFormFile? file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { status = "error", message = "No file provided." });

        using var reader = new StreamReader(file.OpenReadStream());
        using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HeaderValidated = null,
            MissingFieldFound = null,
            TrimOptions = TrimOptions.Trim
        });

        var records = csv.GetRecords<ImportAssetRow>().ToList();
        int created = 0;
        var errors = new List<string>();

        foreach (var row in records)
        {
            if (string.IsNullOrWhiteSpace(row.AssetTag) || string.IsNullOrWhiteSpace(row.Name))
            {
                errors.Add($"Row {created + errors.Count + 1}: AssetTag and Name are required");
                continue;
            }
            _context.Assets.Add(new Asset { AssetTag = row.AssetTag, Name = row.Name, Serial = row.Serial, Notes = row.Notes });
            created++;
        }

        await _context.SaveChangesAsync();
        // Audit trail: single aggregated log per import (not one log per row) to avoid spam on bulk.
        _actionLogService.LogAction(
            itemType: ItemType.Asset,
            itemId: Guid.Empty,
            actionType: ActionType.Import,
            loggedByUserId: GetCurrentUserId(),
            companyId: await _companyScope.GetCurrentUserCompanyIdAsync(),
            note: $"Import {created} asset(s) từ file \"{file.FileName}\"",
            logMeta: System.Text.Json.JsonSerializer.Serialize(new { count = created, files = file.FileName }),
            fileName: file.FileName);
        // LogAction only stages the row in the change tracker → must SaveChanges to persist it.
        await _context.SaveChangesAsync();
        return Ok(new { status = "success", message = $"Imported {created} assets.", errors });
    }

    // === Export Assets (CSV) ===
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

        using var ms = new MemoryStream();
        using var writer = new StreamWriter(ms);
        using var csv = new CsvWriter(writer, new CsvConfiguration(CultureInfo.InvariantCulture));

        csv.WriteField("AssetTag"); csv.WriteField("Name"); csv.WriteField("Serial");
        csv.WriteField("Model"); csv.WriteField("Category"); csv.WriteField("Status");
        csv.WriteField("Location"); csv.WriteField("PurchaseCost"); csv.WriteField("PurchaseDate");
        csv.NextRecord();

        foreach (var a in assets)
        {
            csv.WriteField(a.AssetTag); csv.WriteField(a.Name); csv.WriteField(a.Serial);
            csv.WriteField(a.Model?.Name); csv.WriteField(a.Model?.Category?.Name);
            csv.WriteField(a.Status.ToString()); csv.WriteField(a.Location?.Name);
            csv.WriteField(a.PurchaseCost?.ToString()); csv.WriteField(a.PurchaseDate?.ToString("o"));
            csv.NextRecord();
        }

        writer.Flush();
        return File(ms.ToArray(), "text/csv", $"assets-export-{DateTime.UtcNow:yyyyMMdd}.csv");
    }

    // === Export Consumables (CSV) ===
    [HttpGet("export/consumables")]
    [Authorize(Policy = "consumables.view")]
    public async Task<IActionResult> ExportConsumables()
    {
        var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        var items = await _context.Consumables.Include(c => c.Checkouts).AsNoTracking()
            .Where(c => userCompanyId == null || c.CompanyId == null || c.CompanyId == userCompanyId.Value)
            .Take(1000).ToListAsync();
        using var ms = new MemoryStream();
        using var writer = new StreamWriter(ms);
        using var csv = new CsvWriter(writer, new CsvConfiguration(CultureInfo.InvariantCulture));

        csv.WriteField("Name"); csv.WriteField("ItemNo"); csv.WriteField("Qty"); csv.WriteField("MinAmt"); csv.WriteField("Remaining");
        csv.NextRecord();

        foreach (var c in items)
        {
            csv.WriteField(c.Name); csv.WriteField(c.ItemNo); csv.WriteField(c.Qty); csv.WriteField(c.MinAmt);
            csv.WriteField((c.Qty - c.Checkouts.Sum(ch => ch.Quantity)).ToString());
            csv.NextRecord();
        }

        writer.Flush();
        return File(ms.ToArray(), "text/csv", $"consumables-export-{DateTime.UtcNow:yyyyMMdd}.csv");
    }

    // === Import Template ===
    [HttpGet("import/templates/assets")]
    [Authorize(Policy = "import")]
    public IActionResult DownloadTemplate()
    {
        var csv = "AssetTag,Name,Serial,Model,Category,Status,Location,PurchaseCost,PurchaseDate,Notes\n";
        var bytes = System.Text.Encoding.UTF8.GetBytes(csv);
        return File(bytes, "text/csv", "asset-import-template.csv");
    }

    private Guid GetCurrentUserId()
    {
        // JIT provisioning stamps the local DB user id as "local_user_id" (Keycloak sub ≠ local id).
        if (Guid.TryParse(User.FindFirstValue("local_user_id"), out var local)) return local;
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(sub, out var id) ? id : Guid.Empty;
    }
}

// CSV row mapping
public record ImportAssetRow
{
    public string AssetTag { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Serial { get; set; }
    public string? Notes { get; set; }
}