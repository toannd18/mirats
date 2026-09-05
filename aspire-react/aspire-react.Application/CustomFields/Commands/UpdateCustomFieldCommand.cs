using System.Text.Json;
using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.CustomFields.Commands;

/// <summary>
/// [Giai đoạn 3] PUT /api/v1/custom-fields/{id} (extracted from CustomFieldsController.Update).
/// [BUG-I FIX 2026-09-05] Three behavior changes approved (see BACKLOG):
/// (1) PATCH-SAFE (Task M1/M2 pattern — was FULL-PUT ×8): ALL 8 fields nullable, assigned ONLY
///     when sent (`is not null` / non-whitespace for Name/Slug); absent fields no longer clear
///     stored values (Name=null previously → DB NOT NULL violation → 500).
/// (2) Dup-Slug check ADDED (was missing → rename onto an existing slug hit the DB unique index
///     → raw 500, CONFIRMED via reproduction): only when the slug actually CHANGES, excluding
///     self — "A field with this slug already exists." (400, no error_code, same as Create).
/// (3) Blank Name/Slug WHEN SENT → 400 ("Field name is required." / "Field slug is required.").
/// ILoggableCommand (thin log with 8-field changes-snapshot) — NO cache marker.
/// </summary>
public record UpdateCustomFieldCommand(
    Guid Id,
    string? Name,
    string? Slug,
    string? Format,
    string? Element,
    string? FieldValues,
    bool? FieldEncrypted,
    string? HelpText,
    bool? IsUnique,
    Guid CurrentUserId)
    : IRequest<CustomFieldResult>, ILoggableCommand<CustomFieldResult>
{
    public ActionLogEntry? BuildLogEntry(CustomFieldResult response)
    {
        if (!response.Success)
            return null;

        return new ActionLogEntry
        {
            ItemType = ItemType.CustomField,
            ItemId = Id,
            ActionType = ActionType.Update,
            CreatedBy = CurrentUserId,
            CompanyId = null,
            LogMeta = response.LogMeta,
            Note = response.Note
        };
    }
}

public class UpdateCustomFieldCommandHandler : IRequestHandler<UpdateCustomFieldCommand, CustomFieldResult>
{
    private readonly IApplicationDbContext _context;

    public UpdateCustomFieldCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task<CustomFieldResult> Handle(UpdateCustomFieldCommand request, CancellationToken cancellationToken)
    {
        var field = await _context.CustomFields.FindAsync(request.Id);
        if (field == null)
            return new CustomFieldResult(false, "Custom field not found.", "NOT_FOUND");

        // [BUG-I FIX] (3) blank-when-SENT checks, (2) dup-Slug only when the slug actually
        // changes (exclude self — the check that replaces the raw 500), then (1) patch-safe
        // assignment: absent → keep current.
        if (request.Name != null && string.IsNullOrWhiteSpace(request.Name))
            return new CustomFieldResult(false, "Field name is required.");
        if (request.Slug != null && string.IsNullOrWhiteSpace(request.Slug))
            return new CustomFieldResult(false, "Field slug is required.");
        if (request.Slug != null && request.Slug != field.Slug)
        {
            var slugExists = await _context.CustomFields.AnyAsync(f => f.Slug == request.Slug && f.Id != request.Id, cancellationToken);
            if (slugExists)
                return new CustomFieldResult(false, "A field with this slug already exists.");
        }

        var before = new { field.Name, field.Slug, field.Format, field.Element, field.FieldValues, field.FieldEncrypted, field.HelpText, field.IsUnique };
        if (request.Name != null) field.Name = request.Name;
        if (request.Slug != null) field.Slug = request.Slug;
        if (request.Format != null) field.Format = request.Format;
        if (request.Element is not null) field.Element = request.Element;
        if (request.FieldValues is not null) field.FieldValues = request.FieldValues;
        if (request.FieldEncrypted.HasValue) field.FieldEncrypted = request.FieldEncrypted.Value;
        if (request.HelpText is not null) field.HelpText = request.HelpText;
        if (request.IsUnique.HasValue) field.IsUnique = request.IsUnique.Value;
        await _context.SaveChangesAsync(cancellationToken);

        var logMeta = JsonSerializer.Serialize(new
        {
            changes = new
            {
                name = new { old = before.Name, @new = field.Name },
                slug = new { old = before.Slug, @new = field.Slug },
                format = new { old = before.Format, @new = field.Format },
                element = new { old = before.Element, @new = field.Element },
                fieldValues = new { old = before.FieldValues, @new = field.FieldValues },
                fieldEncrypted = new { old = before.FieldEncrypted, @new = field.FieldEncrypted },
                helpText = new { old = before.HelpText, @new = field.HelpText },
                isUnique = new { old = before.IsUnique, @new = field.IsUnique }
            }
        });

        return new CustomFieldResult(
            true, "Custom field updated.",
            CustomFieldId: field.Id, Name: field.Name,
            LogMeta: logMeta, Note: $"Cập nhật trường tùy chỉnh \"{field.Name}\"");
    }
}
