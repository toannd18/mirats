using System.Text.Json;
using System.Security.Claims;
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
[Route("api/v1/custom-fields")]
public class CustomFieldsController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IActionLogService _actionLogService;
    public CustomFieldsController(AppDbContext context, IActionLogService actionLogService)
    {
        _context = context;
        _actionLogService = actionLogService;
    }

    private Guid GetCurrentUserId()
    {
        // [SEC-FIX CLAIM-CLEANUP, 2026-08-23] ONLY "local_user_id" (JIT-stamped). Keycloak
        // sub/preferred_username are never a user identity source (bug-class 1). Absent → Guid.Empty.
        if (Guid.TryParse(User.FindFirstValue("local_user_id"), out var local)) return local;
        return Guid.Empty;
    }

    [HttpGet]
    [Authorize(Policy = "customfields.view")]
    public async Task<IActionResult> GetFields()
    {
        var fields = await _context.CustomFields.AsNoTracking().OrderBy(f => f.Name).ToListAsync();
        return Ok(new { status = "success", data = fields });
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = "customfields.view")]
    public async Task<IActionResult> GetField(Guid id)
    {
        var f = await _context.CustomFields.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (f == null) return NotFound(new { status = "error", message = "Custom field not found." });
        return Ok(new { status = "success", data = f });
    }

    [HttpPost]
    [Authorize(Policy = "customfields.create")]
    public async Task<IActionResult> Create([FromBody] CreateCustomFieldRequest r)
    {
        var exists = await _context.CustomFields.AnyAsync(f => f.Slug == r.Slug);
        if (exists) return BadRequest(new { status = "error", message = "A field with this slug already exists." });

        var field = new CustomField
        {
            Name = r.Name, Slug = r.Slug, Format = r.Format, Element = r.Element,
            FieldValues = r.FieldValues, FieldEncrypted = r.FieldEncrypted,
            HelpText = r.HelpText, IsUnique = r.IsUnique
        };
        _context.CustomFields.Add(field);
        await _context.SaveChangesAsync();
        _actionLogService.Log(new ActionLogEntry { ItemType = ItemType.CustomField, ItemId = field.Id, ActionType = ActionType.Create, CreatedBy = GetCurrentUserId(), CompanyId = null, Note = $"Tạo trường tùy chỉnh \"{field.Name}\"" });
        await _context.SaveChangesAsync();
        return CreatedAtAction(nameof(GetField), new { id = field.Id }, new { status = "success", message = "Custom field created.", data = new { field.Id, field.Name } });
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "customfields.edit")]
    public async Task<IActionResult> Update(Guid id, [FromBody] CreateCustomFieldRequest r)
    {
        var field = await _context.CustomFields.FindAsync(id);
        if (field == null) return NotFound(new { status = "error", message = "Custom field not found." });
        var before = new { field.Name, field.Slug, field.Format, field.Element, field.FieldValues, field.FieldEncrypted, field.HelpText, field.IsUnique };
        field.Name = r.Name; field.Slug = r.Slug; field.Format = r.Format; field.Element = r.Element;
        field.FieldValues = r.FieldValues; field.FieldEncrypted = r.FieldEncrypted;
        field.HelpText = r.HelpText; field.IsUnique = r.IsUnique;
        await _context.SaveChangesAsync();
        _actionLogService.Log(new ActionLogEntry { ItemType = ItemType.CustomField, ItemId = id, ActionType = ActionType.Update, CreatedBy = GetCurrentUserId(), CompanyId = null,
            LogMeta = JsonSerializer.Serialize(new { changes = new { name = new { old = before.Name, @new = field.Name }, slug = new { old = before.Slug, @new = field.Slug }, format = new { old = before.Format, @new = field.Format }, element = new { old = before.Element, @new = field.Element }, fieldValues = new { old = before.FieldValues, @new = field.FieldValues }, fieldEncrypted = new { old = before.FieldEncrypted, @new = field.FieldEncrypted }, helpText = new { old = before.HelpText, @new = field.HelpText }, isUnique = new { old = before.IsUnique, @new = field.IsUnique } } }), Note = $"Cập nhật trường tùy chỉnh \"{field.Name}\"" });
        await _context.SaveChangesAsync();
        return Ok(new { status = "success", message = "Custom field updated." });
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "customfields.delete")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var field = await _context.CustomFields.FindAsync(id);
        if (field == null) return NotFound(new { status = "error", message = "Custom field not found." });

        // Delete guard (ST6a): a field still linked to any fieldset cannot be deleted — its
        // CustomFieldFieldset pivot rows (FieldId → CustomField, OnDelete(Cascade)) would be
        // silently cascade-deleted, destroying the field↔fieldset relationship history.
        if (await _context.CustomFieldFieldsets.AnyAsync(cf => cf.FieldId == id))
            return BadRequest(new { status = "error", message = "Trường tùy chỉnh đang được fieldset sử dụng — không thể xóa.", error_code = "CUSTOM_FIELD_IN_USE" });

        var fName = field.Name;
        _context.CustomFields.Remove(field);
        await _context.SaveChangesAsync();
        _actionLogService.Log(new ActionLogEntry { ItemType = ItemType.CustomField, ItemId = id, ActionType = ActionType.Delete, CreatedBy = GetCurrentUserId(), CompanyId = null, Note = $"Xóa trường tùy chỉnh \"{fName}\"" });
        await _context.SaveChangesAsync();
        return Ok(new { status = "success", message = "Custom field deleted." });
    }
}

public record CreateCustomFieldRequest(string Name, string Slug, string Format, string? Element,
    string? FieldValues, bool FieldEncrypted, string? HelpText, bool IsUnique);