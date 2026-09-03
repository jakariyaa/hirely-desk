namespace CvPlatform.Core.Entities;

public class PositionAttribute
{
    public Guid PositionId { get; set; }
    public Guid AttributeDefinitionId { get; set; }
    public bool IsRequired { get; set; }
    public int SortOrder { get; set; }

    public Position Position { get; set; } = default!;
    public AttributeDefinition AttributeDefinition { get; set; } = default!;
}
