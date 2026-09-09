using AwesomeAssertions;
using CvPlatform.Core.Access;
using CvPlatform.Core.Entities;
using CvPlatform.Core.Enums;

namespace CvPlatform.Tests;

public class AccessRuleEngineTests
{
    private static readonly Guid AttributeId = Guid.NewGuid();
    private readonly AccessRuleEngine _engine = new();

    [Fact]
    public void Restricted_position_requires_all_rules()
    {
        var position = Position(new AccessRule
        {
            AttributeDefinitionId = AttributeId,
            DataType = AttributeDataType.Numeric,
            Operator = RuleOperator.GreaterThan,
            ComparisonValue = "7",
        });

        _engine.CanAccess(position, false, new Dictionary<Guid, TypedValue>
        {
            [AttributeId] = new(AttributeDataType.Numeric, NumericValue: 8),
        }).Should().BeTrue();
        _engine.CanAccess(position, false, new Dictionary<Guid, TypedValue>()).Should().BeFalse();
    }

    [Fact]
    public void Empty_restricted_rules_deny_and_admin_bypasses()
    {
        var position = Position();

        _engine.CanAccess(position, false, new Dictionary<Guid, TypedValue>()).Should().BeFalse();
        _engine.CanAccess(position, true, new Dictionary<Guid, TypedValue>()).Should().BeTrue();
    }

    [Theory]
    [InlineData(RuleOperator.Equals, "7", true)]
    [InlineData(RuleOperator.LessThan, "8", true)]
    [InlineData(RuleOperator.GreaterThan, "7", false)]
    public void Numeric_operators_evaluate(RuleOperator op, string comparison, bool expected)
    {
        var position = Position(new AccessRule
        {
            AttributeDefinitionId = AttributeId,
            DataType = AttributeDataType.Numeric,
            Operator = op,
            ComparisonValue = comparison,
        });

        _engine.CanAccess(position, false, new Dictionary<Guid, TypedValue>
        {
            [AttributeId] = new(AttributeDataType.Numeric, NumericValue: 7),
        }).Should().Be(expected);
    }

    [Fact]
    public void Boolean_false_is_distinct_from_missing()
    {
        var position = Position(new AccessRule
        {
            AttributeDefinitionId = AttributeId,
            DataType = AttributeDataType.Boolean,
            Operator = RuleOperator.IsFalse,
        });

        _engine.CanAccess(position, false, new Dictionary<Guid, TypedValue>
        {
            [AttributeId] = new(AttributeDataType.Boolean, BooleanValue: false),
        }).Should().BeTrue();
    }

    [Fact]
    public void Period_rule_denies_missing_period_start()
    {
        var position = Position(new AccessRule
        {
            AttributeDefinitionId = AttributeId,
            DataType = AttributeDataType.Period,
            Operator = RuleOperator.StartedBefore,
            ComparisonValue = "2025-01-01",
        });

        _engine.CanAccess(position, false, new Dictionary<Guid, TypedValue>
        {
            [AttributeId] = new(AttributeDataType.Period),
        }).Should().BeFalse();
    }

    private static Position Position(params AccessRule[] rules) => new()
    {
        Id = Guid.NewGuid(),
        AccessRules = [.. rules],
    };
}
