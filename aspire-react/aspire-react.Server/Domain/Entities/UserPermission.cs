using aspire_react.Server.Domain.Enums;

namespace aspire_react.Server.Domain.Entities;

public class UserPermission
{
    public Guid UserId { get; set; }
    public string PermissionKey { get; set; } = string.Empty;
    public PermissionValue Value { get; set; } = PermissionValue.NotSet;

    public User User { get; set; } = null!;
}