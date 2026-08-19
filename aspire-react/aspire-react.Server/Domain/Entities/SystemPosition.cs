namespace aspire_react.Server.Domain.Entities;

public class SystemPosition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SystemInfoId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    // Navigation
    public SystemInfo SystemInfo { get; set; } = null!;
}