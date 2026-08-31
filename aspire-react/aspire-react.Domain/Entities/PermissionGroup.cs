using aspire_react.Server.Domain.Interfaces;

namespace aspire_react.Server.Domain.Entities;

public class PermissionGroup : IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>
    /// System groups (Superuser/Admin) cannot be renamed or deleted via the UI,
    /// preventing admins from locking themselves out of the system.
    /// </summary>
    public bool IsSystem { get; set; }

    // Navigation
    public ICollection<GroupPermission> GroupPermissions { get; set; } = new List<GroupPermission>();
    public ICollection<UserGroup> UserGroups { get; set; } = new List<UserGroup>();

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}