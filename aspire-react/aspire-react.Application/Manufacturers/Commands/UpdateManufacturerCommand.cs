using System.Text.Json;
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
/// conditional. ⚠️ Parity quirk preserved: the `before` snapshot is captured AFTER Code/Name are
/// assigned but BEFORE Url/SupportUrl/SupportEmail — so the Update LogMeta shows code/name
/// old==new (they were already assigned) while url/supportUrl/supportEmail show true old→new.
/// This is the pre-migration behavior, deliberately NOT corrected.
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
        var before = new { m.Code, m.Name, m.Url, m.SupportUrl, m.SupportEmail };
        if (request.Url is not null) m.Url = request.Url;
        if (request.SupportUrl is not null) m.SupportUrl = request.SupportUrl;
        if (request.SupportEmail is not null) m.SupportEmail = request.SupportEmail;
        await _context.SaveChangesAsync(cancellationToken);

        var logMeta = JsonSerializer.Serialize(new
        {
            changes = new
            {
                code = new { old = before.Code, @new = m.Code },
                name = new { old = before.Name, @new = m.Name },
                url = new { old = before.Url, @new = m.Url },
                supportUrl = new { old = before.SupportUrl, @new = m.SupportUrl },
                supportEmail = new { old = before.SupportEmail, @new = m.SupportEmail }
            }
        });

        return new ManufacturerResult(
            true, "Updated.",
            ManufacturerId: m.Id, Code: m.Code, Name: m.Name,
            LogMeta: logMeta, Note: $"Cập nhật nhà sản xuất \"{m.Name}\"");
    }
}
