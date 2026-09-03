using CvPlatform.Core.Enums;

namespace CvPlatform.Core.Entities;

public class AccessRule
{
    public Guid Id { get; set; }
    public Guid PositionId { get; set; }
    public Guid AttributeDefinitionId { get; set; }
    public AttributeDataType DataType { get; set; }
    public RuleOperator Operator { get; set; }
    public string ComparisonValue { get; set; } = default!;

    public Position Position { get; set; } = default!;
    public AttributeDefinition AttributeDefinition { get; set; } = default!;
}
