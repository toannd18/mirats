using aspire_react.Server.Application.Common;
using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Suppliers.Commands;

/// <summary>
/// [Giai đoạn 2] DELETE /api/v1/suppliers/{id} (extracted from AdminController.DeleteSupplier).
/// Delete-guard verbatim: components/accessories/consumables/assets (incl. soft-deleted
/// inventory) + licenses (only non-deleted) → SUPPLIER_IN_USE. Both behaviors opt-in: thin
/// ActionLog + cache tag ref:suppliers.
/// </summary>
public record DeleteSupplierCommand(Guid Id, Guid CurrentUserId)
    : IRequest<SupplierResult>, ILoggableCommand<SupplierResult>, ICacheInvalidatingCommand<SupplierResult>
{
    public IEnumerable<string> CacheTagsToInvalidate => new[] { CacheTags.Suppliers };
    public bool ShouldInvalidateCache(SupplierResult response) => response.Success;

    public ActionLogEntry? BuildLogEntry(SupplierResult response)
    {
        if (!response.Success)
            return null;

        return new ActionLogEntry
        {
            ItemType = ItemType.Supplier,
            ItemId = Id,
            ActionType = ActionType.Delete,
            CreatedBy = CurrentUserId,
            CompanyId = null,
            Note = $"Xóa nhà cung cấp \"{response.Name}\""
        };
    }
}

public class DeleteSupplierCommandHandler : IRequestHandler<DeleteSupplierCommand, SupplierResult>
{
    private readonly IApplicationDbContext _context;

    public DeleteSupplierCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task<SupplierResult> Handle(DeleteSupplierCommand request, CancellationToken cancellationToken)
    {
        var s = await _context.Suppliers.FindAsync(request.Id);
        if (s == null)
            return new SupplierResult(false, "Not found.", "NOT_FOUND");

        // Delete guard: supplier referenced by inventory (incl. asset) / licenses cannot be deleted.
        if (await _context.Components.IgnoreQueryFilters().AnyAsync(c => c.SupplierId == request.Id, cancellationToken)
            || await _context.Accessories.IgnoreQueryFilters().AnyAsync(a => a.SupplierId == request.Id, cancellationToken)
            || await _context.Consumables.IgnoreQueryFilters().AnyAsync(x => x.SupplierId == request.Id, cancellationToken)
            || await _context.Assets.IgnoreQueryFilters().AnyAsync(a => a.SupplierId == request.Id, cancellationToken)
            || await _context.Licenses.IgnoreQueryFilters().AnyAsync(l => l.SupplierId == request.Id && l.DeletedAt == null, cancellationToken))
            return new SupplierResult(false,
                "Nhà cung cấp đang được sản phẩm/tài sản/bản quyền sử dụng — không thể xóa.",
                "SUPPLIER_IN_USE");

        _context.Suppliers.Remove(s);
        await _context.SaveChangesAsync(cancellationToken);

        return new SupplierResult(true, "Deleted.", SupplierId: request.Id, Name: s.Name);
    }
}
