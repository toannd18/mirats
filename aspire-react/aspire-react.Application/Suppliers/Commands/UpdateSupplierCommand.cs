using System.Text.Json;
using aspire_react.Server.Application.Common;
using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Suppliers.Commands;

/// <summary>
/// [Giai đoạn 2] PUT /api/v1/suppliers/{id} (extracted from AdminController.UpdateSupplier).
/// Rule order + semantics verbatim (this section HAS dup-checks on Update): Code (length
/// re-check + dup `x.Id != id`) → Name (dup `x.Id != id`) → 10 contact/address fields conditional.
/// ⚠️ Parity quirk preserved (same as Manufacturer): the `before` snapshot is captured AFTER
/// Code/Name are assigned but BEFORE the 10 fields — Update LogMeta shows code/name old==new.
/// Both behaviors opt-in: thin ActionLog + cache tag ref:suppliers.
/// </summary>
public record UpdateSupplierCommand(
    Guid Id,
    string? Code,
    string? Name,
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
            ItemId = Id,
            ActionType = ActionType.Update,
            CreatedBy = CurrentUserId,
            CompanyId = null,
            LogMeta = response.LogMeta,
            Note = response.Note
        };
    }
}

public class UpdateSupplierCommandHandler : IRequestHandler<UpdateSupplierCommand, SupplierResult>
{
    private readonly IApplicationDbContext _context;

    public UpdateSupplierCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task<SupplierResult> Handle(UpdateSupplierCommand request, CancellationToken cancellationToken)
    {
        var s = await _context.Suppliers.FindAsync(request.Id);
        if (s == null)
            return new SupplierResult(false, "Not found.", "NOT_FOUND");

        // Patch semantics + rules verbatim (order preserved).
        if (!string.IsNullOrWhiteSpace(request.Code))
        {
            if (request.Code.Length < 2 || request.Code.Length > 5)
                return new SupplierResult(false, "Mã NCC phải từ 2-5 ký tự.");
            if (await _context.Suppliers.AnyAsync(x => x.Code == request.Code && x.Id != request.Id, cancellationToken))
                return new SupplierResult(false, "Mã NCC đã tồn tại.");
            s.Code = request.Code;
        }
        if (!string.IsNullOrWhiteSpace(request.Name))
        {
            if (await _context.Suppliers.AnyAsync(x => x.Name == request.Name && x.Id != request.Id, cancellationToken))
                return new SupplierResult(false, "Tên NCC đã tồn tại.");
            s.Name = request.Name;
        }
        var before = new { s.Code, s.Name, s.Url, s.Address, s.City, s.State, s.Country, s.Zip, s.Phone, s.Fax, s.ContactName, s.ContactEmail };
        if (request.Url is not null) s.Url = request.Url;
        if (request.Address is not null) s.Address = request.Address;
        if (request.City is not null) s.City = request.City;
        if (request.State is not null) s.State = request.State;
        if (request.Country is not null) s.Country = request.Country;
        if (request.Zip is not null) s.Zip = request.Zip;
        if (request.Phone is not null) s.Phone = request.Phone;
        if (request.Fax is not null) s.Fax = request.Fax;
        if (request.ContactName is not null) s.ContactName = request.ContactName;
        if (request.ContactEmail is not null) s.ContactEmail = request.ContactEmail;
        await _context.SaveChangesAsync(cancellationToken);

        var logMeta = JsonSerializer.Serialize(new
        {
            changes = new
            {
                code = new { old = before.Code, @new = s.Code },
                name = new { old = before.Name, @new = s.Name },
                url = new { old = before.Url, @new = s.Url },
                address = new { old = before.Address, @new = s.Address },
                city = new { old = before.City, @new = s.City },
                state = new { old = before.State, @new = s.State },
                country = new { old = before.Country, @new = s.Country },
                zip = new { old = before.Zip, @new = s.Zip },
                phone = new { old = before.Phone, @new = s.Phone },
                fax = new { old = before.Fax, @new = s.Fax },
                contactName = new { old = before.ContactName, @new = s.ContactName },
                contactEmail = new { old = before.ContactEmail, @new = s.ContactEmail }
            }
        });

        return new SupplierResult(
            true, "Updated.",
            SupplierId: s.Id, Code: s.Code, Name: s.Name,
            LogMeta: logMeta, Note: $"Cập nhật nhà cung cấp \"{s.Name}\"");
    }
}
