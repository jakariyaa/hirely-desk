using CvPlatform.Core.Enums;

namespace CvPlatform.Core.Entities;

public class AttributeDefinition : IVersioned
{
    public Guid Id { get; set; }
    public Guid CategoryId { get; set; }
    public string Name { get; set; } = default!;
    public string? Description { get; set; }
    public AttributeDataType DataType { get; set; }
    public bool IsBuiltIn { get; set; }
    public string? OptionsJson { get; set; }
    public long Version { get; set; }

    public AttributeCategory Category { get; set; } = default!;
}
