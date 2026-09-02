using aspire_react.Server.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Web.Controllers;

[ApiController, Route("api/v1"), Authorize]
public class AdminController : ControllerBase
{
    private readonly AppDbContext _context;
    public AdminController(AppDbContext context)
    {
        _context = context;
    }

    // === Models ===
    // [Giai đoạn 2] Model CRUD extracted to ModelsController (standalone, MediatR) —
    // routes unchanged: /api/v1/models... (CreateModel TODO BUG-H: no validation, BACKLOG.md).

    // === Categories ===
    // [Giai đoạn 2] Category CRUD extracted to CategoriesController (standalone, MediatR) —
    // routes unchanged: /api/v1/categories... See docs/MEDIATR_MIGRATION_PLAYBOOK.md §6.

    // === Manufacturers ===
    // [Giai đoạn 2] Manufacturer CRUD extracted to ManufacturersController (standalone, MediatR) —
    // routes unchanged: /api/v1/manufacturers...

    // === Suppliers ===
    // [Giai đoạn 2] Supplier CRUD extracted to SuppliersController (standalone, MediatR) —
    // routes unchanged: /api/v1/suppliers...

    // === Locations ===
    // [Giai đoạn 2] Location CRUD extracted to LocationsController (standalone, MediatR) —
    // routes unchanged: /api/v1/locations... Create's missing company-scoping = BUG-G (BACKLOG.md).

    // === Status Labels ===
    // [Giai đoạn 2-cleanup] REMOVED — StatusLabels feature deleted entirely (entity + table +
    // this GET endpoint). Audit 2026-09-01: 0 rows, 0 FK, 0 frontend usage, 0 business logic.
    // Asset status = AssetStatus enum (unrelated system), untouched.

    // === Depreciations ===
    // T-CLEAN1: trước đây chỉ [Authorize] trần (review #33 BACKEND_ARCHITECTURE_REVIEW_2026-08-15) —
    // mọi user đăng nhập đều đọc được. Siết về policy chuẩn như các master-data khác.
    [HttpGet("depreciations"), Authorize(Policy = "depreciations.view")]
    public async Task<IActionResult> GetDepreciations()
    {
        var list = await _context.Depreciations.AsNoTracking().OrderBy(d => d.Name).ToListAsync();
        return Ok(new { status = "success", data = list });
    }
}
