using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using MediatR;

namespace aspire_react.Server.Application.Models.Commands;

/// <summary>
/// [Giai đoạn 2] POST /api/v1/models (extracted from AdminController.CreateModel).
/// ⚠️ TODO BUG-H (MEDIUM, docs/BACKLOG.md): NO validation at all — no empty-name check, no
/// dup-check, ManufacturerId/CategoryId/DepreciationId/FieldsetId not verified to exist
/// (bogus GUID → FK violation 500 at SaveChanges). Pre-migration behavior preserved verbatim
/// for parity; fix requires its own approved task. Request DTO narrowing also REMOVED the old
/// client-set-Id quirk (entity binding accepted a client-chosen PK — see BUG-H entry #1).
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
        // TODO BUG-H: no validation (name empty OK, dup OK, FK existence unchecked) — verbatim
        // pre-migration behavior, see docs/BACKLOG.md BUG-H (MEDIUM).
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
