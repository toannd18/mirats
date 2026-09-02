using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.CustomFields.Queries;

/// <summary>
/// [Giai đoạn 3] GET /api/v1/custom-fields (extracted from CustomFieldsController.GetFields).
/// Raw entity list ordered by Name — verbatim. Reference-ish data: NO company-scoping
/// (CustomField has no CompanyId), NO output-cache (pre-migration had none).
/// </summary>
public record ListCustomFieldsQuery : IRequest<IReadOnlyList<CustomField>>;

public class ListCustomFieldsQueryHandler : IRequestHandler<ListCustomFieldsQuery, IReadOnlyList<CustomField>>
{
    private readonly IApplicationDbContext _context;

    public ListCustomFieldsQueryHandler(IApplicationDbContext context) => _context = context;

    public async Task<IReadOnlyList<CustomField>> Handle(ListCustomFieldsQuery request, CancellationToken cancellationToken)
    {
        var list = await _context.CustomFields.AsNoTracking().OrderBy(f => f.Name).ToListAsync(cancellationToken);
        return list;
    }
}
