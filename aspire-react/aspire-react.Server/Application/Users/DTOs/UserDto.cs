namespace aspire_react.Server.Application.Users.DTOs;

/// <summary>Permission group membership summary (used in UserDto + assignment responses).</summary>
public record UserGroupDto(Guid GroupId, string Name, bool IsSystem);

/// <summary>
/// Data Transfer Object for User with expanded navigation information.
/// Maps User entity to API response including Company, Department, Location names.
/// </summary>
public class UserDto
{
    public Guid Id { get; init; }
    public string Username { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public string? EmployeeNumber { get; init; }
    public string? JobTitle { get; init; }
    public bool IsSuperUser { get; init; }
    public bool IsActive { get; init; }

    // Navigation names instead of just IDs
    public string? CompanyName { get; init; }
    public string? DepartmentName { get; init; }
    public string? LocationName { get; init; }

    // Foreign keys (for form editing)
    public Guid? CompanyId { get; init; }
    public Guid? DepartmentId { get; init; }
    public Guid? LocationId { get; init; }

    /// <summary>Permission groups the user belongs to.</summary>
    public List<UserGroupDto> Groups { get; init; } = new();

    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}