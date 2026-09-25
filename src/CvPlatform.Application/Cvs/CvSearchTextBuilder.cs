using CvPlatform.Core.Entities;
using CvPlatform.Core.Enums;

namespace CvPlatform.Application.Cvs;

public static class CvSearchTextBuilder
{
    public static string Build(Cv cv, Guid? excludedAttributeId = null)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(cv.Profile.User.UserName))
            parts.Add(cv.Profile.User.UserName!);

        var values = cv.Profile.AttributeValues.ToDictionary(v => v.AttributeDefinitionId);
        foreach (var requested in cv.Position.Attributes.OrderBy(a => a.SortOrder))
        {
            if (requested.AttributeDefinitionId == excludedAttributeId)
                continue;
            parts.Add(requested.AttributeDefinition.Name);
            if (values.TryGetValue(requested.AttributeDefinitionId, out var value))
            {
                var text = FormatValue(value, requested.AttributeDefinition.DataType);
                if (!string.IsNullOrWhiteSpace(text))
                    parts.Add(text);
            }
        }

        return string.Join(' ', parts);
    }

    private static string? FormatValue(ProfileAttributeValue value, AttributeDataType type) => type switch
    {
        AttributeDataType.String => value.StringValue,
        AttributeDataType.Text => value.TextValue,
        AttributeDataType.Numeric => value.NumericValue?.ToString(System.Globalization.CultureInfo.InvariantCulture),
        AttributeDataType.Date => value.DateValue?.ToString("yyyy-MM-dd"),
        AttributeDataType.Period => string.Join(" ", new[] { value.PeriodStart?.ToString("yyyy-MM-dd"), value.PeriodEnd?.ToString("yyyy-MM-dd") }.Where(v => v is not null)),
        AttributeDataType.Boolean => value.BooleanValue?.ToString(),
        AttributeDataType.Dropdown => value.DropdownOption,
        AttributeDataType.Image => null,
        _ => null,
    };
}
