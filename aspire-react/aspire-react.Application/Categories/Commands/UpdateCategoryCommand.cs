using System.Text.Json;
using aspire_react.Server.Application.Common;
using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Categories.Commands;

/// <summary>
/// [Giai đoạn 2] PUT /api/v1/categories/{id} (extracted from AdminController.UpdateCategory).
/// Patch semantics verbatim (Task M2 — this section was ALREADY patch-safe, unlike Department
/// BUG-E): Name applied only when non-whitespace; TagColor/CheckinEmail/RequireAcceptance/
/// UseDefaultEula/Notes applied only when explicitly sent; CategoryType immutable.
/// NOTE: no duplicate-name check on Update — the pre-migration controller had none, and renames
/// onto existing names SUCCEED (verified against the old binary; pure-migration parity).
/// Both behaviors opt-in: thin ActionLog with changes-snapshot + cache tag ref:categories.
/// </summary>
public record UpdateCategoryCommand(
    Guid Id,
    string? Name,
    string? TagColor,
    bool? CheckinEmail,
    bool? RequireAcceptance,
    bool? UseDefaultEula,
    string? Notes,
    Guid CurrentUserId)
    : IRequest<CategoryResult>, ILoggableCommand<CategoryResult>, ICacheInvalidatingCommand<CategoryResult>
{
    public IEnumerable<string> CacheTagsToInvalidate => new[] { CacheTags.Categories };
    public bool ShouldInvalidateCache(CategoryResult response) => response.Success;

    public ActionLogEntry? BuildLogEntry(CategoryResult response)
    {
        if (!response.Success)
            return null;

        return new ActionLogEntry
        {
            ItemType = ItemType.Category,
            ItemId = Id,
            ActionType = ActionType.Update,
            CreatedBy = CurrentUserId,
            CompanyId = null,
            LogMeta = response.LogMeta,
            Note = response.Note
        };
    }
}

public class UpdateCategoryCommandHandler : IRequestHandler<UpdateCategoryCommand, CategoryResult>
{
    private readonly IApplicationDbContext _context;

    public UpdateCategoryCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task<CategoryResult> Handle(UpdateCategoryCommand request, CancellationToken cancellationToken)
    {
        var c = await _context.Categories.FindAsync(request.Id);
        if (c == null)
            return new CategoryResult(false, "Category not found.", "NOT_FOUND");

        // Patch semantics (Task M2): only fields explicitly sent are applied (absent → keep current).
        var before = new { c.Name, c.TagColor, c.CheckinEmail, c.RequireAcceptance, c.UseDefaultEula, c.Notes };
        var oldName = c.Name;
        if (!string.IsNullOrWhiteSpace(request.Name)) c.Name = request.Name;
        if (request.TagColor is not null) c.TagColor = request.TagColor;
        if (request.CheckinEmail.HasValue) c.CheckinEmail = request.CheckinEmail.Value;
        if (request.RequireAcceptance.HasValue) c.RequireAcceptance = request.RequireAcceptance.Value;
        if (request.UseDefaultEula.HasValue) c.UseDefaultEula = request.UseDefaultEula.Value;
        if (request.Notes is not null) c.Notes = request.Notes;
        // CategoryType cannot be changed after creation

        await _context.SaveChangesAsync(cancellationToken);

        var logMeta = JsonSerializer.Serialize(new
        {
            changes = new
            {
                name = new { old = before.Name, @new = c.Name },
                tagColor = new { old = before.TagColor, @new = c.TagColor },
                checkinEmail = new { old = before.CheckinEmail, @new = c.CheckinEmail },
                requireAcceptance = new { old = before.RequireAcceptance, @new = c.RequireAcceptance },
                useDefaultEula = new { old = before.UseDefaultEula, @new = c.UseDefaultEula },
                notes = new { old = before.Notes, @new = c.Notes }
            }
        });

        return new CategoryResult(
            true, "Updated.",
            CategoryId: c.Id, Name: c.Name,
            LogMeta: logMeta, Note: $"Cập nhật danh mục \"{oldName}\"");
    }
}
