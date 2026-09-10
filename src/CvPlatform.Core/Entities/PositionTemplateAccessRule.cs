using CvPlatform.Core.Enums;

namespace CvPlatform.Core.Entities;

public class PositionTemplateAccessRule
{
    public Guid Id { get; set; }
    public Guid PositionTemplateId { get; set; }
    public Guid AttributeDefinitionId { get; set; }
    public AttributeDataType DataType { get; set; }
    public RuleOperator Operator { get; set; }
    public string ComparisonValue { get; set; } = "";
    public decimal? NumericComparison { get; set; }
    public DateOnly? DateComparison { get; set; }

    public PositionTemplate PositionTemplate { get; set; } = default!;
    public AttributeDefinition AttributeDefinition { get; set; } = default!;
}
