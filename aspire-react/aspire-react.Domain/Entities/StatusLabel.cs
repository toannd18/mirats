namespace aspire_react.Server.Domain.Entities;

public class StatusLabel
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public bool Deployable { get; set; }
    public bool Pending { get; set; }
    public bool Archived { get; set; }
    public string? StatusType { get; set; }
    public string? Color { get; set; }
}