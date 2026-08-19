using aspire_react.Server.Domain.Interfaces;

namespace aspire_react.Server.Domain.Entities;

public class Location : ICompanyable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public Guid? ParentId { get; set; }
    public Guid? ManagerId { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? Zip { get; set; }
    public Guid? CompanyId { get; set; }

    // Self-referencing hierarchy
    public Location? Parent { get; set; }
    public ICollection<Location> Children { get; set; } = new List<Location>();

    // Navigation
    public User? Manager { get; set; }
}