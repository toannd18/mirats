using aspire_react.Server.Application.Common;
using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Manufacturers.Commands;

/// <summary>
/// [Giai đoạn 2] DELETE /api/v1/manufacturers/{id} (extracted from AdminController.DeleteManufacturer).
/// Delete-guard verbatim: components/accessories/consumables/models (incl. soft-deleted inventory)
/// + licenses (only non-deleted) → MANUFACTURER_IN_USE. Both behaviors opt-in: thin ActionLog +
/// cache tag ref:manufacturers.
/// </summary>
public record DeleteManufacturerCommand(Guid Id, Guid CurrentUserId)
    : IRequest<ManufacturerResult>, ILoggableCommand<ManufacturerResult>, ICacheInvalidatingCommand<ManufacturerResult>
{
    public IEnumerable<string> CacheTagsToInvalidate => new[] { CacheTags.Manufacturers };
    public bool ShouldInvalidateCache(ManufacturerResult response) => response.Success;

    public ActionLogEntry? BuildLogEntry(ManufacturerResult response)
    {
        if (!response.Success)
            return null;

        return new ActionLogEntry
        {
            ItemType = ItemType.Manufacturer,
            ItemId = Id,
            ActionType = ActionType.Delete,
            CreatedBy = CurrentUserId,
            CompanyId = null,
            Note = $"Xóa nhà sản xuất \"{response.Name}\""
        };
    }
}

public class DeleteManufacturerCommandHandler : IRequestHandler<DeleteManufacturerCommand, ManufacturerResult>
{
    private readonly IApplicationDbContext _context;

    public DeleteManufacturerCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task<ManufacturerResult> Handle(DeleteManufacturerCommand request, CancellationToken cancellationToken)
    {
        var m = await _context.Manufacturers.FindAsync(request.Id);
        if (m == null)
            return new ManufacturerResult(false, "Not found.", "NOT_FOUND");

        // Delete guard: manufacturer referenced by inventory/models/licenses cannot be deleted.
        if (await _context.Components.IgnoreQueryFilters().AnyAsync(c => c.ManufacturerId == request.Id, cancellationToken)
            || await _context.Accessories.IgnoreQueryFilters().AnyAsync(a => a.ManufacturerId == request.Id, cancellationToken)
            || await _context.Consumables.IgnoreQueryFilters().AnyAsync(x => x.ManufacturerId == request.Id, cancellationToken)
            || await _context.Models.IgnoreQueryFilters().AnyAsync(mm => mm.ManufacturerId == request.Id, cancellationToken)
            || await _context.Licenses.IgnoreQueryFilters().AnyAsync(l => l.ManufacturerId == request.Id && l.DeletedAt == null, cancellationToken))
            return new ManufacturerResult(false,
                "Nhà sản xuất đang được sản phẩm/model/bản quyền sử dụng — không thể xóa.",
                "MANUFACTURER_IN_USE");

        _context.Manufacturers.Remove(m);
        await _context.SaveChangesAsync(cancellationToken);

        return new ManufacturerResult(true, "Deleted.", ManufacturerId: request.Id, Name: m.Name);
    }
}
