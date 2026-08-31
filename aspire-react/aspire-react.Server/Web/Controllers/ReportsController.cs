using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Interfaces;
using aspire_react.Server.Infrastructure.Persistence;
using aspire_react.Server.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Web.Controllers;

[ApiController]
[Route("api/v1/reports")]
public class ReportsController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly ICompanyScopeService _companyScope;
    private readonly IActionLogVisibilityService _actionLogVisibility;
    public ReportsController(AppDbContext context, ICompanyScopeService companyScope, IActionLogVisibilityService actionLogVisibility)
    {
        _context = context;
        _companyScope = companyScope;
        _actionLogVisibility = actionLogVisibility;
    }

    [HttpGet("custom")]
    [Authorize(Policy = "reports.view")]
    public async Task<IActionResult> CustomReport(
        [FromQuery] DateTime? startDate, [FromQuery] DateTime? endDate,
        [FromQuery] Guid? categoryId, [FromQuery] Guid? locationId,
        [FromQuery] AssetStatus? status, [FromQuery] string? groupBy)
    {
        var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        var query = _context.Assets.Include(a => a.Model).ThenInclude(m => m != null ? m.Category : null)
            .Include(a => a.Location).Include(a => a.Company)
            .AsNoTracking().AsQueryable();

        query = query.Where(a => userCompanyId == null || a.CompanyId == null || a.CompanyId == userCompanyId.Value);

        if (startDate.HasValue) query = query.Where(a => a.CreatedAt >= startDate.Value);
        if (endDate.HasValue) query = query.Where(a => a.CreatedAt <= endDate.Value);
        if (categoryId.HasValue) query = query.Where(a => a.Model != null && a.Model.CategoryId == categoryId);
        if (locationId.HasValue) query = query.Where(a => a.LocationId == locationId);
        if (status.HasValue) query = query.Where(a => a.Status == status.Value);

        var assets = await query.OrderBy(a => a.AssetTag).Take(500).Select(a => new
        {
            a.Id,
            a.AssetTag,
            a.Name,
            a.Serial,
            a.PurchaseCost,
            a.PurchaseDate,
            a.Status,
            Model = a.Model == null ? null : new { a.Model.Id, a.Model.Name },
            Category = a.Model != null && a.Model.Category != null ? new { a.Model.Category.Id, a.Model.Category.Name } : null,
            Location = a.Location == null ? null : new { a.Location.Id, a.Location.Name },
            Company = a.Company == null ? null : new { a.Company.Id, a.Company.Name },
            a.CreatedAt
        }).ToListAsync();

        return Ok(new { status = "success", data = assets, total = assets.Count });
    }

    [HttpGet("depreciation")]
    [Authorize(Policy = "reports.view")]
    public async Task<IActionResult> DepreciationReport()
    {
        var now = DateTime.UtcNow;
        var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        var assets = await _context.Assets
            .Include(a => a.Model).ThenInclude(m => m != null ? m.Depreciation : null)
            .AsNoTracking()
            .Where(a => (userCompanyId == null || a.CompanyId == null || a.CompanyId == userCompanyId.Value)
                        && a.Model != null && a.Model.Depreciation != null && a.PurchaseCost.HasValue && a.PurchaseDate.HasValue)
            .Take(200)
            .ToListAsync();

        var data = assets.Select(a =>
        {
            var months = a.Model!.Depreciation!.Months;
            var monthsUsed = (int)((now - a.PurchaseDate!.Value).TotalDays / 30.44);
            var monthlyDep = a.PurchaseCost!.Value / months;
            var bookValue = Math.Max(0, a.PurchaseCost.Value - (monthlyDep * Math.Min(monthsUsed, months)));
            return new
            {
                a.Id,
                a.AssetTag,
                a.Name,
                a.PurchaseCost,
                a.PurchaseDate,
                Model = a.Model.Name,
                Depreciation = a.Model.Depreciation.Name,
                MonthsTotal = months,
                MonthsUsed = Math.Min(monthsUsed, months),
                MonthsRemaining = Math.Max(0, months - Math.Min(monthsUsed, months)),
                CurrentBookValue = Math.Round(bookValue, 2)
            };
        }).OrderBy(a => a.AssetTag).ToList();

        return Ok(new { status = "success", data });
    }

    [HttpGet("audit")]
    [Authorize(Policy = "reports.view")]
    public async Task<IActionResult> AuditReport()
    {
        var now = DateTime.UtcNow;
        var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        var audited = await _context.Assets.AsNoTracking()
            .Where(a => (userCompanyId == null || a.CompanyId == null || a.CompanyId == userCompanyId.Value) && a.LastAuditDate != null)
            .CountAsync();
        var notAudited = await _context.Assets.AsNoTracking()
            .Where(a => (userCompanyId == null || a.CompanyId == null || a.CompanyId == userCompanyId.Value) && a.LastAuditDate == null)
            .CountAsync();
        var overdue = await _context.Assets.AsNoTracking()
                .Where(a => (userCompanyId == null || a.CompanyId == null || a.CompanyId == userCompanyId.Value)
                            && a.NextAuditDate != null && a.NextAuditDate < now && a.Status != AssetStatus.Archived)
                .CountAsync();

        return Ok(new
        {
            status = "success",
            data = new { totalAudited = audited, notAudited, overdueAudit = overdue }
        });
    }

    [HttpGet("checkout-history")]
    [Authorize(Policy = "reports.view")]
    public async Task<IActionResult> CheckoutHistory(
        [FromQuery] DateTime? startDate, [FromQuery] DateTime? endDate)
    {
        var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        var candidates = await _context.ActionLogs
            .Include(l => l.Creator)
            .AsNoTracking()
            .Where(l => l.ActionType == Domain.Enums.ActionType.Checkout || l.ActionType == Domain.Enums.ActionType.Checkin)
            .Where(l => !startDate.HasValue || l.ActionDate >= startDate.Value)
            .Where(l => !endDate.HasValue || l.ActionDate <= endDate.Value)
            .OrderByDescending(l => l.ActionDate)
            .Take(200)
            .ToListAsync();

        // Company scoping: a regular user may only see checkout/checkin history of items in their company.
        var visible = userCompanyId == null
            ? candidates
            : await _actionLogVisibility.FilterVisibleLogsAsync(candidates, userCompanyId.Value);

        var logs = visible
            .Select(l => new
            {
                l.Id,
                l.ItemType,
                l.ItemId,
                l.ActionType,
                l.Note,
                l.ActionDate,
                Creator = new { l.Creator.Id, l.Creator.Username, l.Creator.FirstName, l.Creator.LastName }
            }).ToList();

        return Ok(new { status = "success", data = logs });
    }
}