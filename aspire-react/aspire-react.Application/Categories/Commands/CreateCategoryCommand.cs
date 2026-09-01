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
/// [Giai đoạn 2] POST /api/v1/categories (extracted from AdminController.CreateCategory).
/// Duplicate rule verbatim: Name + CategoryType combination must be unique. No empty-name rule
/// existed pre-migration — deliberately NOT added (pure migration). Both behaviors opt-in:
/// ActionLog (thin entry, note verbatim, no LogMeta on create) + cache tag ref:categories.
/// </summary>
public record CreateCategoryCommand(
    string Name,
    CategoryType CategoryType,
    string? TagColor,
    bool CheckinEmail,
    bool RequireAcceptance,
    bool UseDefaultEula,
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
            ItemId = response.CategoryId!.Value,
            ActionType = ActionType.Create,
            CreatedBy = CurrentUserId,
            CompanyId = null,
            Note = $"Tạo danh mục \"{Name}\" (loại: {CategoryType})"
        };
    }
}

public class CreateCategoryCommandHandler : IRequestHandler<CreateCategoryCommand, CategoryResult>
{
    private readonly IApplicationDbContext _context;

    public CreateCategoryCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task<CategoryResult> Handle(CreateCategoryCommand request, CancellationToken cancellationToken)
    {
        if (await _context.Categories.AnyAsync(
                x => x.Name == request.Name && x.CategoryType == request.CategoryType, cancellationToken))
            return new CategoryResult(false, "Tên danh mục đã tồn tại.");

        var c = new Category
        {
            Name = request.Name,
            CategoryType = request.CategoryType,
            TagColor = request.TagColor,
            CheckinEmail = request.CheckinEmail,
            RequireAcceptance = request.RequireAcceptance,
            UseDefaultEula = request.UseDefaultEula,
            Notes = request.Notes
        };
        _context.Categories.Add(c);
        await _context.SaveChangesAsync(cancellationToken);

        return new CategoryResult(true, "Created.", CategoryId: c.Id, Name: c.Name);
    }
}
