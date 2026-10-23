using aspire_react.Server.Application.Common;
using System.Text.Json;
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
            // [AUDIT-LOG B6b] Snapshot TỐI THIỂU cho xóa cứng (id + tên) — không dữ liệu nhạy cảm.
            LogMeta = JsonSerializer.Serialize(new { id = response.CategoryId ?? Id, name = response.Name }),
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
        var blockers = new List<string>();
        if (await _context.Components.IgnoreQueryFilters().AnyAsync(x => x.CategoryId == request.Id, cancellationToken))
            blockers.Add("linh kiện");
        if (await _context.Models.AnyAsync(x => x.CategoryId == request.Id, cancellationToken))
            blockers.Add("model tài sản");
        if (await _context.Consumables.AnyAsync(x => x.CategoryId == request.Id, cancellationToken))
            blockers.Add("vật tư tiêu hao");
        if (await _context.Accessories.AnyAsync(x => x.CategoryId == request.Id, cancellationToken))
            blockers.Add("phụ kiện");
        if (await _context.Licenses.AnyAsync(x => x.CategoryId == request.Id, cancellationToken))
            blockers.Add("bản quyền");
        if (blockers.Count > 0)
            return new CategoryResult(
                false,
                $"Danh mục đang được sử dụng bởi {string.Join(", ", blockers)} — không thể xóa.",
                "CATEGORY_IN_USE");

        _context.Categories.Remove(c);
        await _context.SaveChangesAsync(cancellationToken);

        return new CategoryResult(true, "Deleted.", CategoryId: request.Id, Name: c.Name);
    }
}
