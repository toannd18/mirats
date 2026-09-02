using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.CustomFields.Queries;

/// <summary>
/// [Giai đoạn 3] GET /api/v1/custom-fields/{id} (extracted from CustomFieldsController.GetField —
/// GetById EXISTED pre-migration, verbatim). Returns entity or NULL → controller 404.
/// </summary>
public record GetCustomFieldByIdQuery(Guid Id) : IRequest<CustomField?>;

public class GetCustomFieldByIdQueryHandler : IRequestHandler<GetCustomFieldByIdQuery, CustomField?>
{
    private readonly IApplicationDbContext _context;

    public GetCustomFieldByIdQueryHandler(IApplicationDbContext context) => _context = context;

    public async Task<CustomField?> Handle(GetCustomFieldByIdQuery request, CancellationToken cancellationToken)
        => await _context.CustomFields.AsNoTracking().FirstOrDefaultAsync(x => x.Id == request.Id, cancellationToken);
}
