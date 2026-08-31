namespace aspire_react.Server.Domain.Entities;

public class Department
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public Guid? CompanyId { get; set; }
    public Guid? ManagerId { get; set; }
    public string? Phone { get; set; }
    public string? Fax { get; set; }

    // Navigation
    public Company? Company { get; set; }
    public User? Manager { get; set; }
}