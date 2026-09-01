using System.Text.Json;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using aspire_react.Server.Application.Common.Interfaces;
using MediatR;

namespace aspire_react.Server.Application.Accessories.Commands;

/// <summary>
/// [Giai đoạn 0.2 — M1] PILOT for the ActionLogBehavior: implements ILoggableCommand so the
/// pipeline persists its ActionLog (previously a manual LogAction call inside the handler).
/// BuildLogEntry reproduces the EXACT same log fields the manual call produced — verify parity
/// was done via the real API before/after this migration.
/// </summary>
public record CreateAccessoryCommand : IRequest<AccessoryResult>, ILoggableCommand<AccessoryResult>
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

    /// <summary>
    /// ActionLog source of truth for this command. Null (no log) mirrors the old early-return on
    /// soft-fail (COMPANY_MISMATCH returned before the manual LogAction call was ever reached).
    /// The entry's required fields enforce ItemType/ActionType/ItemId/CreatedBy/CompanyId at
    /// compile time (Task S2a builder).
    /// </summary>
    public ActionLogEntry? BuildLogEntry(AccessoryResult response)
    {
        if (!response.Success)
            return null;

        return new ActionLogEntry
        {
            ItemType = ItemType.Accessory,
            ItemId = response.AccessoryId!.Value,
            ActionType = ActionType.Create,
            CreatedBy = CurrentUserId,
            CompanyId = CompanyId,
            Note = $"Created accessory: {Name}" + (ItemNo != null ? $" (#{ItemNo})" : ""),
            LogMeta = JsonSerializer.Serialize(new { name = Name, qty = Qty, minAmt = MinAmt })
        };
    }
}

public class CreateAccessoryCommandHandler : IRequestHandler<CreateAccessoryCommand, AccessoryResult>
{
    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _companyScope;

    public CreateAccessoryCommandHandler(IApplicationDbContext context, ICompanyScopeService companyScope)
    {
        _context = context;
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

        // [Giai đoạn 0.2 — M1] ActionLog is now persisted by ActionLogBehavior (ILoggableCommand)
        // inside the same transaction as this SaveChanges — manual LogAction call removed.

        await _context.SaveChangesAsync(cancellationToken);

        return new AccessoryResult(true, "Accessory created successfully.", AccessoryId: accessory.Id);
    }
}