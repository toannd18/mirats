using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;

namespace aspire_react.Server.Application.Common.Inventory;

/// <summary>
/// [FEAT-SPARE 3] Single source of truth for remaining stock of components and consumables.
/// <para>
/// Every read endpoint (list, detail, low-stock, system spare-part tab, dashboard, export) and the
/// issue/return command MUST call this helper. Remaining is the physical/allocated pool MINUS the
/// net quantity issued to systems (Issue = −, Return = +). A serial component is still counted by
/// its InStock units; issuing serial components is rejected by the write API.
/// </para>
/// </summary>
public static class InventoryRemaining
{
    /// <summary>Net quantity currently issued to systems (Issue positive, Return negative).</summary>
    public static int NetIssued(IEnumerable<SystemSparePartMovement> movements) =>
        movements.Sum(m => m.Direction == SparePartMovementDirection.Issue ? m.Quantity : -m.Quantity);

    /// <summary>Net quantity issued to one specific system.</summary>
    public static int IssuedToSystem(IEnumerable<SystemSparePartMovement> movements, Guid systemId) =>
        NetIssued(movements.Where(m => m.SystemInfoId == systemId));

    /// <summary>Remaining quantity of a component (bulk or serial).</summary>
    public static int ForComponent(Component component)
    {
        var baseQty = component.TrackingType == TrackingType.Serial
            ? component.Units.Count(u => u.Status == ComponentUnitStatus.InStock)
            : component.Qty - component.Assignments.Sum(a => a.AssignedQty);
        return baseQty - NetIssued(component.SparePartMovements);
    }

    /// <summary>Remaining quantity of a consumable.</summary>
    public static int ForConsumable(Consumable consumable) =>
        consumable.Qty
        - consumable.Checkouts.Sum(c => c.Quantity)
        - NetIssued(consumable.SparePartMovements);
}
