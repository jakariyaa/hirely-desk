namespace CvPlatform.Core.Entities;

public class AttributeCategory
{
    public Guid Id { get; set; }
    public string Name { get; set; } = default!;

    public List<AttributeDefinition> Definitions { get; set; } = [];
}
