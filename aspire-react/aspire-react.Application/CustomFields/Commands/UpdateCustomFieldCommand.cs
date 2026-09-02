using System.Text.Json;
using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using MediatR;

namespace aspire_react.Server.Application.CustomFields.Commands;

/// <summary>
/// [Giai đoạn 3] PUT /api/v1/custom-fields/{id} (extracted from CustomFieldsController.Update).
/// ⚠️ TODO BUG-I (MEDIUM, docs/BACKLOG.md) — TWO pre-migration defects preserved verbatim:
/// (1) FULL-PUT ×8: ALL fields assigned unconditionally — a payload missing fields clears them
/// (Name=null → DB NOT NULL violation → 500); (2) NO dup-Slug check on Update — renaming to an
/// existing slug hits the DB unique index → raw 500 (verified on the pre-migration binary).
/// Both behaviors reproduced 1:1 for parity; fix requires its own approved task.
/// ILoggableCommand (thin log with 8-field changes-snapshot) — NO cache marker.
/// </summary>
public record UpdateCustomFieldCommand(
    Guid Id,
    string Name,
    string Slug,
    string Format,
    string? Element,
    string? FieldValues,
    bool FieldEncrypted,
    string? HelpText,
    bool IsUnique,
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

        // Verbatim pre-migration FULL-PUT: all 8 fields assigned unconditionally (BUG-I #1),
        // and NO dup-Slug check before save (BUG-I #2 — duplicate slug hits the DB unique
        // index → raw 500, verified against the pre-migration binary).
        var before = new { field.Name, field.Slug, field.Format, field.Element, field.FieldValues, field.FieldEncrypted, field.HelpText, field.IsUnique };
        field.Name = request.Name; field.Slug = request.Slug; field.Format = request.Format; field.Element = request.Element;
        field.FieldValues = request.FieldValues; field.FieldEncrypted = request.FieldEncrypted;
        field.HelpText = request.HelpText; field.IsUnique = request.IsUnique;
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
