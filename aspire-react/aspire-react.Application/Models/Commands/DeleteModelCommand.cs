using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Models.Commands;

/// <summary>
/// [Giai đoạn 2] DELETE /api/v1/models/{id} (extracted from AdminController.DeleteModel).
/// Guard order verbatim: hasAssets check runs BEFORE the existence check (missing model with no
/// referencing assets still ends up 404 — same outcome as before). Guard message has NO
/// error_code (MapFailure null-path). ⚠️ FK assets→models is ON DELETE SET NULL (verified in DB):
/// the pre-migration guard is the ONLY protection — assets were never cascade-deleted.
/// ILoggableCommand only (no output-cache on /models).
/// </summary>
public record DeleteModelCommand(Guid Id, Guid CurrentUserId)
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
            ActionType = ActionType.Delete,
            CreatedBy = CurrentUserId,
            CompanyId = null,
            Note = $"Xóa model \"{response.Name}\""
        };
    }
}

public class DeleteModelCommandHandler : IRequestHandler<DeleteModelCommand, ModelResult>
{
    private readonly IApplicationDbContext _context;

    public DeleteModelCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task<ModelResult> Handle(DeleteModelCommand request, CancellationToken cancellationToken)
    {
        var hasAssets = await _context.Assets.AnyAsync(a => a.ModelId == request.Id, cancellationToken);
        if (hasAssets)
            return new ModelResult(false, "Không thể xóa Model đang có tài sản sử dụng.");

        var m = await _context.Models.FindAsync(request.Id);
        if (m == null)
            return new ModelResult(false, "Not found.", "NOT_FOUND");

        _context.Models.Remove(m);
        await _context.SaveChangesAsync(cancellationToken);

        return new ModelResult(true, "Deleted.", ModelId: request.Id, Name: m.Name);
    }
}
