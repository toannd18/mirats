using System.Text.Json;
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
[Route("api/v1/licenses")]
public class LicensesController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly ICompanyScopeService _companyScope;
    private readonly IActionLogService _actionLogService;

    public LicensesController(AppDbContext context, ICurrentUserService currentUserService, ICompanyScopeService companyScope, IActionLogService actionLogService)
    {
        _context = context;
        _currentUserService = currentUserService;
        _companyScope = companyScope;
        _actionLogService = actionLogService;
    }

    private Guid GetCurrentUserId() => _currentUserService.GetLocalUserId();

    /// <summary>Regular users get their CompanyId (null = Superuser/sees all).</summary>
    private Task<Guid?> GetUserCompanyIdAsync() => _companyScope.GetCurrentUserCompanyIdAsync();

    /// <summary>Company-scoping: regular users only see licenses of their own company; floater
    /// licenses (no company) are visible to everyone — same convention as the rest of the system.
    /// 404 (hide existence) for out-of-scope licenses.</summary>
    private bool IsLicenseVisible(License l, Guid? userCompanyId)
        => userCompanyId == null || l.CompanyId == null || l.CompanyId == userCompanyId.Value;

    private static int CountTargets(LicenseSeat s)
        => (s.UserId != null ? 1 : 0) + (s.AssetId != null ? 1 : 0) + (s.SystemInfoId != null ? 1 : 0);

    // ==================== LIST ====================

    [HttpGet]
    [Authorize(Policy = "licenses.view")]
    public async Task<IActionResult> GetLicenses([FromQuery] string? search, [FromQuery] Guid? categoryId,
        [FromQuery] Guid? companyId, [FromQuery] bool expiringSoon = false, [FromQuery] bool lowSeats = false,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var userCompanyId = await GetUserCompanyIdAsync();
        var query = _context.Licenses.AsNoTracking()
            .Where(l => l.DeletedAt == null)
            .Where(l => userCompanyId == null || l.CompanyId == null || l.CompanyId == userCompanyId.Value);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.ToLower();
            query = query.Where(l => l.Name.ToLower().Contains(s) || (l.Serial != null && l.Serial.ToLower().Contains(s)));
        }
        if (categoryId.HasValue) query = query.Where(l => l.CategoryId == categoryId);
        if (companyId.HasValue) query = query.Where(l => l.CompanyId == companyId);

        var now = DateTime.UtcNow;
        var soon = now.AddDays(30);

        var total = await query.CountAsync();
        var items = await query
            .Include(l => l.LicenseSeats)
            .Include(l => l.Category)
            .Include(l => l.Company)
            .Include(l => l.Supplier)
            .Include(l => l.Manufacturer)
            .OrderBy(l => l.Name)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(l => new
            {
                l.Id, l.Name, l.Serial, l.Notes, l.Seats, l.Reassignable, l.ExpirationDate, l.TerminationDate, l.MinSeats,
                AssignedSeats = l.LicenseSeats.Count(s => s.UserId != null || s.AssetId != null || s.SystemInfoId != null),
                AvailableSeats = l.Seats - l.LicenseSeats.Count(s => s.UserId != null || s.AssetId != null || s.SystemInfoId != null),
                ExpiringSoon = l.ExpirationDate != null && l.ExpirationDate <= soon && l.ExpirationDate > now,
                IsExpired = l.ExpirationDate != null && l.ExpirationDate < now,
                IsLowSeats = l.MinSeats != null && (l.Seats - l.LicenseSeats.Count(s => s.UserId != null || s.AssetId != null || s.SystemInfoId != null)) <= l.MinSeats.Value,
                Category = l.Category == null ? null : new { l.Category.Id, l.Category.Name },
                Company = l.Company == null ? null : new { l.Company.Id, l.Company.Name },
                Supplier = l.Supplier == null ? null : new { l.Supplier.Id, l.Supplier.Name },
                Manufacturer = l.Manufacturer == null ? null : new { l.Manufacturer.Id, l.Manufacturer.Name }
            })
            .ToListAsync();

        if (expiringSoon) items = items.Where(i => i.ExpiringSoon || i.IsExpired).ToList();
        if (lowSeats) items = items.Where(i => i.IsLowSeats).ToList();

        return Ok(new { status = "success", data = items, pagination = new { page, pageSize, totalItems = total, totalPages = (int)Math.Ceiling((double)total / pageSize), hasNextPage = page * pageSize < total, hasPreviousPage = page > 1 } });
    }

    // ==================== DETAIL + SEATS ====================

    [HttpGet("{id:guid}")]
    [Authorize(Policy = "licenses.view")]
    public async Task<IActionResult> GetLicense(Guid id)
    {
        var l = await _context.Licenses
            .Include(x => x.Category).Include(x => x.Company).Include(x => x.Supplier).Include(x => x.Manufacturer)
            .AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.DeletedAt == null);
        if (l == null) return NotFound(new { status = "error", message = "License not found." });
        var userCompanyId = await GetUserCompanyIdAsync();
        if (!IsLicenseVisible(l, userCompanyId)) return NotFound(new { status = "error", message = "License not found." });

        var seats = await _context.LicenseSeats.AsNoTracking()
            .Include(s => s.User).Include(s => s.Asset).Include(s => s.SystemInfo)
            .Where(s => s.LicenseId == id).OrderBy(s => s.SeatNumber).ToListAsync();

        var assigned = seats.Count(s => CountTargets(s) > 0);
        return Ok(new { status = "success", data = new {
            l.Id, l.Name, l.Serial, l.Seats, l.Reassignable, l.ExpirationDate, l.TerminationDate,
            l.PurchaseCost, l.PurchaseDate, l.OrderNumber, l.MinSeats, l.Notes,
            l.SupplierId, l.ManufacturerId, l.CategoryId, l.CompanyId,
            AssignedSeats = assigned, AvailableSeats = l.Seats - assigned,
            Category = l.Category == null ? null : new { l.Category.Id, l.Category.Name },
            Company = l.Company == null ? null : new { l.Company.Id, l.Company.Name },
            Supplier = l.Supplier == null ? null : new { l.Supplier.Id, l.Supplier.Name },
            Manufacturer = l.Manufacturer == null ? null : new { l.Manufacturer.Id, l.Manufacturer.Name },
            SeatDetails = ProjectSeats(seats)
        }});
    }

    /// <summary>Seat list — used by the License detail modal and the SystemDetailPage License tab.</summary>
    [HttpGet("{id:guid}/seats")]
    [Authorize(Policy = "licenses.view")]
    public async Task<IActionResult> GetSeats(Guid id)
    {
        var l = await _context.Licenses.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.DeletedAt == null);
        if (l == null) return NotFound(new { status = "error", message = "License not found." });
        var userCompanyId = await GetUserCompanyIdAsync();
        if (!IsLicenseVisible(l, userCompanyId)) return NotFound(new { status = "error", message = "License not found." });

        var seats = await _context.LicenseSeats.AsNoTracking()
            .Include(s => s.User).Include(s => s.Asset).Include(s => s.SystemInfo)
            .Where(s => s.LicenseId == id).OrderBy(s => s.SeatNumber).ToListAsync();
        return Ok(new { status = "success", data = ProjectSeats(seats) });
    }

    private static object ProjectSeats(List<LicenseSeat> seats) => seats.Select(s => new
    {
        s.Id, s.SeatNumber,
        Assigned = CountTargets(s) > 0,
        TargetType = s.UserId != null ? "User" : s.AssetId != null ? "Asset" : s.SystemInfoId != null ? "SystemInfo" : (string?)null,
        User = s.User == null ? null : new { s.User.Id, Name = (s.User.FirstName + " " + s.User.LastName).Trim() != "" ? (s.User.FirstName + " " + s.User.LastName).Trim() : s.User.Username },
        Asset = s.Asset == null ? null : new { s.Asset.Id, s.Asset.AssetTag, s.Asset.Name },
        SystemInfo = s.SystemInfo == null ? null : new { s.SystemInfo.Id, s.SystemInfo.Code, s.SystemInfo.Name },
        s.Note, s.AssignedAt
    }).ToList();



    /// <summary>Licenses whose seat is currently checked out to the given User.</summary>
    [HttpGet("for-user/{userId:guid}")]
    [Authorize(Policy = "licenses.view")]
    public async Task<IActionResult> GetLicensesForUser(Guid userId)
    {
        var userCompanyId = await GetUserCompanyIdAsync();
        var now = DateTime.UtcNow;
        var soon = now.AddDays(30);
        var seats = await _context.LicenseSeats.AsNoTracking()
            .Include(s => s.License).Include(s => s.License.Company)
            .Where(s => s.UserId == userId && s.License.DeletedAt == null)
            .Where(s => userCompanyId == null || s.License.CompanyId == null || s.License.CompanyId == userCompanyId.Value)
            .Select(s => new
            {
                LicenseId = s.License.Id,
                LicenseName = s.License.Name,
                Serial = s.License.Serial,
                s.SeatNumber,
                s.AssignedAt,
                s.Note,
                ExpirationDate = s.License.ExpirationDate,
                ExpiringSoon = s.License.ExpirationDate != null && s.License.ExpirationDate <= soon && s.License.ExpirationDate > now,
                IsExpired = s.License.ExpirationDate != null && s.License.ExpirationDate < now,
                Company = s.License.Company == null ? null : new { s.License.Company.Id, s.License.Company.Name }
            })
            .ToListAsync();
        return Ok(new { status = "success", data = seats });
    }
    // ==================== ASSET / SYSTEM LICENSE LISTS (2-way links) ====================

    /// <summary>Licenses whose seat is currently checked out to the given Asset.</summary>
    [HttpGet("for-asset/{assetId:guid}")]
    [Authorize(Policy = "licenses.view")]
    public async Task<IActionResult> GetLicensesForAsset(Guid assetId)
    {
        var userCompanyId = await GetUserCompanyIdAsync();
        var now = DateTime.UtcNow;
        var soon = now.AddDays(30);
        var seats = await _context.LicenseSeats.AsNoTracking()
            .Include(s => s.License).Include(s => s.License.Company)
            .Where(s => s.AssetId == assetId && s.License.DeletedAt == null)
            .Where(s => userCompanyId == null || s.License.CompanyId == null || s.License.CompanyId == userCompanyId.Value)
            .Select(s => new
            {
                LicenseId = s.License.Id,
                LicenseName = s.License.Name,
                Serial = s.License.Serial,
                s.SeatNumber,
                s.AssignedAt,
                s.Note,
                ExpirationDate = s.License.ExpirationDate,
                ExpiringSoon = s.License.ExpirationDate != null && s.License.ExpirationDate <= soon && s.License.ExpirationDate > now,
                IsExpired = s.License.ExpirationDate != null && s.License.ExpirationDate < now,
                Company = s.License.Company == null ? null : new { s.License.Company.Id, s.License.Company.Name }
            })
            .ToListAsync();
        return Ok(new { status = "success", data = seats });
    }

    /// <summary>Licenses whose seat is currently checked out to the given SystemInfo (the "Hệ thống"
    /// target is the SystemInfo PARENT — a license applies to the whole system).</summary>
    [HttpGet("for-system/{systemInfoId:guid}")]
    [Authorize(Policy = "licenses.view")]
    public async Task<IActionResult> GetLicensesForSystem(Guid systemInfoId)
    {
        var userCompanyId = await GetUserCompanyIdAsync();
        var now = DateTime.UtcNow;
        var soon = now.AddDays(30);
        var seats = await _context.LicenseSeats.AsNoTracking()
            .Include(s => s.License).Include(s => s.License.Company)
            .Where(s => s.SystemInfoId == systemInfoId && s.License.DeletedAt == null)
            .Where(s => userCompanyId == null || s.License.CompanyId == null || s.License.CompanyId == userCompanyId.Value)
            .Select(s => new
            {
                LicenseId = s.License.Id,
                LicenseName = s.License.Name,
                Serial = s.License.Serial,
                s.SeatNumber,
                s.AssignedAt,
                s.Note,
                ExpirationDate = s.License.ExpirationDate,
                ExpiringSoon = s.License.ExpirationDate != null && s.License.ExpirationDate <= soon && s.License.ExpirationDate > now,
                IsExpired = s.License.ExpirationDate != null && s.License.ExpirationDate < now,
                Company = s.License.Company == null ? null : new { s.License.Company.Id, s.License.Company.Name }
            })
            .ToListAsync();
        return Ok(new { status = "success", data = seats });
    }
    // ==================== CREATE ====================

    [HttpPost]
    [Authorize(Policy = "licenses.create")]
    public async Task<IActionResult> Create([FromBody] CreateLicenseRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.Name))
            return BadRequest(new { status = "error", message = "Tên license là bắt buộc.", error_code = "NAME_REQUIRED" });
        if (r.Seats < 1)
            return BadRequest(new { status = "error", message = "Số chỗ (Seats) phải từ 1 trở lên.", error_code = "SEATS_MIN_1" });
        if (!r.CategoryId.HasValue)
            return BadRequest(new { status = "error", message = "Danh mục là bắt buộc.", error_code = "CATEGORY_REQUIRED" });
        var category = await _context.Categories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == r.CategoryId.Value);
        if (category == null || category.CategoryType != CategoryType.License)
            return BadRequest(new { status = "error", message = "Danh mục không hợp lệ (phải thuộc loại License).", error_code = "CATEGORY_INVALID" });

        // Regular users are forced to their own company; Superuser picks the company explicitly.
        var userCompanyId = await GetUserCompanyIdAsync();
        var companyId = userCompanyId ?? r.CompanyId;

        var l = new License
        {
            Name = r.Name.Trim(), Serial = r.Serial, Seats = r.Seats, Reassignable = r.Reassignable ?? true,
            ExpirationDate = r.ExpirationDate,
            TerminationDate = r.TerminationDate.HasValue ? DateTime.SpecifyKind(r.TerminationDate.Value, DateTimeKind.Unspecified) : null,
            PurchaseCost = r.PurchaseCost, PurchaseDate = r.PurchaseDate, OrderNumber = r.OrderNumber,
            Notes = r.Notes, MinSeats = r.MinSeats, SupplierId = r.SupplierId, ManufacturerId = r.ManufacturerId,
            CategoryId = r.CategoryId, CompanyId = companyId, UpdatedAt = DateTime.UtcNow
        };
        _context.Licenses.Add(l);
        for (var i = 1; i <= r.Seats; i++)
            _context.LicenseSeats.Add(new LicenseSeat { LicenseId = l.Id, SeatNumber = i });

        _actionLogService.Log(new ActionLogEntry
        {
            ItemType = ItemType.License,
            ItemId = l.Id,
            ActionType = ActionType.Create,
            CreatedBy = GetCurrentUserId(),
            CompanyId = l.CompanyId,
            Note = $"Tạo license \"{l.Name}\" ({r.Seats} seats)"
        });
        await _context.SaveChangesAsync();
        return Ok(new { status = "success", message = "License created.", data = new { l.Id, l.Name } });
    }

    // ==================== UPDATE (whitelist + seat sync) ====================

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "licenses.edit")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateLicenseRequest r)
    {
        var l = await _context.Licenses.Include(x => x.LicenseSeats).FirstOrDefaultAsync(x => x.Id == id && x.DeletedAt == null);
        if (l == null) return NotFound(new { status = "error", message = "License not found." });
        var userCompanyId = await GetUserCompanyIdAsync();
        if (!IsLicenseVisible(l, userCompanyId)) return NotFound(new { status = "error", message = "License not found." });

        // Locked structural fields (same whitelist principle as Component/Maintenance).
        if (r.CategoryId.HasValue && r.CategoryId.Value != l.CategoryId)
            return BadRequest(new { status = "error", message = "Không thể đổi danh mục sau khi tạo.", error_code = "FIELD_LOCKED" });
        if (r.CompanyId.HasValue && r.CompanyId != l.CompanyId)
            return BadRequest(new { status = "error", message = "Không thể đổi công ty sau khi tạo.", error_code = "FIELD_LOCKED" });

        // Seat count sync: increase → generate new seats; decrease → only if enough free seats.
        if (r.Seats.HasValue && r.Seats.Value != l.Seats)
        {
            if (r.Seats.Value < l.Seats)
            {
                var free = l.LicenseSeats.Count(s => CountTargets(s) == 0);
                if (free < (l.Seats - r.Seats.Value))
                    return BadRequest(new { status = "error", message = "Không thể giảm số chỗ vì các chỗ đang được sử dụng.", error_code = "CANNOT_REDUCE_SEATS_IN_USE" });
                var toRemove = l.LicenseSeats.Where(s => CountTargets(s) == 0)
                    .OrderByDescending(s => s.SeatNumber).Take(l.Seats - r.Seats.Value).ToList();
                _context.LicenseSeats.RemoveRange(toRemove);
            }
            else
            {
                for (var i = l.Seats + 1; i <= r.Seats.Value; i++)
                    _context.LicenseSeats.Add(new LicenseSeat { LicenseId = l.Id, SeatNumber = i });
            }
            l.Seats = r.Seats.Value;
        }

        // ─── Patch semantics (Task M1, mirroring Task F Asset): only fields EXPLICITLY sent
        // (non-null / HasValue) are applied. A partial payload must NOT wipe the other fields.
        // CompanyId/CategoryId stay locked (rejected above); seats handled above.
        if (!string.IsNullOrWhiteSpace(r.Name)) l.Name = r.Name.Trim();
        l.Serial = r.Serial ?? l.Serial;
        if (r.Reassignable.HasValue) l.Reassignable = r.Reassignable.Value;
        if (r.ExpirationDate is not null) l.ExpirationDate = r.ExpirationDate;
        if (r.TerminationDate.HasValue) l.TerminationDate = DateTime.SpecifyKind(r.TerminationDate.Value, DateTimeKind.Unspecified);
        if (r.PurchaseCost is not null) l.PurchaseCost = r.PurchaseCost;
        if (r.PurchaseDate is not null) l.PurchaseDate = r.PurchaseDate;
        if (r.OrderNumber is not null) l.OrderNumber = r.OrderNumber;
        l.Notes = r.Notes ?? l.Notes;
        if (r.MinSeats.HasValue) l.MinSeats = r.MinSeats.Value;
        if (r.SupplierId is not null) l.SupplierId = r.SupplierId;
        if (r.ManufacturerId is not null) l.ManufacturerId = r.ManufacturerId;
        l.UpdatedAt = DateTime.UtcNow;

        _actionLogService.Log(new ActionLogEntry
        {
            ItemType = ItemType.License,
            ItemId = id,
            ActionType = ActionType.Update,
            CreatedBy = GetCurrentUserId(),
            CompanyId = l.CompanyId,
            Note = $"Cập nhật license \"{l.Name}\""
        });
        await _context.SaveChangesAsync();
        return Ok(new { status = "success", message = "License updated." });
    }

    // ==================== DELETE (guard) ====================

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "licenses.delete")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var l = await _context.Licenses.Include(x => x.LicenseSeats).FirstOrDefaultAsync(x => x.Id == id && x.DeletedAt == null);
        if (l == null) return NotFound(new { status = "error", message = "License not found." });
        var userCompanyId = await GetUserCompanyIdAsync();
        if (!IsLicenseVisible(l, userCompanyId)) return NotFound(new { status = "error", message = "License not found." });

        var assigned = l.LicenseSeats.Count(s => CountTargets(s) > 0);
        var anyCheckout = await _context.ActionLogs.AnyAsync(a => a.ItemType == ItemType.License && a.ItemId == id && a.ActionType == ActionType.Checkout);
        if (assigned > 0 || anyCheckout)
            return BadRequest(new { status = "error", message = "Không thể xóa license vì đã có seat được cấp phát.", error_code = "LICENSE_IN_USE" });

        l.DeletedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
        // l.UpdatedAt is `timestamp with time zone` (safe list) → keep Kind=UTC.
        l.UpdatedAt = DateTime.UtcNow;
        _actionLogService.Log(new ActionLogEntry
        {
            ItemType = ItemType.License,
            ItemId = id,
            ActionType = ActionType.Delete,
            CreatedBy = GetCurrentUserId(),
            CompanyId = l.CompanyId,
            Note = $"Xóa license \"{l.Name}\""
        });
        await _context.SaveChangesAsync();
        return Ok(new { status = "success", message = "License deleted." });
    }

    // ==================== CHECKOUT / CHECKIN ====================

    [HttpPost("{id:guid}/checkout")]
    [Authorize(Policy = "licenses.checkout")]
    public async Task<IActionResult> CheckoutSeat(Guid id, [FromBody] CheckoutLicenseSeatRequest r)
    {
        var l = await _context.Licenses.FirstOrDefaultAsync(x => x.Id == id && x.DeletedAt == null);
        if (l == null) return NotFound(new { status = "error", message = "License not found." });
        var userCompanyId = await GetUserCompanyIdAsync();
        if (!IsLicenseVisible(l, userCompanyId)) return NotFound(new { status = "error", message = "License not found." });

        Guid? userId = null, assetId = null, systemInfoId = null;
        string? systemInfoName = null;
        switch (r.TargetType)
        {
            case LicenseSeatTargetType.User:
                if (!r.TargetId.HasValue) return BadRequest(new { status = "error", message = "Cần chọn người dùng nhận.", error_code = "TARGET_REQUIRED" });
                var user = await _context.Users.AsNoTracking().Select(u => new { u.Id, u.CompanyId }).FirstOrDefaultAsync(u => u.Id == r.TargetId.Value);
                if (user == null) return BadRequest(new { status = "error", message = "Người dùng không tồn tại.", error_code = "TARGET_NOT_FOUND" });
                if (l.CompanyId.HasValue && user.CompanyId != l.CompanyId)
                    return BadRequest(new { status = "error", message = "Người dùng không thuộc cùng công ty với license.", error_code = "LICENSE_COMPANY_MISMATCH" });
                userId = user.Id;
                break;
            case LicenseSeatTargetType.Asset:
                if (!r.TargetId.HasValue) return BadRequest(new { status = "error", message = "Cần chọn tài sản nhận.", error_code = "TARGET_REQUIRED" });
                var asset = await _context.Assets.AsNoTracking().Select(a => new { a.Id, a.CompanyId }).FirstOrDefaultAsync(a => a.Id == r.TargetId.Value);
                if (asset == null) return BadRequest(new { status = "error", message = "Tài sản không tồn tại.", error_code = "TARGET_NOT_FOUND" });
                if (l.CompanyId.HasValue && asset.CompanyId != l.CompanyId)
                    return BadRequest(new { status = "error", message = "Tài sản không thuộc cùng công ty với license.", error_code = "LICENSE_COMPANY_MISMATCH" });
                assetId = asset.Id;
                break;
            case LicenseSeatTargetType.SystemInfo:
                if (!r.TargetId.HasValue) return BadRequest(new { status = "error", message = "Cần chọn hệ thống nhận.", error_code = "TARGET_REQUIRED" });
                var sys = await _context.SystemInfos.AsNoTracking()
                    .Select(si => new { si.Id, si.CompanyId, si.Name })
                    .FirstOrDefaultAsync(si => si.Id == r.TargetId.Value);
                if (sys == null) return BadRequest(new { status = "error", message = "Hệ thống không tồn tại.", error_code = "TARGET_NOT_FOUND" });
                if (l.CompanyId.HasValue && sys.CompanyId != l.CompanyId)
                    return BadRequest(new { status = "error", message = "Hệ thống không thuộc cùng công ty với license.", error_code = "LICENSE_COMPANY_MISMATCH" });
                systemInfoId = sys.Id;
                systemInfoName = sys.Name;
                break;
            default:
                return BadRequest(new { status = "error", message = "Loại đối tượng nhận không hợp lệ.", error_code = "INVALID_TARGET_TYPE" });
        }

        // Exactly ONE of the three target kinds must be selected.
        var targetCount = (userId != null ? 1 : 0) + (assetId != null ? 1 : 0) + (systemInfoId != null ? 1 : 0);
        if (targetCount != 1)
            return BadRequest(new { status = "error", message = "Phải chọn đúng 1 đối tượng nhận (Người dùng, Tài sản hoặc Hệ thống).", error_code = "SEAT_TARGET_AMBIGUOUS" });

        // ──── Task O-FIX: serialize seat-picking under a row lock so two concurrent checkouts cannot
        // both "succeed" on the last free seat (previously both returned 200 but one silently overwrote
        // the other). The license row is locked FOR UPDATE as a mutex for seat allocation of this license.
        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync<IActionResult>(async () =>
        {
            await using var tx = await _context.Database.BeginTransactionAsync();

            var lockedLicense = _context.Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory"
                ? await _context.Licenses.FirstOrDefaultAsync(x => x.Id == id && x.DeletedAt == null)
                : await _context.Licenses.FromSqlRaw(
                    "SELECT * FROM licenses WHERE \"Id\" = {0} AND \"DeletedAt\" IS NULL FOR UPDATE", id)
                    .FirstOrDefaultAsync();
            if (lockedLicense == null)
            {
                await tx.RollbackAsync();
                return NotFound(new { status = "error", message = "License not found." });
            }

            // Pick a free seat (seatId optional → auto-pick the first free seat).
            LicenseSeat? seat;
            if (r.SeatId.HasValue)
            {
                seat = await _context.LicenseSeats.FirstOrDefaultAsync(s => s.Id == r.SeatId.Value && s.LicenseId == id);
                if (seat == null)
                {
                    await tx.RollbackAsync();
                    return NotFound(new { status = "error", message = "Seat not found.", error_code = "SEAT_NOT_FOUND" });
                }
                if (CountTargets(seat) > 0)
                {
                    await tx.RollbackAsync();
                    return BadRequest(new { status = "error", message = "Seat này đã được cấp phát.", error_code = "SEAT_ALREADY_ASSIGNED" });
                }
            }
            else
            {
                seat = await _context.LicenseSeats
                    .Where(s => s.LicenseId == id && s.UserId == null && s.AssetId == null && s.SystemInfoId == null)
                    .OrderBy(s => s.SeatNumber).FirstOrDefaultAsync();
                if (seat == null)
                {
                    await tx.RollbackAsync();
                    return BadRequest(new { status = "error", message = "Không còn chỗ trống trong license này.", error_code = "NO_AVAILABLE_SEATS" });
                }
            }

            seat.UserId = userId;
            seat.AssetId = assetId;
            seat.SystemInfoId = systemInfoId;
            seat.AssignedAt = DateTime.UtcNow;
            seat.Note = r.Note;
            seat.UpdatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);

            _actionLogService.Log(new ActionLogEntry
            {
                ItemType = ItemType.License,
                ItemId = id,
                ActionType = ActionType.Checkout,
                CreatedBy = GetCurrentUserId(),
                CompanyId = l.CompanyId,
                TargetType = r.TargetType switch
                {
                    LicenseSeatTargetType.User => AssignmentTargetType.User,
                    LicenseSeatTargetType.Asset => AssignmentTargetType.Asset,
                    _ => AssignmentTargetType.SystemInfo
                },
                TargetId = r.TargetId,
                TargetSystemInfoId = systemInfoId,
                TargetSystemInfoName = systemInfoName,
                Note = $"Cấp phát seat #{seat.SeatNumber} cho {TargetTypeLabel(r.TargetType)}"
            });

            await _context.SaveChangesAsync();
            await tx.CommitAsync();
            return Ok(new { status = "success", message = "Seat assigned.", data = new { seat.Id } });
        });
    }

    [HttpPost("{id:guid}/checkin")]
    [Authorize(Policy = "licenses.checkout")]
    public async Task<IActionResult> CheckinSeat(Guid id, [FromBody] CheckinLicenseSeatRequest r)
    {
        var l = await _context.Licenses.FirstOrDefaultAsync(x => x.Id == id && x.DeletedAt == null);
        if (l == null) return NotFound(new { status = "error", message = "License not found." });
        var userCompanyId = await GetUserCompanyIdAsync();
        if (!IsLicenseVisible(l, userCompanyId)) return NotFound(new { status = "error", message = "License not found." });

        var seat = await _context.LicenseSeats.FirstOrDefaultAsync(s => s.Id == r.SeatId && s.LicenseId == id);
        if (seat == null) return NotFound(new { status = "error", message = "Seat not found.", error_code = "SEAT_NOT_FOUND" });
        if (CountTargets(seat) == 0)
            return BadRequest(new { status = "error", message = "Seat này chưa được cấp phát.", error_code = "SEAT_NOT_ASSIGNED" });

        if (!l.Reassignable)
            return BadRequest(new { status = "error", message = "License không cho phép thu hồi để cấp lại (Reassignable = false).", error_code = "LICENSE_NOT_REASSIGNABLE" });

        seat.UserId = null; seat.AssetId = null; seat.SystemInfoId = null;
        seat.AssignedAt = null; seat.Note = null; seat.UpdatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);

        _actionLogService.Log(new ActionLogEntry
        {
            ItemType = ItemType.License,
            ItemId = id,
            ActionType = ActionType.Checkin,
            CreatedBy = GetCurrentUserId(),
            CompanyId = l.CompanyId,
            Note = $"Thu hồi seat #{seat.SeatNumber}"
        });
        await _context.SaveChangesAsync();
        return Ok(new { status = "success", message = "Seat checked in." });
    }

    // ==================== Legacy aliases (assign/remove) ====================

    [HttpPost("{id:guid}/assign")]
    [Authorize(Policy = "licenses.checkout")]
    public Task<IActionResult> AssignSeatLegacy(Guid id, [FromBody] AssignSeatRequest r)
    {
        if (r.UserId.HasValue && r.AssetId.HasValue)
            return Task.FromResult<IActionResult>(BadRequest(new { status = "error", message = "Phải chọn đúng 1 đối tượng nhận.", error_code = "SEAT_TARGET_AMBIGUOUS" }));
        if (!r.UserId.HasValue && !r.AssetId.HasValue)
            return Task.FromResult<IActionResult>(BadRequest(new { status = "error", message = "Cần chọn đối tượng nhận.", error_code = "TARGET_REQUIRED" }));
        var targetType = r.UserId.HasValue ? LicenseSeatTargetType.User : LicenseSeatTargetType.Asset;
        return CheckoutSeat(id, new CheckoutLicenseSeatRequest(r.SeatId, targetType, r.UserId ?? r.AssetId, r.Note));
    }

    [HttpPost("{id:guid}/remove")]
    [Authorize(Policy = "licenses.checkout")]
    public Task<IActionResult> RemoveSeatLegacy(Guid id, [FromBody] AssignSeatRequest r)
        => CheckinSeat(id, new CheckinLicenseSeatRequest(r.SeatId));

    private static string TargetTypeLabel(LicenseSeatTargetType t) => t switch
    {
        LicenseSeatTargetType.User => "người dùng",
        LicenseSeatTargetType.Asset => "tài sản",
        _ => "hệ thống"
    };
}

public record CreateLicenseRequest(string Name, string? Serial, int Seats, bool? Reassignable = null, DateTime? ExpirationDate = null,
    DateTime? TerminationDate = null, decimal? PurchaseCost = null, DateTime? PurchaseDate = null, string? OrderNumber = null,
    int? MinSeats = null, string? Notes = null, Guid? SupplierId = null, Guid? ManufacturerId = null, Guid? CategoryId = null, Guid? CompanyId = null);

public record UpdateLicenseRequest(string? Name = null, string? Serial = null, int? Seats = null, bool? Reassignable = null,
    DateTime? ExpirationDate = null, DateTime? TerminationDate = null, decimal? PurchaseCost = null, DateTime? PurchaseDate = null,
    string? OrderNumber = null, int? MinSeats = null, string? Notes = null, Guid? SupplierId = null, Guid? ManufacturerId = null,
    Guid? CategoryId = null, Guid? CompanyId = null);

public record CheckoutLicenseSeatRequest(Guid? SeatId, LicenseSeatTargetType TargetType, Guid? TargetId, string? Note = null);
public record CheckinLicenseSeatRequest(Guid SeatId);
public record AssignSeatRequest(Guid SeatId, Guid? AssetId, Guid? UserId, string? Note = null); // legacy