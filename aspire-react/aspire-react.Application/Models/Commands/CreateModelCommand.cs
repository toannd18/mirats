using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using MediatR;

namespace aspire_react.Server.Application.Models.Commands;

/// <summary>
/// [Giai đoạn 2] POST /api/v1/models (extracted from AdminController.CreateModel).
/// [BUG-H FIX 2026-09-05] Validation ADDED (behavior change approved — see ModelValidation):
/// empty-name → 400; dup-name → 400; supplied ManufacturerId/CategoryId/DepreciationId/FieldsetId
/// must exist → 400 RESOURCE_NOT_FOUND (previously: no checks at all, bogus FK → raw 500 at
/// SaveChanges). Request DTO narrowing had already REMOVED the old client-set-Id quirk (entity
/// binding accepted a client-chosen PK — BUG-H entry #1, fixed by DTO since migration).
/// ILoggableCommand only: /models has NO output-cache — ICacheInvalidatingCommand deliberately
/// NOT implemented.
/// </summary>
public record CreateModelCommand(
    string Name,
    string? ModelNumber,
    Guid? ManufacturerId,
    Guid? CategoryId,
    Guid? DepreciationId,
    Guid? FieldsetId,
    int? Eol,
    string? Notes,
    bool Requestable,
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
            ItemId = response.ModelId!.Value,
            ActionType = ActionType.Create,
            CreatedBy = CurrentUserId,
            CompanyId = null,
            Note = $"Tạo model \"{Name}\""
        };
    }
}

public class CreateModelCommandHandler : IRequestHandler<CreateModelCommand, ModelResult>
{
    private readonly IApplicationDbContext _context;

    public CreateModelCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task<ModelResult> Handle(CreateModelCommand request, CancellationToken cancellationToken)
    {
        // [BUG-H FIX] Validation before any mutation (order: name → dup → FK existence).
        var nameError = await ModelValidation.ValidateNameAsync(_context, request.Name, excludeModelId: null, cancellationToken);
        if (nameError != null)
            return new ModelResult(false, nameError);
        var fkError = await ModelValidation.ValidateForeignKeysAsync(
            _context, request.ManufacturerId, request.CategoryId, request.DepreciationId, request.FieldsetId, cancellationToken);
        if (fkError != null)
            return ModelValidation.FkNotFound(fkError);

        var m = new AssetModel
        {
            Name = request.Name,
            ModelNumber = request.ModelNumber,
            ManufacturerId = request.ManufacturerId,
            CategoryId = request.CategoryId,
            DepreciationId = request.DepreciationId,
            FieldsetId = request.FieldsetId,
            Eol = request.Eol,
            Notes = request.Notes,
            Requestable = request.Requestable
        };
        _context.Models.Add(m);
        await _context.SaveChangesAsync(cancellationToken);

        return new ModelResult(true, "Created.", ModelId: m.Id, Name: m.Name);
    }
}
