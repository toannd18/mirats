using aspire_react.Server.Application.Common.ActionLogs;
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
/// [AUDIT-LOG B4 — BUG-P FIX] Old snapshot nay chụp TRƯỚC mọi nhánh gán (trước đây `before` chụp SAU
/// khi Code/Name đã gán nên LogMeta ghi code/name old == new — mất giá trị cũ thật). Cả 12 field đi
/// qua helper ActionLogChanges.
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

        // [AUDIT-LOG B4 — BUG-P FIX] Chụp giá trị CŨ của đủ 12 field TRƯỚC mọi nhánh gán.
        // (Trước đây `before` chụp SAU khi Code/Name đã gán ⇒ LogMeta ghi code/name old == new.)
        var changes = new ActionLogChanges()
            .Track("code", s.Code)
            .Track("name", s.Name)
            .Track("url", s.Url)
            .Track("address", s.Address)
            .Track("city", s.City)
            .Track("state", s.State)
            .Track("country", s.Country)
            .Track("zip", s.Zip)
            .Track("phone", s.Phone)
            .Track("fax", s.Fax)
            .Track("contactName", s.ContactName)
            .Track("contactEmail", s.ContactEmail);

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

        var logMeta = changes
            .Set("code", s.Code)
            .Set("name", s.Name)
            .Set("url", s.Url)
            .Set("address", s.Address)
            .Set("city", s.City)
            .Set("state", s.State)
            .Set("country", s.Country)
            .Set("zip", s.Zip)
            .Set("phone", s.Phone)
            .Set("fax", s.Fax)
            .Set("contactName", s.ContactName)
            .Set("contactEmail", s.ContactEmail)
            .ToLogMeta();

        return new SupplierResult(
            true, "Updated.",
            SupplierId: s.Id, Code: s.Code, Name: s.Name,
            LogMeta: logMeta, Note: $"Cập nhật nhà cung cấp \"{s.Name}\"");
    }
}
