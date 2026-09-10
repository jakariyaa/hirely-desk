using CvPlatform.Core.Enums;

namespace CvPlatform.Core.Access;

public readonly record struct TypedValue(
    AttributeDataType DataType,
    string? StringValue = null,
    string? TextValue = null,
    decimal? NumericValue = null,
    DateOnly? DateValue = null,
    DateOnly? PeriodStart = null,
    bool? BooleanValue = null,
    string? DropdownOption = null);
