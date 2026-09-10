namespace CvPlatform.Core.Entities;

public class PositionTemplateAttribute
{
    public Guid PositionTemplateId { get; set; }
    public Guid AttributeDefinitionId { get; set; }
    public bool IsRequired { get; set; }
    public int SortOrder { get; set; }

    public PositionTemplate PositionTemplate { get; set; } = default!;
    public AttributeDefinition AttributeDefinition { get; set; } = default!;
}
