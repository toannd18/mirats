using aspire_react.Server.Domain.Enums;

namespace aspire_react.Server.Domain.Entities;

public class AccessoryCheckout
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AccessoryId { get; set; }

    /// <summary>Polymorphic target type: 1=User, 2=Department, 3=Location, 4=SystemPosition</summary>
    public AccessoryCheckoutType CheckoutType { get; set; } = AccessoryCheckoutType.User;

    /// <summary>The ID of the target entity (User.Id, Department.Id, Location.Id, or SystemPosition.Id)</summary>
    public Guid TargetId { get; set; }

    /// <summary>Quantity initially checked out</summary>
    public int AssignedQty { get; set; } = 1;

    /// <summary>Quantity returned (partial or full check-in)</summary>
    public int ReturnedQty { get; set; }

    /// <summary>The admin who performed the checkout</summary>
    public Guid? CreatedByUserId { get; set; }

    public string? Note { get; set; }
    public DateTime CheckedOutAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public Accessory Accessory { get; set; } = null!;
    public User? CreatedByUser { get; set; }

    // Computed
    public int RemainingCheckedOut => AssignedQty - ReturnedQty;
}