using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Models.Queries;

public record ModelManufacturerDto(Guid Id, string Name);
public record ModelCategoryDto(Guid Id, string Name);
public record ModelDepreciationDto(Guid Id, string Name, int Months);

public record ModelListItemDto(
    Guid Id,
    string Name,
    string? ModelNumber,
    int? Eol,
    string? Notes,
    bool Requestable,
    Guid? ManufacturerId,
    Guid? CategoryId,
    Guid? DepreciationId,
    Guid? FieldsetId,
    ModelManufacturerDto? Manufacturer,
    ModelCategoryDto? Category,
    ModelDepreciationDto? Depreciation);

/// <summary>
/// [Giai đoạn 2] GET /api/v1/models (extracted from AdminController.GetModels).
/// Reference data — NOT company-scoped (AssetModel has no CompanyId). Include×3 + nested
/// projection verbatim (Manufacturer{Id,Name}, Category{Id,Name}, Depreciation{Id,Name,Months}).
/// NOTE: this endpoint has NO [OutputCache] (pre-migration had none) — no cache marker needed.
/// </summary>
public record ListModelsQuery : IRequest<IReadOnlyList<ModelListItemDto>>;

public class ListModelsQueryHandler : IRequestHandler<ListModelsQuery, IReadOnlyList<ModelListItemDto>>
{
    private readonly IApplicationDbContext _context;

    public ListModelsQueryHandler(IApplicationDbContext context) => _context = context;

    public async Task<IReadOnlyList<ModelListItemDto>> Handle(ListModelsQuery request, CancellationToken cancellationToken)
    {
        var list = await _context.Models
            .Include(m => m.Manufacturer).Include(m => m.Category).Include(m => m.Depreciation)
            .AsNoTracking().OrderBy(m => m.Name)
            .Select(m => new ModelListItemDto(
                m.Id, m.Name, m.ModelNumber, m.Eol, m.Notes, m.Requestable,
                m.ManufacturerId, m.CategoryId, m.DepreciationId, m.FieldsetId,
                m.Manufacturer == null ? null : new ModelManufacturerDto(m.Manufacturer.Id, m.Manufacturer.Name),
                m.Category == null ? null : new ModelCategoryDto(m.Category.Id, m.Category.Name),
                m.Depreciation == null ? null : new ModelDepreciationDto(m.Depreciation.Id, m.Depreciation.Name, m.Depreciation.Months)))
            .ToListAsync(cancellationToken);
        return list;
    }
}
