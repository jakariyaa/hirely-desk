using System.Globalization;
using CvPlatform.Core.Entities;
using CvPlatform.Core.Enums;

namespace CvPlatform.Core.Access;

public sealed class AccessRuleEngine : IAccessRuleEngine
{
    public bool CanAccess(
        Position position,
        bool isAdmin,
        IReadOnlyDictionary<Guid, TypedValue> profileValues)
    {
        if (isAdmin || position.IsPublic)
            return true;
        if (position.AccessRules.Count == 0)
            return false;

        return position.AccessRules.All(rule =>
            profileValues.TryGetValue(rule.AttributeDefinitionId, out var value) &&
            value.DataType == rule.DataType &&
            RuleOperatorCatalog.IsAllowed(rule.DataType, rule.Operator) &&
            Evaluate(rule.Operator, value, rule.ComparisonValue));
    }

    private static bool Evaluate(RuleOperator op, TypedValue value, string comparison)
    {
        return op switch
        {
            RuleOperator.Equals => value.DataType switch
            {
                AttributeDataType.String or AttributeDataType.Text =>
                    string.Equals(value.StringValue, comparison, StringComparison.Ordinal),
                AttributeDataType.Numeric =>
                    decimal.TryParse(comparison, NumberStyles.Number, CultureInfo.InvariantCulture, out var number) &&
                    value.NumericValue == number,
                AttributeDataType.Date =>
                    DateOnly.TryParse(comparison, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) &&
                    value.DateValue == date,
                AttributeDataType.Dropdown =>
                    string.Equals(value.DropdownOption, comparison, StringComparison.Ordinal),
                _ => false,
            },
            RuleOperator.NotEquals => value.DataType is AttributeDataType.String or AttributeDataType.Text &&
                !string.Equals(value.StringValue, comparison, StringComparison.Ordinal),
            RuleOperator.Contains => value.DataType is AttributeDataType.String or AttributeDataType.Text &&
                value.StringValue?.Contains(comparison, StringComparison.OrdinalIgnoreCase) == true,
            RuleOperator.GreaterThan => CompareNumeric(value, comparison, comparisonValue => value.NumericValue > comparisonValue),
            RuleOperator.LessThan => CompareNumeric(value, comparison, comparisonValue => value.NumericValue < comparisonValue),
            RuleOperator.On => CompareDate(AttributeDataType.Date, value, comparison, comparisonValue => value.DateValue == comparisonValue),
            RuleOperator.Before => CompareDate(AttributeDataType.Date, value, comparison, comparisonValue => value.DateValue < comparisonValue),
            RuleOperator.After => CompareDate(AttributeDataType.Date, value, comparison, comparisonValue => value.DateValue > comparisonValue),
            RuleOperator.StartedBefore => CompareDate(AttributeDataType.Period, value, comparison, comparisonValue => value.PeriodStart < comparisonValue),
            RuleOperator.StartedAfter => CompareDate(AttributeDataType.Period, value, comparison, comparisonValue => value.PeriodStart > comparisonValue),
            RuleOperator.IsTrue => value.DataType == AttributeDataType.Boolean && value.BooleanValue == true,
            RuleOperator.IsFalse => value.DataType == AttributeDataType.Boolean && value.BooleanValue == false,
            _ => false,
        };
    }

    private static bool CompareNumeric(
        TypedValue value, string comparison, Func<decimal, bool> predicate) =>
        value.DataType == AttributeDataType.Numeric &&
        decimal.TryParse(comparison, NumberStyles.Number, CultureInfo.InvariantCulture, out var number) &&
        value.NumericValue is not null && predicate(number);

    private static bool CompareDate(
        AttributeDataType expectedType, TypedValue value, string comparison, Func<DateOnly, bool> predicate) =>
        value.DataType == expectedType &&
        DateOnly.TryParse(comparison, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) &&
        predicate(date);
}
