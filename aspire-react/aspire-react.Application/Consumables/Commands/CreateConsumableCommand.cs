using System.Text.Json;
using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using MediatR;

namespace aspire_react.Server.Application.Consumables.Commands;

/// <summary>
/// [Giai đoạn 3 — Nặng] Consumables CUD commands — log style VERBATIM: IActionLogService.LogAction
/// (named-args + LogMeta JSON) written in the SAME SaveChanges as data (NOT ILoggableCommand —
/// LogAction's internal enrichment path is the pre-migration behavior).
/// </summary>
public record CreateConsumableCommand(
    string Name, string? ItemNo, int Qty, int MinAmt, Guid? CategoryId, Guid? ManufacturerId,
    Guid? SupplierId, Guid? LocationId, Guid? CompanyId, string? ModelNumber, string? OrderNumber,
    decimal? PurchaseCost, DateTime? PurchaseDate, string? Notes, string? Image, Guid CurrentUserId)
    : IRequest<ConsumableResult>;

public record ConsumableResult(bool Success, string Message, string? ErrorCode = null, Guid? Id = null, string? Name = null);

public class CreateConsumableCommandHandler : IRequestHandler<CreateConsumableCommand, ConsumableResult>
{
    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _companyScope;
    private readonly IActionLogService _actionLogService;

    public CreateConsumableCommandHandler(IApplicationDbContext context, ICompanyScopeService companyScope, IActionLogService actionLogService)
    {
        _context = context;
        _companyScope = companyScope;
        _actionLogService = actionLogService;
    }

    public async Task<ConsumableResult> Handle(CreateConsumableCommand request, CancellationToken cancellationToken)
    {
        // [Task L2] Company-scoping on CREATE: a regular user may only create consumables for their
        // own company (or company-less floater). Superuser (scope → null) may create for any company.
        var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        if (userCompanyId.HasValue && request.CompanyId.HasValue && request.CompanyId.Value != userCompanyId.Value)
            return new ConsumableResult(false, "Bạn chỉ được tạo vật tư cho công ty của mình.", "COMPANY_MISMATCH");

        var c = new Consumable
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
        _context.Consumables.Add(c);
        _actionLogService.LogAction(
            itemType: ItemType.Consumable,
            itemId: c.Id,
            actionType: ActionType.Create,
            loggedByUserId: request.CurrentUserId,
            note: $"Created consumable: {c.Name}",
            logMeta: JsonSerializer.Serialize(new { name = c.Name, qty = c.Qty, minAmt = c.MinAmt }),
            companyId: c.CompanyId);
        await _context.SaveChangesAsync(cancellationToken);
        return new ConsumableResult(true, "Consumable created.", Id: c.Id, Name: c.Name);
    }
}
