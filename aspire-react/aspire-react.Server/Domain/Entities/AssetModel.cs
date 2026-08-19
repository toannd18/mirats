namespace aspire_react.Server.Domain.Entities;

public class AssetModel
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string? ModelNumber { get; set; }
    public Guid? ManufacturerId { get; set; }
    public Guid? CategoryId { get; set; }
    public Guid? DepreciationId { get; set; }
    public Guid? FieldsetId { get; set; }
    public int? Eol { get; set; }
    public string? Notes { get; set; }
    public bool Requestable { get; set; }

    // Navigation
    public Manufacturer? Manufacturer { get; set; }
    public Category? Category { get; set; }
    public Depreciation? Depreciation { get; set; }
    public ICollection<Asset> Assets { get; set; } = new List<Asset>();
}