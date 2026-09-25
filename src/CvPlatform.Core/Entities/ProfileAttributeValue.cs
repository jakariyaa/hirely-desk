namespace CvPlatform.Core.Entities;

public class ProfileAttributeValue : IVersioned
{
    public Guid Id { get; set; }
    public Guid ProfileId { get; set; }
    public Guid AttributeDefinitionId { get; set; }

    public string? StringValue { get; set; }
    public string? TextValue { get; set; }
    public decimal? NumericValue { get; set; }
    public DateOnly? DateValue { get; set; }
    public DateOnly? PeriodStart { get; set; }
    public DateOnly? PeriodEnd { get; set; }
    public bool? BooleanValue { get; set; }
    public string? DropdownOption { get; set; }
    public string? ImageObjectKey { get; set; }

    public long Version { get; set; }

    public Profile Profile { get; set; } = default!;
    public AttributeDefinition AttributeDefinition { get; set; } = default!;
}
