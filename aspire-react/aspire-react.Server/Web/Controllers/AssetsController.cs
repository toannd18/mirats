using System.Security.Claims;
using System.Text.Json;
using aspire_react.Server.Application.Assets.Commands;
using aspire_react.Server.Application.Assets.DTOs;
using aspire_react.Server.Application.Assets.Queries;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using aspire_react.Server.Infrastructure.Persistence;
using aspire_react.Server.Infrastructure.Services;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Web.Controllers;

[ApiController]
[Route("api/v1/assets")]
public class AssetsController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IMediator _mediator;
    private readonly ICurrentUserService _currentUserService;
    private readonly ICompanyScopeService _companyScope;

    public AssetsController(AppDbContext context, IMediator mediator, ICurrentUserService currentUserService, ICompanyScopeService companyScope)
    {
        _context = context;
        _mediator = mediator;
        _currentUserService = currentUserService;
        _companyScope = companyScope;
    }

    private Guid GetCurrentUserId() => _currentUserService.GetLocalUserId();

    private Task<Guid?> GetUserCompanyIdAsync() => _companyScope.GetCurrentUserCompanyIdAsync();

    // ==================== LIST ====================

    [HttpGet]
    [Authorize(Policy = "assets.view")]
    public async Task<IActionResult> GetAssets(
        [FromQuery] string? search, [FromQuery] AssetStatus? status, [FromQuery] Guid? categoryId,
        [FromQuery] Guid? locationId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var query = _context.Assets
            .Include(a => a.Model).ThenInclude(m => m != null ? m.Manufacturer : null)
            .Include(a => a.Model).ThenInclude(m => m != null ? m.Category : null)
            .Include(a => a.Location)
            .Include(a => a.Company)
            .Include(a => a.CurrentAssignment)
            .AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.ToLower();
            query = query.Where(a => a.AssetTag.ToLower().Contains(s) || a.Name.ToLower().Contains(s) || (a.Serial != null && a.Serial.ToLower().Contains(s)));
        }
        if (status.HasValue)
            query = query.Where(a => a.Status == status.Value);
        if (categoryId.HasValue) query = query.Where(a => a.Model != null && a.Model.CategoryId == categoryId);
        if (locationId.HasValue) query = query.Where(a => a.LocationId == locationId);

        var userCompanyId = await GetUserCompanyIdAsync();
        query = query.Where(a => userCompanyId == null || a.CompanyId == null || a.CompanyId == userCompanyId.Value);

        var total = await query.CountAsync();
        var assets = await query.OrderBy(a => a.AssetTag).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(a => new {
                a.Id, a.AssetTag, a.Name, a.Serial, a.Notes, a.PurchaseCost, a.PurchaseDate,
                Status = a.Status.ToString(), a.IsConfirmed, a.CheckoutCounter, a.CheckinCounter,
                a.LastCheckout, a.LastCheckin,
                Model = a.Model == null ? null : new { a.Model.Id, a.Model.Name },
                Category = a.Model == null || a.Model.Category == null ? null : new { a.Model.Category.Id, a.Model.Category.Name, a.Model.Category.TagColor },
                Manufacturer = a.Model == null || a.Model.Manufacturer == null ? null : new { a.Model.Manufacturer.Id, a.Model.Manufacturer.Name },
                Location = a.Location == null ? null : new { a.Location.Id, a.Location.Name },
                Company = a.Company == null ? null : new { a.Company.Id, a.Company.Name },
                AssignedTo = a.CurrentAssignment == null ? null : new {
                    type = a.CurrentAssignment.TargetType.ToString(),
                    targetId = a.CurrentAssignment.TargetId
                }
            }).ToListAsync();

        // ── Batch-resolve assigned-to target names ──
        var atAssets = assets.Where(a => a.AssignedTo != null).Select(a => a.AssignedTo!).ToList();
        var uDict = new Dictionary<Guid, string>(); var dDict = new Dictionary<Guid, string>(); var pDict = new Dictionary<Guid, string>();
        if (atAssets.Any()) {
            var uids = atAssets.Where(x => x.type == "User").Select(x => x.targetId).Distinct().ToList();
            var dids = atAssets.Where(x => x.type == "Department").Select(x => x.targetId).Distinct().ToList();
            var pids = atAssets.Where(x => x.type == "SystemPosition").Select(x => x.targetId).Distinct().ToList();
            if (uids.Any()) uDict = await _context.Users.Where(u => uids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => (u.FirstName + " " + u.LastName).Trim() != "" ? (u.FirstName + " " + u.LastName).Trim() : u.Username);
            if (dids.Any()) dDict = await _context.Departments.Where(d => dids.Contains(d.Id)).ToDictionaryAsync(d => d.Id, d => d.Name);
            if (pids.Any()) pDict = await _context.SystemPositions.Where(sp => pids.Contains(sp.Id)).ToDictionaryAsync(sp => sp.Id, sp => sp.Name);
        }
        var enriched = assets.Select(a => {
            string? an = null;
            if (a.AssignedTo != null) an = a.AssignedTo.type switch { "User" => uDict.GetValueOrDefault(a.AssignedTo.targetId), "Department" => dDict.GetValueOrDefault(a.AssignedTo.targetId), "SystemPosition" => pDict.GetValueOrDefault(a.AssignedTo.targetId), _ => null };
            return new { a.Id, a.AssetTag, a.Name, a.Serial, a.Notes, a.PurchaseCost, a.PurchaseDate, a.Status, a.IsConfirmed, a.CheckoutCounter, a.CheckinCounter, a.LastCheckout, a.LastCheckin, a.Model, a.Category, a.Manufacturer, a.Location, a.Company, AssignedTo = a.AssignedTo == null ? null : new { a.AssignedTo.type, a.AssignedTo.targetId, name = an } };
        }).ToList();

        return Ok(new { status = "success", data = enriched, pagination = new { page, pageSize, totalItems = total, totalPages = (int)Math.Ceiling((double)total / pageSize), hasNextPage = page * pageSize < total, hasPreviousPage = page > 1 } });
    }

    // ==================== BY ID ====================

    [HttpGet("{id:guid}")]
    [Authorize(Policy = "assets.view")]
    public async Task<IActionResult> GetAsset(Guid id)
    {
        var userCompanyId = await GetUserCompanyIdAsync();
        var asset = await _context.Assets
            .Include(a => a.Model).ThenInclude(m => m != null ? m.Manufacturer : null)
            .Include(a => a.Model).ThenInclude(m => m != null ? m.Category : null)
            .Include(a => a.Location).Include(a => a.Supplier).Include(a => a.Company)
            .Include(a => a.CurrentAssignment)
            .AsNoTracking().FirstOrDefaultAsync(a => a.Id == id);

        // Company scoping: a regular user may only view assets of their company (or company-less / floater).
        if (asset == null || (userCompanyId.HasValue && asset.CompanyId.HasValue && asset.CompanyId.Value != userCompanyId.Value))
            return NotFound(new { status = "error", message = "Asset not found." });

        object? assignedTo = null;
        if (asset.CurrentAssignment != null)
        {
            var asgn = asset.CurrentAssignment;
            assignedTo = asgn.TargetType switch
            {
                AssignmentTargetType.User => await _context.Users.AsNoTracking().Select(u => new { u.Id, Type = "user", u.Username, u.FirstName, u.LastName }).FirstOrDefaultAsync(u => u.Id == asgn.TargetId),
                AssignmentTargetType.Department => await _context.Departments.AsNoTracking().Select(d => new { d.Id, Type = "department", d.Name }).FirstOrDefaultAsync(d => d.Id == asgn.TargetId),
                AssignmentTargetType.SystemPosition => await _context.SystemPositions.AsNoTracking().Select(sp => new { sp.Id, Type = "systemPosition", sp.Name }).FirstOrDefaultAsync(sp => sp.Id == asgn.TargetId),
                _ => null
            };
        }

        return Ok(new { status = "success", data = new {
            asset.Id, asset.AssetTag, asset.Name, asset.Serial, asset.Image,
            asset.PurchaseCost, asset.PurchaseDate, asset.WarrantyMonths,
            asset.LastCheckout, asset.LastCheckin,
            asset.LastAuditDate, asset.NextAuditDate,
            asset.CheckinCounter, asset.CheckoutCounter, asset.RequestsCounter,
            Status = asset.Status.ToString(), asset.IsConfirmed,
            asset.Physical, asset.Requestable, asset.Accepted,
            asset.OrderNumber, asset.Notes,
            asset.CreatedAt, asset.UpdatedAt,
            Model = asset.Model == null ? null : new { asset.Model.Id, asset.Model.Name, asset.Model.ModelNumber },
            Category = asset.Model?.Category == null ? null : new { asset.Model.Category.Id, asset.Model.Category.Name, asset.Model.Category.TagColor },
            Manufacturer = asset.Model?.Manufacturer == null ? null : new { asset.Model.Manufacturer.Id, asset.Model.Manufacturer.Name },
            Location = asset.Location == null ? null : new { asset.Location.Id, asset.Location.Name },
            Supplier = asset.Supplier == null ? null : new { asset.Supplier.Id, asset.Supplier.Name },
            Company = asset.Company == null ? null : new { asset.Company.Id, asset.Company.Name },
            AssignedTo = assignedTo
        }});
    }

    // ==================== CREATE ====================

    [HttpPost]
    [Authorize(Policy = "assets.create")]
    public async Task<IActionResult> CreateAsset([FromBody] CreateAssetRequest r)
    {
        var userId = GetCurrentUserId();
        var result = await _mediator.Send(new CreateAssetCommand
        {
            AssetTag = r.AssetTag, Name = r.Name, Serial = r.Serial,
            ModelId = r.ModelId, LocationId = r.LocationId,
            SupplierId = r.SupplierId, CompanyId = r.CompanyId,
            PurchaseCost = r.PurchaseCost, PurchaseDate = r.PurchaseDate,
            WarrantyMonths = r.WarrantyMonths, OrderNumber = r.OrderNumber, Notes = r.Notes,
            Physical = r.Physical, Requestable = r.Requestable, CurrentUserId = userId
        });
        if (!result.Success) return BadRequest(new { status = "error", message = result.Message, error_code = result.ErrorCode });
        return CreatedAtAction(nameof(GetAsset), new { id = result.AssetId }, new { status = "success", message = result.Message, data = new { Id = result.AssetId } });
    }

    // ==================== UPDATE ====================

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "assets.edit")]
    public async Task<IActionResult> UpdateAsset(Guid id, [FromBody] UpdateAssetRequest r)
    {
        var userId = GetCurrentUserId();
        var result = await _mediator.Send(new UpdateAssetCommand
        {
            Id = id, AssetTag = r.AssetTag ?? string.Empty, Name = r.Name, Serial = r.Serial,
            Image = r.Image, ModelId = r.ModelId, LocationId = r.LocationId,
            SupplierId = r.SupplierId, CompanyId = r.CompanyId,
            PurchaseCost = r.PurchaseCost, PurchaseDate = r.PurchaseDate,
            WarrantyMonths = r.WarrantyMonths, OrderNumber = r.OrderNumber, Notes = r.Notes,
            Physical = r.Physical, Requestable = r.Requestable, CurrentUserId = userId
        });
        if (!result.Success) return BadRequest(new { status = "error", message = result.Message, error_code = result.ErrorCode });
        return Ok(new { status = "success", message = result.Message });
    }

    // ==================== DELETE ====================

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "assets.delete")]
    public async Task<IActionResult> DeleteAsset(Guid id)
    {
        var userId = GetCurrentUserId();
        var result = await _mediator.Send(new DeleteAssetCommand { AssetId = id, CurrentUserId = userId });
        if (!result.Success) return BadRequest(new { status = "error", message = result.Message, error_code = result.ErrorCode });
        return Ok(new { status = "success", message = result.Message });
    }

    // ==================== CONFIRM ====================

    [HttpPost("{id:guid}/confirm")]
    [Authorize(Policy = "assets.edit")]
    public async Task<IActionResult> Confirm(Guid id)
    {
        var userId = GetCurrentUserId();
        var result = await _mediator.Send(new ConfirmAssetCommand(id, userId));
        if (!result.Success) return BadRequest(new { status = "error", message = result.Message, error_code = result.ErrorCode });
        return Ok(new { status = "success", message = result.Message });
    }

    // ==================== CHECKOUT ====================

    [HttpPost("{id:guid}/checkout")]
    [Authorize(Policy = "assets.checkout")]
    public async Task<IActionResult> Checkout(Guid id, [FromBody] CheckoutRequestDto request)
    {
        var userId = GetCurrentUserId();
        var result = await _mediator.Send(new CheckoutAssetCommand(
            id, request.TargetType, request.TargetId, request.Note,
            request.CheckoutAt, request.LocationId, userId));
        if (!result.Success) return BadRequest(new { status = "error", message = result.Message, error_code = result.ErrorCode });
        return Ok(new { status = "success", message = result.Message, data = new { assignment_id = result.Assignment?.Id } });
    }

    // ==================== CHECKIN ====================

    [HttpPost("{id:guid}/checkin")]
    [Authorize(Policy = "assets.checkin")]
    public async Task<IActionResult> Checkin(Guid id, [FromBody] CheckinRequestDto request)
    {
        var userId = GetCurrentUserId();
        var result = await _mediator.Send(new CheckinAssetCommand(id, request.LocationId, request.Note, request.CheckinAt, userId));
        if (!result.Success) return BadRequest(new { status = "error", message = result.Message, error_code = result.ErrorCode });
        return Ok(new { status = "success", message = result.Message });
    }

    // ==================== ARCHIVE / UNARCHIVE ====================

    [HttpPost("{id:guid}/archive")]
    [Authorize(Policy = "assets.edit")]
    public async Task<IActionResult> Archive(Guid id, [FromBody] ArchiveUnarchiveRequestDto request)
    {
        var userId = GetCurrentUserId();
        var result = await _mediator.Send(new ArchiveAssetCommand(id, request.LocationId, userId, request.Note));
        if (!result.Success) return BadRequest(new { status = "error", message = result.Message, error_code = result.ErrorCode });
        return Ok(new { status = "success", message = result.Message });
    }

    [HttpPost("{id:guid}/unarchive")]
    [Authorize(Policy = "assets.edit")]
    public async Task<IActionResult> Unarchive(Guid id, [FromBody] ArchiveUnarchiveRequestDto request)
    {
        var userId = GetCurrentUserId();
        var result = await _mediator.Send(new UnarchiveAssetCommand(id, userId, request.Note));
        if (!result.Success) return BadRequest(new { status = "error", message = result.Message, error_code = result.ErrorCode });
        return Ok(new { status = "success", message = result.Message });
    }

    // ==================== HISTORY ====================

    [HttpGet("{id:guid}/history")]
    [Authorize(Policy = "assets.view")]
    public async Task<IActionResult> GetHistory(Guid id)
    {
        // [Task K] Company-scoping: history of an asset is only visible to users who can see the
        // asset itself (mirrors GetAsset). Out-of-scope asset → 404 to hide existence.
        var userCompanyId = await GetUserCompanyIdAsync();
        var visible = await _context.Assets.AsNoTracking()
            .AnyAsync(a => a.Id == id && (userCompanyId == null || a.CompanyId == null || a.CompanyId == userCompanyId.Value));
        if (!visible)
            return NotFound(new { status = "error", message = "Asset not found." });

        var logs = await _context.ActionLogs.Include(l => l.Creator).AsNoTracking()
            .Where(l => l.ItemType == ItemType.Asset && l.ItemId == id && l.DeletedAt == null)
            .OrderByDescending(l => l.ActionDate).Take(50)
            .Select(l => new { l.Id, l.ActionType, l.Note, l.LogMeta, l.ActionDate,
                l.LocationId, l.RemoteIp, l.ActionSource,
                Creator = new { l.Creator.Id, l.Creator.Username, l.Creator.FirstName, l.Creator.LastName } })
            .ToListAsync();
        return Ok(new { status = "success", data = logs });
    }

    // ==================== AUDIT ====================

    [HttpPost("{id:guid}/audit")]
    [Authorize(Policy = "assets.audit")]
    public async Task<IActionResult> Audit(Guid id, [FromBody] AuditRequestDto request)
    {
        var userId = GetCurrentUserId();
        var result = await _mediator.Send(new AuditAssetCommand(id, request.AuditDate, request.Note, userId));
        if (!result.Success) return BadRequest(new { status = "error", message = result.Message, error_code = result.ErrorCode });
        return Ok(new { status = "success", message = result.Message });
    }
}

// ==================== DTOs ====================

public record CreateAssetRequest(string AssetTag, string Name, string? Serial, Guid? ModelId,
    Guid? LocationId, Guid? SupplierId, Guid? CompanyId, decimal? PurchaseCost,
    DateTime? PurchaseDate, int? WarrantyMonths, string? OrderNumber, string? Notes,
    bool Physical = true, bool Requestable = false, string? Image = null);

public record UpdateAssetRequest(string? AssetTag, string Name, string? Serial, Guid? ModelId,
    Guid? LocationId, Guid? SupplierId, Guid? CompanyId, decimal? PurchaseCost,
    DateTime? PurchaseDate, int? WarrantyMonths, string? OrderNumber, string? Notes,
    bool? Physical = null, bool? Requestable = null, string? Image = null);

public record CheckoutRequestDto(AssignmentTargetType TargetType, Guid TargetId,
    string? Note = null, DateTime? CheckoutAt = null, Guid? LocationId = null);

public record CheckinRequestDto(Guid LocationId, string? Note = null, DateTime? CheckinAt = null);

public record ArchiveUnarchiveRequestDto(Guid LocationId, string? Note = null);

public record AuditRequestDto(DateTime? AuditDate = null, string? Note = null);