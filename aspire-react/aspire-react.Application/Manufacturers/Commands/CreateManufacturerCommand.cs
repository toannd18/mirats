using aspire_react.Server.Application.Common;
using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Manufacturers.Commands;

/// <summary>
/// [Giai đoạn 2] POST /api/v1/manufacturers (extracted from AdminController.CreateManufacturer).
/// Validation rules verbatim (all soft-fail 400s WITHOUT error_code, custom Vietnamese messages):
/// Code required 2-5 chars → dup Code → dup Name. No company-scoping (reference data, no
/// CompanyId — by design, NOT a bug). Both behaviors opt-in: thin ActionLog (no LogMeta on
/// create) + cache tag ref:manufacturers.
/// </summary>
public record CreateManufacturerCommand(
    string Code,
    string Name,
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
            ItemId = response.ManufacturerId!.Value,
            ActionType = ActionType.Create,
            CreatedBy = CurrentUserId,
            CompanyId = null,
            Note = $"Tạo nhà sản xuất \"{Name}\""
        };
    }
}

public class CreateManufacturerCommandHandler : IRequestHandler<CreateManufacturerCommand, ManufacturerResult>
{
    private readonly IApplicationDbContext _context;

    public CreateManufacturerCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task<ManufacturerResult> Handle(CreateManufacturerCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Code) || request.Code.Length < 2 || request.Code.Length > 5)
            return new ManufacturerResult(false, "Mã NSX phải từ 2-5 ký tự.");
        if (await _context.Manufacturers.AnyAsync(x => x.Code == request.Code, cancellationToken))
            return new ManufacturerResult(false, "Mã NSX đã tồn tại.");
        if (await _context.Manufacturers.AnyAsync(x => x.Name == request.Name, cancellationToken))
            return new ManufacturerResult(false, "Tên NSX đã tồn tại.");

        var m = new Manufacturer
        {
            Code = request.Code,
            Name = request.Name,
            Url = request.Url,
            SupportUrl = request.SupportUrl,
            SupportEmail = request.SupportEmail
        };
        _context.Manufacturers.Add(m);
        await _context.SaveChangesAsync(cancellationToken);

        return new ManufacturerResult(true, "Created.", ManufacturerId: m.Id, Code: m.Code, Name: m.Name);
    }
}
