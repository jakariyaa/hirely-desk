using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using CvPlatform.Core.Access;
using CvPlatform.Application.Profiles;
using CvPlatform.Core.Entities;
using CvPlatform.Core.Enums;

namespace CvPlatform.Application.Attributes;

public static class AttributeValueRules
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);

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
            (options?.Choices is null || !options.Choices
                .Select(choice => choice.Trim())
                .Contains(input.DropdownOption, StringComparer.Ordinal)))
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
                if (!Regex.IsMatch(text, options.Regex, RegexOptions.CultureInvariant, RegexTimeout))
                    return "Value format is invalid.";
            }
            catch (ArgumentException)
            {
                return "Attribute options are invalid.";
            }
            catch (RegexMatchTimeoutException)
            {
                return "Attribute options are invalid.";
            }
        }

        if (input.ImageUrl is not null &&
            (!Uri.TryCreate(input.ImageUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps))
            return "Image URL must use HTTPS.";

        return null;
    }

    public static string? ValidateOptions(AttributeDataType dataType, string? optionsJson)
    {
        if (string.IsNullOrWhiteSpace(optionsJson))
            return null;
        if (optionsJson.Length > 32_768)
            return "Attribute options cannot exceed 32 KB.";

        AttributeOptions? options;
        try
        {
            using var document = JsonDocument.Parse(optionsJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return "Options must be a JSON object.";
            options = JsonSerializer.Deserialize<AttributeOptions>(optionsJson, JsonOptions);
        }
        catch (JsonException)
        {
            return "Options are invalid JSON.";
        }

        if (options is null)
            return "Options must be a JSON object.";

        if (options.Choices is not null && dataType != AttributeDataType.Dropdown)
            return "Choices are supported only for dropdown attributes.";
        if (dataType == AttributeDataType.Dropdown && options.Choices is not null &&
            (options.Choices.Any(string.IsNullOrWhiteSpace) ||
             options.Choices.Select(choice => choice.Trim()).Distinct(StringComparer.Ordinal).Count() != options.Choices.Length))
            return "Dropdown choices must be non-empty and unique.";

        if ((options.Min is not null || options.Max is not null) && dataType != AttributeDataType.Numeric)
            return "Min and max are supported only for numeric attributes.";
        if (options.Min is not null && options.Max is not null && options.Min > options.Max)
            return "Minimum cannot exceed maximum.";

        if (options.MaxLength is not null &&
            dataType is not AttributeDataType.String and not AttributeDataType.Text)
            return "Max length is supported only for string and text attributes.";
        if (options.MaxLength is <= 0 or > 10_000)
            return "Max length must be between 1 and 10000.";

        if (options.Regex is not null &&
            dataType is not AttributeDataType.String and not AttributeDataType.Text)
            return "Regex is supported only for string and text attributes.";
        if (!string.IsNullOrWhiteSpace(options.Regex))
        {
            try
            {
                _ = Regex.IsMatch(string.Empty, options.Regex, RegexOptions.CultureInvariant, RegexTimeout);
            }
            catch (ArgumentException)
            {
                return "Attribute options are invalid.";
            }
            catch (RegexMatchTimeoutException)
            {
                return "Attribute options are invalid.";
            }
        }

        return null;
    }

    public static string? ValidateComparison(
        AttributeDefinition definition, RuleOperator op, string? comparison)
    {
        comparison = comparison?.Trim();
        if (!RuleOperatorCatalog.IsAllowed(definition.DataType, op))
            return "Operator is not supported for this attribute type.";
        if (op is RuleOperator.IsTrue or RuleOperator.IsFalse)
            return string.IsNullOrEmpty(comparison) ? null : "Boolean rules do not accept a comparison value.";
        if (string.IsNullOrEmpty(comparison))
            return "Comparison value is required.";

        return definition.DataType switch
        {
            AttributeDataType.Numeric => decimal.TryParse(
                comparison, NumberStyles.Number, CultureInfo.InvariantCulture, out _)
                ? null : "Comparison value must be a number.",
            AttributeDataType.Date or AttributeDataType.Period => DateOnly.TryParse(
                comparison, CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
                ? null : "Comparison value must be a date.",
            AttributeDataType.Dropdown => IsDropdownChoice(definition.OptionsJson, comparison)
                ? null : "Comparison value is not an allowed choice.",
            _ => null,
        };
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

    private static bool IsDropdownChoice(string? optionsJson, string value)
    {
        try
        {
            var options = string.IsNullOrWhiteSpace(optionsJson)
                ? null
                : JsonSerializer.Deserialize<AttributeOptions>(optionsJson, JsonOptions);
            return options?.Choices?.Select(choice => choice.Trim()).Contains(value, StringComparer.Ordinal) == true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private sealed record AttributeOptions(
        string[]? Choices,
        decimal? Min,
        decimal? Max,
        int? MaxLength,
        string? Regex);
}
