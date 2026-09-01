using aspire_react.Server.Application.Common;
using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Categories.Commands;

/// <summary>
/// [Giai đoạn 2] DELETE /api/v1/categories/{id} (extracted from AdminController.DeleteCategory).
/// Delete-guard verbatim: reject while ANY of components (incl. soft-deleted)/models/consumables/
/// accessories/licenses references the category → CATEGORY_IN_USE. Both behaviors opt-in:
/// thin ActionLog (note verbatim) + cache tag ref:categories.
/// </summary>
public record DeleteCategoryCommand(Guid Id, Guid CurrentUserId)
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
            ActionType = ActionType.Delete,
            CreatedBy = CurrentUserId,
            CompanyId = null,
            Note = $"Xóa danh mục \"{response.Name}\""
        };
    }
}

public class DeleteCategoryCommandHandler : IRequestHandler<DeleteCategoryCommand, CategoryResult>
{
    private readonly IApplicationDbContext _context;

    public DeleteCategoryCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task<CategoryResult> Handle(DeleteCategoryCommand request, CancellationToken cancellationToken)
    {
        var c = await _context.Categories.FindAsync(request.Id);
        if (c == null)
            return new CategoryResult(false, "Category not found.", "NOT_FOUND");

        // Guard: reject deletion while any entity still references this category
        // (components, asset models, consumables, accessories, licenses — incl. soft-deleted).
        var inUse =
            await _context.Components.IgnoreQueryFilters().AnyAsync(x => x.CategoryId == request.Id, cancellationToken)
            || await _context.Models.AnyAsync(x => x.CategoryId == request.Id, cancellationToken)
            || await _context.Consumables.AnyAsync(x => x.CategoryId == request.Id, cancellationToken)
            || await _context.Accessories.AnyAsync(x => x.CategoryId == request.Id, cancellationToken)
            || await _context.Licenses.AnyAsync(x => x.CategoryId == request.Id, cancellationToken);
        if (inUse)
            return new CategoryResult(false, "Danh mục đang được sử dụng — không thể xóa.", "CATEGORY_IN_USE");

        _context.Categories.Remove(c);
        await _context.SaveChangesAsync(cancellationToken);

        return new CategoryResult(true, "Deleted.", CategoryId: request.Id, Name: c.Name);
    }
}
