using aspire_react.Server.Application.Common;
using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Suppliers.Commands;

/// <summary>
/// [Giai đoạn 2] POST /api/v1/suppliers (extracted from AdminController.CreateSupplier).
/// Validation rules verbatim (all soft-fail 400s WITHOUT error_code, custom Vietnamese messages):
/// Code required 2-5 chars → dup Code → dup Name. No company-scoping (reference data, no
/// CompanyId — by design, NOT a bug). Both behaviors opt-in: thin ActionLog (no LogMeta on
/// create) + cache tag ref:suppliers.
/// </summary>
public record CreateSupplierCommand(
    string Code,
    string Name,
    string? Url,
    string? Address,
    string? City,
    string? State,
    string? Country,
    string? Zip,
    string? Phone,
    string? Fax,
    string? ContactName,
    string? ContactEmail,
    Guid CurrentUserId)
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
            ItemId = response.SupplierId!.Value,
            ActionType = ActionType.Create,
            CreatedBy = CurrentUserId,
            CompanyId = null,
            Note = $"Tạo nhà cung cấp \"{Name}\""
        };
    }
}

public class CreateSupplierCommandHandler : IRequestHandler<CreateSupplierCommand, SupplierResult>
{
    private readonly IApplicationDbContext _context;

    public CreateSupplierCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task<SupplierResult> Handle(CreateSupplierCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Code) || request.Code.Length < 2 || request.Code.Length > 5)
            return new SupplierResult(false, "Mã NCC phải từ 2-5 ký tự.");
        if (await _context.Suppliers.AnyAsync(x => x.Code == request.Code, cancellationToken))
            return new SupplierResult(false, "Mã NCC đã tồn tại.");
        if (await _context.Suppliers.AnyAsync(x => x.Name == request.Name, cancellationToken))
            return new SupplierResult(false, "Tên NCC đã tồn tại.");

        var s = new Supplier
        {
            Code = request.Code,
            Name = request.Name,
            Url = request.Url,
            Address = request.Address,
            City = request.City,
            State = request.State,
            Country = request.Country,
            Zip = request.Zip,
            Phone = request.Phone,
            Fax = request.Fax,
            ContactName = request.ContactName,
            ContactEmail = request.ContactEmail
        };
        _context.Suppliers.Add(s);
        await _context.SaveChangesAsync(cancellationToken);

        return new SupplierResult(true, "Created.", SupplierId: s.Id, Code: s.Code, Name: s.Name);
    }
}
