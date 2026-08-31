namespace aspire_react.Server.Domain.Entities;

public class Depreciation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public int Months { get; set; }
}