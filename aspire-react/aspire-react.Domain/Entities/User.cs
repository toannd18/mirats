using aspire_react.Server.Domain.Interfaces;

namespace aspire_react.Server.Domain.Entities;

public class User : IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Username { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? EmployeeNumber { get; set; }
    public string? JobTitle { get; set; }

    // Foreign keys
    public Guid? LocationId { get; set; }
    public Guid? DepartmentId { get; set; }
    public Guid? CompanyId { get; set; }

    // Navigation
    public Location? Location { get; set; }
    public Department? Department { get; set; }

    // Auth & Permissions
    public bool IsSuperUser { get; set; }
    public bool IsActive { get; set; } = true;

    // [AUTH Phase 1] Local password auth (Keycloak replacement). PasswordHash null = user has
    // never been given a local password (admin hasn't reset them yet) → password login blocked
    // with a clear message (dual-auth keeps their Keycloak session working until then).
    public string? PasswordHash { get; set; }

    /// <summary>Set by admin reset — forces a password change at next login before system use
    /// (limited-scope token: only /auth/password + /users/me until changed).</summary>
    public bool MustChangePassword { get; set; }

    // Navigation
    public Company? Company { get; set; }
    public ICollection<UserPermission> UserPermissions { get; set; } = new List<UserPermission>();
    public ICollection<UserGroup> UserGroups { get; set; } = new List<UserGroup>();

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}