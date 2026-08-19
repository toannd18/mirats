namespace aspire_react.Server.Domain.Entities;

public class SystemInfo
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid? CompanyId { get; set; }

    public Company? Company { get; set; }
    public ICollection<SystemPosition> Positions { get; set; } = new List<SystemPosition>();
}
