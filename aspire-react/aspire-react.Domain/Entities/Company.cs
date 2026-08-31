using aspire_react.Server.Domain.Interfaces;

namespace aspire_react.Server.Domain.Entities;

public class Company : IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    /// <summary>Short unique code used for {COMPANY} in auto-generated Asset Tags (Task ASSET-TAG-AUTO).
    /// Auto-suggested from the name but admin-editable. "NOCO" is reserved for company-less floaters.</summary>
    public string Code { get; set; } = string.Empty;
    public Guid? ParentId { get; set; }

    // Multi-tenant hierarchy
    public Company? Parent { get; set; }
    public ICollection<Company> Children { get; set; } = new List<Company>();

    // Navigation
    public ICollection<User> Users { get; set; } = new List<User>();

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}