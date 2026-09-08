using System.Text.Json;
using System.Text.RegularExpressions;
using CvPlatform.Application.Profiles;
using CvPlatform.Core.Entities;
using CvPlatform.Core.Enums;

namespace CvPlatform.Application.Attributes;

public static class AttributeValueRules
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static AttributeValueInput Normalize(AttributeDataType dataType, AttributeValueInput input)
    {
        var normalized = input with
        {
            StringValue = NullIfBlank(input.StringValue),
            TextValue = NullIfBlank(input.TextValue),
            DropdownOption = NullIfBlank(input.DropdownOption),
            ImageUrl = NullIfBlank(input.ImageUrl),
        };

        return dataType switch
        {
            AttributeDataType.String => normalized with { TextValue = null, ImageUrl = null },
            AttributeDataType.Text => normalized with { StringValue = null, ImageUrl = null },
            AttributeDataType.Date => normalized with { PeriodStart = null, PeriodEnd = null },
            AttributeDataType.Period => normalized with { DateValue = null },
            AttributeDataType.Boolean => normalized with { PeriodStart = null, PeriodEnd = null },
            AttributeDataType.Dropdown => normalized with { ImageUrl = null },
            AttributeDataType.Image => normalized with { StringValue = null, TextValue = null },
            _ => normalized,
        };
    }

    public static string? Validate(AttributeDefinition definition, AttributeValueInput input)
    {
        if (!HasOnlyExpectedValue(definition.DataType, input))
            return $"Value shape does not match '{definition.Name}'.";

        if (input.PeriodStart is not null && input.PeriodEnd is not null &&
            input.PeriodEnd < input.PeriodStart)
            return "Period end cannot be earlier than period start.";

        AttributeOptions? options;
        try
        {
            options = string.IsNullOrWhiteSpace(definition.OptionsJson)
                ? null
                : JsonSerializer.Deserialize<AttributeOptions>(definition.OptionsJson, JsonOptions);
        }
        catch (JsonException)
        {
            return "Attribute options are invalid.";
        }

        if (definition.DataType == AttributeDataType.Dropdown && input.DropdownOption is not null &&
            (options?.Choices is null || !options.Choices.Contains(input.DropdownOption, StringComparer.Ordinal)))
            return $"'{input.DropdownOption}' is not one of the allowed choices for '{definition.Name}'.";

        if (input.NumericValue is { } numeric && options is not null)
        {
            if (options.Min is { } min && numeric < min)
                return $"Value cannot be less than {min}.";
            if (options.Max is { } max && numeric > max)
                return $"Value cannot exceed {max}.";
        }

        var text = input.StringValue ?? input.TextValue;
        if (text is not null && options?.MaxLength is { } maxLength && text.Length > maxLength)
            return $"Value cannot exceed {maxLength} characters.";

        if (text is not null && !string.IsNullOrWhiteSpace(options?.Regex))
        {
            try
            {
                if (!Regex.IsMatch(text, options.Regex, RegexOptions.CultureInvariant))
                    return "Value format is invalid.";
            }
            catch (ArgumentException)
            {
                return "Attribute options are invalid.";
            }
        }

        if (input.ImageUrl is not null &&
            (!Uri.TryCreate(input.ImageUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps))
            return "Image URL must use HTTPS.";

        return null;
    }

    private static bool HasOnlyExpectedValue(AttributeDataType dataType, AttributeValueInput input)
    {
        var populated = new[]
        {
            (AttributeDataType.String, input.StringValue is not null),
            (AttributeDataType.Text, input.TextValue is not null),
            (AttributeDataType.Numeric, input.NumericValue is not null),
            (AttributeDataType.Date, input.DateValue is not null),
            (AttributeDataType.Period, input.PeriodStart is not null || input.PeriodEnd is not null),
            (AttributeDataType.Boolean, input.BooleanValue is not null),
            (AttributeDataType.Dropdown, input.DropdownOption is not null),
            (AttributeDataType.Image, input.ImageUrl is not null),
        };

        return populated.All(value => !value.Item2 || value.Item1 == dataType);
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record AttributeOptions(
        string[]? Choices,
        decimal? Min,
        decimal? Max,
        int? MaxLength,
        string? Regex);
}
