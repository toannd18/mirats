using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.CustomFields.Commands;

/// <summary>
/// [Giai đoạn 3] POST /api/v1/custom-fields (extracted from CustomFieldsController.Create).
/// Validation verbatim: ONLY dup-Slug check ("A field with this slug already exists." — no
/// error_code). Deliberately NO empty-Name/Slug check (pre-migration had none — see BUG-I).
/// ILoggableCommand (thin log, no LogMeta on create) — NO cache marker (no output-cache).
/// </summary>
public record CreateCustomFieldCommand(
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
            ItemId = response.CustomFieldId!.Value,
            ActionType = ActionType.Create,
            CreatedBy = CurrentUserId,
            CompanyId = null,
            Note = $"Tạo trường tùy chỉnh \"{Name}\""
        };
    }
}

public class CreateCustomFieldCommandHandler : IRequestHandler<CreateCustomFieldCommand, CustomFieldResult>
{
    private readonly IApplicationDbContext _context;

    public CreateCustomFieldCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task<CustomFieldResult> Handle(CreateCustomFieldCommand request, CancellationToken cancellationToken)
    {
        var exists = await _context.CustomFields.AnyAsync(f => f.Slug == request.Slug, cancellationToken);
        if (exists)
            return new CustomFieldResult(false, "A field with this slug already exists.");

        var field = new CustomField
        {
            Name = request.Name,
            Slug = request.Slug,
            Format = request.Format,
            Element = request.Element,
            FieldValues = request.FieldValues,
            FieldEncrypted = request.FieldEncrypted,
            HelpText = request.HelpText,
            IsUnique = request.IsUnique
        };
        _context.CustomFields.Add(field);
        await _context.SaveChangesAsync(cancellationToken);

        return new CustomFieldResult(true, "Custom field created.", CustomFieldId: field.Id, Name: field.Name);
    }
}
