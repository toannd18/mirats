using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Categories.Queries;

public record CategoryListItemDto(
    Guid Id,
    string Name,
    CategoryType CategoryType,
    string? TagColor,
    bool CheckinEmail,
    bool RequireAcceptance,
    bool UseDefaultEula,
    string? Notes);

/// <summary>
/// [Giai đoạn 2] GET /api/v1/categories (extracted from AdminController.GetCategories).
/// Reference data — NOT company-scoped (Category has no CompanyId), identical filter/order/
/// projection as the pre-migration action. OutputCache attribute stays on the controller action.
/// </summary>
public record ListCategoriesQuery(CategoryType? Type) : IRequest<IReadOnlyList<CategoryListItemDto>>;

public class ListCategoriesQueryHandler : IRequestHandler<ListCategoriesQuery, IReadOnlyList<CategoryListItemDto>>
{
    private readonly IApplicationDbContext _context;

    public ListCategoriesQueryHandler(IApplicationDbContext context) => _context = context;

    public async Task<IReadOnlyList<CategoryListItemDto>> Handle(ListCategoriesQuery request, CancellationToken cancellationToken)
    {
        var query = _context.Categories.AsNoTracking();
        if (request.Type.HasValue)
            query = query.Where(c => c.CategoryType == request.Type.Value);
        var list = await query.OrderBy(c => c.Name)
            .Select(c => new CategoryListItemDto(
                c.Id, c.Name, c.CategoryType, c.TagColor, c.CheckinEmail, c.RequireAcceptance, c.UseDefaultEula, c.Notes))
            .ToListAsync(cancellationToken);
        return list;
    }
}
