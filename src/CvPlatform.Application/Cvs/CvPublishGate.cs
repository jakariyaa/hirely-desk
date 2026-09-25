using CvPlatform.Core.Entities;
using CvPlatform.Core.Enums;

namespace CvPlatform.Application.Cvs;

/// <summary>
/// Pure publish-gate evaluator (§7.4 of the build plan): every IsRequired position
/// attribute must have a non-empty profile value. "Non-empty" is per-type —
/// BooleanValue = false counts as filled (non-null is the test, not truthiness);
/// a Period requires PeriodStart (PeriodEnd optional).
/// </summary>
public static class CvPublishGate
{
    public static bool IsSatisfied(
        IReadOnlyList<PositionAttribute> required, IReadOnlyDictionary<Guid, ProfileAttributeValue> values)
        => Missing(required, values).Count == 0;

    /// <summary>Names of required attributes that are not (yet) filled.</summary>
    public static IReadOnlyList<string> Missing(
        IReadOnlyList<PositionAttribute> required, IReadOnlyDictionary<Guid, ProfileAttributeValue> values)
    {
        var missing = new List<string>();
        foreach (var attr in required.Where(a => a.IsRequired).OrderBy(a => a.SortOrder))
        {
            var definition = attr.AttributeDefinition;
            values.TryGetValue(definition.Id, out var value);
            if (!IsFilled(definition.DataType, value))
                missing.Add(definition.Name);
        }

        return missing;
    }

    private static bool IsFilled(AttributeDataType type, ProfileAttributeValue? value) => type switch
    {
        AttributeDataType.String => value?.StringValue is not null,
        AttributeDataType.Text => value?.TextValue is not null,
        AttributeDataType.Numeric => value?.NumericValue is not null,
        AttributeDataType.Date => value?.DateValue is not null,
        AttributeDataType.Period => value?.PeriodStart is not null,
        AttributeDataType.Boolean => value?.BooleanValue is not null,   // false counts as filled
        AttributeDataType.Dropdown => value?.DropdownOption is not null,
        AttributeDataType.Image => value?.ImageObjectKey is not null,
        _ => false,
    };
}
