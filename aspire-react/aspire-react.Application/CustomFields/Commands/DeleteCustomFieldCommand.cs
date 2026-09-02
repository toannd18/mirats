using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.CustomFields.Commands;

/// <summary>
/// [Giai đoạn 3] DELETE /api/v1/custom-fields/{id} (extracted from CustomFieldsController.Delete).
/// Guard verbatim (ST6a): a field still linked to any CustomFieldFieldset cannot be deleted —
/// its pivot rows (FieldId → CustomField, OnDelete(Cascade)) would be silently cascade-deleted
/// → CUSTOM_FIELD_IN_USE. ILoggableCommand only (no output-cache on custom-fields).
/// </summary>
public record DeleteCustomFieldCommand(Guid Id, Guid CurrentUserId)
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
            ActionType = ActionType.Delete,
            CreatedBy = CurrentUserId,
            CompanyId = null,
            Note = $"Xóa trường tùy chỉnh \"{response.Name}\""
        };
    }
}

public class DeleteCustomFieldCommandHandler : IRequestHandler<DeleteCustomFieldCommand, CustomFieldResult>
{
    private readonly IApplicationDbContext _context;

    public DeleteCustomFieldCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task<CustomFieldResult> Handle(DeleteCustomFieldCommand request, CancellationToken cancellationToken)
    {
        var field = await _context.CustomFields.FindAsync(request.Id);
        if (field == null)
            return new CustomFieldResult(false, "Custom field not found.", "NOT_FOUND");

        // Delete guard (ST6a): a field still linked to any fieldset cannot be deleted — its
        // CustomFieldFieldset pivot rows (FieldId → CustomField, OnDelete(Cascade)) would be
        // silently cascade-deleted, destroying the field↔fieldset relationship history.
        if (await _context.CustomFieldFieldsets.AnyAsync(cf => cf.FieldId == request.Id, cancellationToken))
            return new CustomFieldResult(false,
                "Trường tùy chỉnh đang được fieldset sử dụng — không thể xóa.",
                "CUSTOM_FIELD_IN_USE");

        _context.CustomFields.Remove(field);
        await _context.SaveChangesAsync(cancellationToken);

        return new CustomFieldResult(true, "Custom field deleted.", CustomFieldId: request.Id, Name: field.Name);
    }
}
