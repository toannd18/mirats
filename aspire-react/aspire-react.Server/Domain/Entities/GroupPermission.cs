using aspire_react.Server.Domain.Enums;

namespace aspire_react.Server.Domain.Entities;

public class GroupPermission
{
    public Guid GroupId { get; set; }
    public string PermissionKey { get; set; } = string.Empty;
    public PermissionValue Value { get; set; } = PermissionValue.NotSet;

    public PermissionGroup Group { get; set; } = null!;
}