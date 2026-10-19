using aspire_react.Server.Application.Common.ActionLogs;
using aspire_react.Server.Application.Common;
using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Manufacturers.Commands;

/// <summary>
/// [Giai đoạn 2] PUT /api/v1/manufacturers/{id} (extracted from AdminController.UpdateManufacturer).
/// Rule order + semantics verbatim (this section HAS dup-checks on Update — better than Category):
/// Code (length re-check + dup `x.Id != id`) → Name (dup `x.Id != id`) → Url/SupportUrl/SupportEmail
/// conditional.
/// [AUDIT-LOG B4 — BUG-P FIX] Old snapshot nay chụp TRƯỚC mọi nhánh gán (trước đây `before` chụp SAU
/// khi Code/Name đã gán nên LogMeta ghi code/name old == new — mất giá trị cũ thật). Cả 5 field đi
/// qua helper ActionLogChanges.
/// Both behaviors opt-in: thin ActionLog + cache tag ref:manufacturers.
/// </summary>
public record UpdateManufacturerCommand(
    Guid Id,
    string? Code,
    string? Name,
    string? Url,
    string? SupportUrl,
    string? SupportEmail,
    Guid CurrentUserId)
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
            ActionType = ActionType.Update,
            CreatedBy = CurrentUserId,
            CompanyId = null,
            LogMeta = response.LogMeta,
            Note = response.Note
        };
    }
}

public class UpdateManufacturerCommandHandler : IRequestHandler<UpdateManufacturerCommand, ManufacturerResult>
{
    private readonly IApplicationDbContext _context;

    public UpdateManufacturerCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task<ManufacturerResult> Handle(UpdateManufacturerCommand request, CancellationToken cancellationToken)
    {
        var m = await _context.Manufacturers.FindAsync(request.Id);
        if (m == null)
            return new ManufacturerResult(false, "Not found.", "NOT_FOUND");

        // [AUDIT-LOG B4 — BUG-P FIX] Chụp giá trị CŨ của đủ 5 field TRƯỚC mọi nhánh gán.
        // (Trước đây `before` chụp SAU khi Code/Name đã gán ⇒ LogMeta ghi code/name old == new.)
        var changes = new ActionLogChanges()
            .Track("code", m.Code)
            .Track("name", m.Name)
            .Track("url", m.Url)
            .Track("supportUrl", m.SupportUrl)
            .Track("supportEmail", m.SupportEmail);

        // Patch semantics + rules verbatim (order preserved).
        if (!string.IsNullOrWhiteSpace(request.Code))
        {
            if (request.Code.Length < 2 || request.Code.Length > 5)
                return new ManufacturerResult(false, "Mã NSX phải từ 2-5 ký tự.");
            if (await _context.Manufacturers.AnyAsync(x => x.Code == request.Code && x.Id != request.Id, cancellationToken))
                return new ManufacturerResult(false, "Mã NSX đã tồn tại.");
            m.Code = request.Code;
        }
        if (!string.IsNullOrWhiteSpace(request.Name))
        {
            if (await _context.Manufacturers.AnyAsync(x => x.Name == request.Name && x.Id != request.Id, cancellationToken))
                return new ManufacturerResult(false, "Tên NSX đã tồn tại.");
            m.Name = request.Name;
        }
        if (request.Url is not null) m.Url = request.Url;
        if (request.SupportUrl is not null) m.SupportUrl = request.SupportUrl;
        if (request.SupportEmail is not null) m.SupportEmail = request.SupportEmail;
        await _context.SaveChangesAsync(cancellationToken);

        var logMeta = changes
            .Set("code", m.Code)
            .Set("name", m.Name)
            .Set("url", m.Url)
            .Set("supportUrl", m.SupportUrl)
            .Set("supportEmail", m.SupportEmail)
            .ToLogMeta();

        return new ManufacturerResult(
            true, "Updated.",
            ManufacturerId: m.Id, Code: m.Code, Name: m.Name,
            LogMeta: logMeta, Note: $"Cập nhật nhà sản xuất \"{m.Name}\"");
    }
}
