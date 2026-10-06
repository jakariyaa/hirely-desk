using System.Text.Json;
using CvPlatform.Core.Enums;

namespace CvPlatform.Application.Attributes;

/// <summary>
/// Calendar-date semantics shared by attribute value validation, the attribute catalog and the UI.
/// Dates are calendar days: <see cref="DateOnly"/> with no time or offset component.
/// </summary>
public static class AttributeDateRules
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>An inclusive range of allowed calendar days.</summary>
    public sealed record DateRange(DateOnly? Min, DateOnly? Max)
    {
        public static DateRange Unbounded { get; } = new(null, null);
    }

    /// <summary>The current calendar day for the supplied clock, defaulting to the system clock.</summary>
    public static DateOnly TodayFrom(TimeProvider? timeProvider) =>
        DateOnly.FromDateTime((timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime);

    /// <summary>Parses attribute options, returning null for missing or malformed JSON.</summary>
    public static AttributeOptions? Parse(string? optionsJson)
    {
        if (string.IsNullOrWhiteSpace(optionsJson))
            return null;
        try
        {
            return JsonSerializer.Deserialize<AttributeOptions>(optionsJson, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Resolves the effective range for a data type. Date and period values are never allowed in the
    /// future unless an explicit maximum is configured; all other types are unbounded.
    /// </summary>
    public static DateRange Resolve(AttributeDataType dataType, string? optionsJson, DateOnly today) =>
        dataType is AttributeDataType.Date or AttributeDataType.Period
            ? Resolve(Parse(optionsJson), today)
            : DateRange.Unbounded;

    /// <summary>
    /// Combines absolute bounds with age-relative bounds. Relative bounds are evaluated against the
    /// current day so seeded configuration never goes stale; the most restrictive bound wins.
    /// <c>MaxAgeDays</c> yields the earliest allowed day, <c>MinAgeDays</c> the latest.
    /// </summary>
    public static DateRange Resolve(AttributeOptions? options, DateOnly today)
    {
        DateOnly? min = options?.MinDate;
        DateOnly? max = options?.MaxDate;

        if (options?.MaxAgeDays is { } maxAgeDays)
        {
            var bound = today.AddDays(-maxAgeDays);
            min = min is null || bound > min ? bound : min;
        }

        if (options?.MinAgeDays is { } minAgeDays)
        {
            var bound = today.AddDays(-minAgeDays);
            max = max is null || bound < max ? bound : max;
        }

        return new DateRange(min, max ?? today);
    }

    /// <summary>Checks the date options themselves, independent of the current day.</summary>
    public static string? ValidateOptions(AttributeDataType dataType, AttributeOptions? options)
    {
        if (options is null)
            return null;

        var usesDateOptions = options.MinDate is not null || options.MaxDate is not null ||
            options.MinAgeDays is not null || options.MaxAgeDays is not null;
        if (!usesDateOptions)
            return null;

        if (dataType != AttributeDataType.Date)
            return "Date bounds are supported only for date attributes.";

        if (options.MinDate is { } minDate && options.MaxDate is { } maxDate && minDate > maxDate)
            return "Minimum date cannot be after the maximum date.";

        if (options.MinAgeDays is { } minAge && minAge < 0)
            return "Minimum age in days cannot be negative.";
        if (options.MaxAgeDays is { } maxAge && maxAge < 0)
            return "Maximum age in days cannot be negative.";
        if (options.MinAgeDays is { } minAgeDays && options.MaxAgeDays is { } maxAgeDays && maxAgeDays < minAgeDays)
            return "Maximum age in days cannot be lower than the minimum age in days.";

        return null;
    }

    /// <summary>The effective upper bound, defaulting to the current day so future dates are rejected.</summary>
    public static DateOnly UpperBound(DateRange range, DateOnly today) => range.Max ?? today;

    /// <summary>Human-readable validation message for a date outside the range, or null when valid.</summary>
    public static string? ValidateValue(string label, DateOnly value, DateRange range, DateOnly today)
    {
        if (range.Min is { } min && value < min)
            return $"{label} is earlier than the earliest allowed date ({min:yyyy-MM-dd}).";

        var max = UpperBound(range, today);
        if (value > max)
            return max == today
                ? $"{label} cannot be in the future."
                : $"{label} is later than the latest allowed date ({max:yyyy-MM-dd}).";

        return null;
    }
}