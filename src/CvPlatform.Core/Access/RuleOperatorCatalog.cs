using CvPlatform.Core.Enums;

namespace CvPlatform.Core.Access;

public static class RuleOperatorCatalog
{
    private static readonly IReadOnlyDictionary<AttributeDataType, IReadOnlyList<RuleOperator>> Operators =
        new Dictionary<AttributeDataType, IReadOnlyList<RuleOperator>>
        {
            [AttributeDataType.String] = [RuleOperator.Equals, RuleOperator.NotEquals, RuleOperator.Contains],
            [AttributeDataType.Text] = [RuleOperator.Contains],
            [AttributeDataType.Numeric] = [RuleOperator.Equals, RuleOperator.GreaterThan, RuleOperator.LessThan],
            [AttributeDataType.Date] = [RuleOperator.On, RuleOperator.Before, RuleOperator.After],
            [AttributeDataType.Period] = [RuleOperator.StartedBefore, RuleOperator.StartedAfter],
            [AttributeDataType.Boolean] = [RuleOperator.IsTrue, RuleOperator.IsFalse],
            [AttributeDataType.Dropdown] = [RuleOperator.Equals],
            [AttributeDataType.Image] = [],
        };

    public static IReadOnlyList<RuleOperator> For(AttributeDataType dataType) =>
        Operators.TryGetValue(dataType, out var operators) ? operators : [];

    public static bool IsAllowed(AttributeDataType dataType, RuleOperator op) =>
        For(dataType).Contains(op);
}
