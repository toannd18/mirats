using System.Text.Json;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using aspire_react.Server.Infrastructure.Persistence;
using aspire_react.Server.Infrastructure.Services;
using MediatR;

namespace aspire_react.Server.Application.Accessories.Commands;

public record CreateAccessoryCommand : IRequest<AccessoryResult>
{
    public string Name { get; init; } = string.Empty;
    public string? ItemNo { get; init; }
    public int Qty { get; init; }
    public int MinAmt { get; init; }
    public Guid? CategoryId { get; init; }
    public Guid? ManufacturerId { get; init; }
    public Guid? SupplierId { get; init; }
    public Guid? LocationId { get; init; }
    public Guid? CompanyId { get; init; }
    public string? ModelNumber { get; init; }
    public string? OrderNumber { get; init; }
    public decimal? PurchaseCost { get; init; }
    public DateTime? PurchaseDate { get; init; }
    public string? Notes { get; init; }
    public string? Image { get; init; }
    public Guid CurrentUserId { get; init; }
}

public class CreateAccessoryCommandHandler : IRequestHandler<CreateAccessoryCommand, AccessoryResult>
{
    private readonly AppDbContext _context;
    private readonly IActionLogService _actionLogService;
    private readonly ICompanyScopeService _companyScope;

    public CreateAccessoryCommandHandler(AppDbContext context, IActionLogService actionLogService, ICompanyScopeService companyScope)
    {
        _context = context;
        _actionLogService = actionLogService;
        _companyScope = companyScope;
    }

    public async Task<AccessoryResult> Handle(CreateAccessoryCommand request, CancellationToken cancellationToken)
    {
        // [Task L2] Company-scoping on CREATE: a regular user may only create accessories for their
        // own company (or company-less floater). Superuser (scope → null) may create for any company.
        var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        if (userCompanyId.HasValue && request.CompanyId.HasValue && request.CompanyId.Value != userCompanyId.Value)
            return new AccessoryResult(false, "Bạn chỉ được tạo phụ kiện cho công ty của mình.", ErrorCode: "COMPANY_MISMATCH");

        var accessory = new Accessory
        {
            Name = request.Name,
            ItemNo = request.ItemNo,
            Qty = request.Qty,
            MinAmt = request.MinAmt,
            CategoryId = request.CategoryId,
            ManufacturerId = request.ManufacturerId,
            SupplierId = request.SupplierId,
            LocationId = request.LocationId,
            CompanyId = request.CompanyId,
            ModelNumber = request.ModelNumber,
            OrderNumber = request.OrderNumber,
            PurchaseCost = request.PurchaseCost,
            PurchaseDate = request.PurchaseDate,
            Notes = request.Notes,
            Image = request.Image
        };
        _context.Accessories.Add(accessory);

        _actionLogService.LogAction(
            itemType: ItemType.Accessory,
            itemId: accessory.Id,
            actionType: ActionType.Create,
            loggedByUserId: request.CurrentUserId,
            companyId: accessory.CompanyId,
            note: $"Created accessory: {request.Name}" + (request.ItemNo != null ? $" (#{request.ItemNo})" : ""),
            logMeta: JsonSerializer.Serialize(new { name = request.Name, qty = request.Qty, minAmt = request.MinAmt }));

        await _context.SaveChangesAsync(cancellationToken);

        return new AccessoryResult(true, "Accessory created successfully.", AccessoryId: accessory.Id);
    }
}