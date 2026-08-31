namespace aspire_react.Server.Domain.Entities;

public class CustomField
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Format { get; set; } = "TEXT"; // TEXT, NUMBER, DATE, BOOLEAN, SELECT
    public string? Element { get; set; } // text, textarea, date, checkbox, select
    public string? FieldValues { get; set; } // comma-separated or JSON options for dropdown
    public bool FieldEncrypted { get; set; }
    public string? HelpText { get; set; }
    public bool ShowInEmail { get; set; }
    public bool IsUnique { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<CustomFieldFieldset> CustomFieldFieldsets { get; set; } = new List<CustomFieldFieldset>();
}

public class CustomFieldset
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<CustomFieldFieldset> CustomFieldFieldsets { get; set; } = new List<CustomFieldFieldset>();
}

public class CustomFieldFieldset
{
    public Guid FieldsetId { get; set; }
    public Guid FieldId { get; set; }
    public bool Required { get; set; }
    public int Order { get; set; }

    public CustomFieldset Fieldset { get; set; } = null!;
    public CustomField Field { get; set; } = null!;
}