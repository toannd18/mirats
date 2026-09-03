using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Licenses.Commands;

public record LicenseResult(bool Success, string Message, string? ErrorCode = null, Guid? Id = null, string? Name = null);

/// <summary>
/// [Giai đoạn 3 — Nặng] POST /api/v1/licenses (extracted from LicensesController.Create).
/// Verbatim: NAME_REQUIRED / SEATS_MIN_1 / CATEGORY_REQUIRED / CATEGORY_INVALID; regular user
/// forced to own company (userCompanyId ?? r.CompanyId); seat rows generated 1..Seats;
/// TerminationDate Kind=Unspecified (timestamptz-vs-without-time-zone convention); log via
/// IActionLogService (thin Log — NOT ILoggableCommand, verbatim ordering in same SaveChanges).
/// </summary>
public record CreateLicenseCommand(
    string Name, string? Serial, int Seats, bool? Reassignable, DateTime? ExpirationDate,
    DateTime? TerminationDate, decimal? PurchaseCost, DateTime? PurchaseDate, string? OrderNumber,
    int? MinSeats, string? Notes, Guid? SupplierId, Guid? ManufacturerId, Guid? CategoryId,
    Guid? CompanyId, Guid CurrentUserId) : IRequest<LicenseResult>;

public class CreateLicenseCommandHandler : IRequestHandler<CreateLicenseCommand, LicenseResult>
{
    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _companyScope;
    private readonly IActionLogService _actionLogService;

    public CreateLicenseCommandHandler(IApplicationDbContext context, ICompanyScopeService companyScope, IActionLogService actionLogService)
    {
        _context = context;
        _companyScope = companyScope;
        _actionLogService = actionLogService;
    }

    public async Task<LicenseResult> Handle(CreateLicenseCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return new LicenseResult(false, "Tên license là bắt buộc.", "NAME_REQUIRED");
        if (request.Seats < 1)
            return new LicenseResult(false, "Số chỗ (Seats) phải từ 1 trở lên.", "SEATS_MIN_1");
        if (!request.CategoryId.HasValue)
            return new LicenseResult(false, "Danh mục là bắt buộc.", "CATEGORY_REQUIRED");
        var category = await _context.Categories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == request.CategoryId.Value, cancellationToken);
        if (category == null || category.CategoryType != CategoryType.License)
            return new LicenseResult(false, "Danh mục không hợp lệ (phải thuộc loại License).", "CATEGORY_INVALID");

        // Regular users are forced to their own company; Superuser picks the company explicitly.
        var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        var companyId = userCompanyId ?? request.CompanyId;

        var l = new License
        {
            Name = request.Name.Trim(),
            Serial = request.Serial,
            Seats = request.Seats,
            Reassignable = request.Reassignable ?? true,
            ExpirationDate = request.ExpirationDate,
            TerminationDate = request.TerminationDate.HasValue ? DateTime.SpecifyKind(request.TerminationDate.Value, DateTimeKind.Unspecified) : null,
            PurchaseCost = request.PurchaseCost,
            PurchaseDate = request.PurchaseDate,
            OrderNumber = request.OrderNumber,
            Notes = request.Notes,
            MinSeats = request.MinSeats,
            SupplierId = request.SupplierId,
            ManufacturerId = request.ManufacturerId,
            CategoryId = request.CategoryId,
            CompanyId = companyId,
            UpdatedAt = DateTime.UtcNow
        };
        _context.Licenses.Add(l);
        for (var i = 1; i <= request.Seats; i++)
            _context.LicenseSeats.Add(new LicenseSeat { LicenseId = l.Id, SeatNumber = i });

        _actionLogService.Log(new ActionLogEntry
        {
            ItemType = ItemType.License,
            ItemId = l.Id,
            ActionType = ActionType.Create,
            CreatedBy = request.CurrentUserId,
            CompanyId = l.CompanyId,
            Note = $"Tạo license \"{l.Name}\" ({request.Seats} seats)"
        });
        await _context.SaveChangesAsync(cancellationToken);
        return new LicenseResult(true, "License created.", Id: l.Id, Name: l.Name);
    }
}
