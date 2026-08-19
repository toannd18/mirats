namespace aspire_react.Server.Domain.Enums;

public enum AssignmentTargetType
{
    User = 1,
    Department = 2,
    SystemPosition = 3,
    Asset = 4,      // Used by Components (component assignment to parent asset)
    Location = 5,   // Used by Accessory checkouts (AccessoryCheckoutType.Location)
    SystemInfo = 6  // Used by License checkout (license assigned to a whole SystemInfo)
}