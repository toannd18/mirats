namespace aspire_react.Server.Domain.Entities;

public class UserGroup
{
    public Guid UserId { get; set; }
    public Guid GroupId { get; set; }

    public User User { get; set; } = null!;
    public PermissionGroup Group { get; set; } = null!;
}