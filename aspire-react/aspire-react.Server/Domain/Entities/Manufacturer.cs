namespace aspire_react.Server.Domain.Entities;

public class Manufacturer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Url { get; set; }
    public string? SupportUrl { get; set; }
    public string? SupportEmail { get; set; }
}
