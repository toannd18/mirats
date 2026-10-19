using aspire_react.Server.Application.Common.ActionLogs;
using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using MediatR;

namespace aspire_react.Server.Application.Models.Commands;

/// <summary>
/// [Giai đoạn 2] PUT /api/v1/models/{id} (extracted from AdminController.UpdateModel).
/// Patch semantics verbatim (Task M2): 9 fields conditional (`is not null` / non-whitespace for
/// Name). [BUG-H FIX 2026-09-05] Validation ADDED (behavior change approved — see ModelValidation):
/// blank name WHEN SENT → 400; dup-name only when actually CHANGED (exclude self) → 400; supplied
/// FK ids must exist → 400 RESOURCE_NOT_FOUND (previously unchecked → raw FK-violation 500).
/// LogMeta changes-snapshot (9 fields) built in the handler, carried to ActionLogBehavior via
/// the response.
/// </summary>
public record UpdateModelCommand(
    Guid Id,
    string? Name,
    string? ModelNumber,
    Guid? ManufacturerId,
    Guid? CategoryId,
    Guid? DepreciationId,
    Guid? FieldsetId,
    int? Eol,
    string? Notes,
    bool? Requestable,
    Guid CurrentUserId)
    : IRequest<ModelResult>, ILoggableCommand<ModelResult>
{
    public ActionLogEntry? BuildLogEntry(ModelResult response)
    {
        if (!response.Success)
            return null;

        return new ActionLogEntry
        {
            ItemType = ItemType.Model,
            ItemId = Id,
            ActionType = ActionType.Update,
            CreatedBy = CurrentUserId,
            CompanyId = null,
            LogMeta = response.LogMeta,
            Note = response.Note
        };
    }
}

public class UpdateModelCommandHandler : IRequestHandler<UpdateModelCommand, ModelResult>
{
    private readonly IApplicationDbContext _context;

    public UpdateModelCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task<ModelResult> Handle(UpdateModelCommand request, CancellationToken cancellationToken)
    {
        var m = await _context.Models.FindAsync(request.Id);
        if (m == null)
            return new ModelResult(false, "Not found.", "NOT_FOUND");

        // Patch semantics (Task M2): only fields explicitly sent are applied (absent → keep current).
        // [BUG-H FIX] Validation before mutation: blank-when-SENT name → 400; dup only when the
        // name actually changes; FK existence for every SUPPLIED id.
        if (request.Name != null && string.IsNullOrWhiteSpace(request.Name))
            return new ModelResult(false, "Tên model không được để trống.");
        if (request.Name != null && request.Name != m.Name)
        {
            var nameError = await ModelValidation.ValidateNameAsync(_context, request.Name, request.Id, cancellationToken);
            if (nameError != null)
                return new ModelResult(false, nameError);
        }
        var fkError = await ModelValidation.ValidateForeignKeysAsync(
            _context, request.ManufacturerId, request.CategoryId, request.DepreciationId, request.FieldsetId, cancellationToken);
        if (fkError != null)
            return ModelValidation.FkNotFound(fkError);

        // [AUDIT-LOG B6a] helper dùng chung: Track TRƯỚC khi gán (giá trị cũ), Set SAU (giá trị mới).
        var changes = new ActionLogChanges()
            .Track("name", m.Name)
            .Track("modelNumber", m.ModelNumber)
            .Track("manufacturerId", m.ManufacturerId)
            .Track("categoryId", m.CategoryId)
            .Track("depreciationId", m.DepreciationId)
            .Track("fieldsetId", m.FieldsetId)
            .Track("eol", m.Eol)
            .Track("notes", m.Notes)
            .Track("requestable", m.Requestable);
        if (!string.IsNullOrWhiteSpace(request.Name)) m.Name = request.Name;
        if (request.ModelNumber is not null) m.ModelNumber = request.ModelNumber;
        if (request.ManufacturerId is not null) m.ManufacturerId = request.ManufacturerId;
        if (request.CategoryId is not null) m.CategoryId = request.CategoryId;
        if (request.DepreciationId is not null) m.DepreciationId = request.DepreciationId;
        if (request.FieldsetId is not null) m.FieldsetId = request.FieldsetId;
        if (request.Eol is not null) m.Eol = request.Eol;
        if (request.Notes is not null) m.Notes = request.Notes;
        if (request.Requestable.HasValue) m.Requestable = request.Requestable.Value;
        await _context.SaveChangesAsync(cancellationToken);

        var logMeta = changes
            .Set("name", m.Name)
            .Set("modelNumber", m.ModelNumber)
            .Set("manufacturerId", m.ManufacturerId)
            .Set("categoryId", m.CategoryId)
            .Set("depreciationId", m.DepreciationId)
            .Set("fieldsetId", m.FieldsetId)
            .Set("eol", m.Eol)
            .Set("notes", m.Notes)
            .Set("requestable", m.Requestable)
            .ToLogMeta();

        return new ModelResult(
            true, "Updated.",
            ModelId: m.Id, Name: m.Name,
            LogMeta: logMeta, Note: $"Cập nhật model \"{m.Name}\"");
    }
}
