using aspire_react.Server.Domain.Enums;

namespace aspire_react.Server.Domain.Entities;

public class Category
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public CategoryType CategoryType { get; set; }
    public string? TagColor { get; set; }
    public bool UseDefaultEula { get; set; }
    public bool RequireAcceptance { get; set; }
    public bool CheckinEmail { get; set; }
    public string? Notes { get; set; }
}
