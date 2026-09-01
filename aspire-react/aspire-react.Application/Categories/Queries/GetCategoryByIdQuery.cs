using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Categories.Queries;

/// <summary>
/// [Giai đoạn 2] GET /api/v1/categories/{id} — NEW endpoint (was missing pre-migration).
/// Returns the Category entity or NULL when missing (controller maps to the same 404 body style
/// as Department). No company-scoping — Category is global reference data.
/// </summary>
public record GetCategoryByIdQuery(Guid Id) : IRequest<Category?>;

public class GetCategoryByIdQueryHandler : IRequestHandler<GetCategoryByIdQuery, Category?>
{
    private readonly IApplicationDbContext _context;

    public GetCategoryByIdQueryHandler(IApplicationDbContext context) => _context = context;

    public async Task<Category?> Handle(GetCategoryByIdQuery request, CancellationToken cancellationToken)
        => await _context.Categories.AsNoTracking().FirstOrDefaultAsync(x => x.Id == request.Id, cancellationToken);
}
