using System.Text.Json.Serialization;
using CvPlatform.Core.Enums;

namespace CvPlatform.Application.Integration;

/// <summary>
/// Aggregated, anonymized view of one position for the external integration API (Odoo).
/// Only statistics leave the platform; individual candidate values never do.
/// </summary>
public sealed record PositionSummaryDto(
    Guid PositionId,
    string Title,
    string? Company,
    string? Level,
    string ShortDescription,
    int CvCount,
    DateTime GeneratedAt,
    IReadOnlyList<PositionSummaryAttributeDto> Attributes);

/// <summary>
/// One position attribute with its type and the aggregate that type supports. Sections that do
/// not apply to <see cref="DataType"/> stay null so the JSON contract is stable.
/// </summary>
public sealed record PositionSummaryAttributeDto(
    string Name,
    string Category,
    [property: JsonConverter(typeof(JsonStringEnumConverter<AttributeDataType>))] AttributeDataType DataType,
    int FilledCount,
    NumericAggregateDto? Numeric = null,
    BooleanAggregateDto? Boolean = null,
    DateAggregateDto? Date = null,
    PeriodAggregateDto? Period = null,
    IReadOnlyList<TopValueDto>? TopValues = null,
    int? DistinctValueCount = null);

/// <summary>Average/min/max for numeric attributes.</summary>
public sealed record NumericAggregateDto(int Count, decimal? Average, decimal? Min, decimal? Max);

/// <summary>True/false split for boolean attributes.</summary>
public sealed record BooleanAggregateDto(int TrueCount, int FalseCount);

/// <summary>Earliest/latest date for date attributes.</summary>
public sealed record DateAggregateDto(int Count, DateOnly? Earliest, DateOnly? Latest);

/// <summary>Earliest start and latest end for period attributes.</summary>
public sealed record PeriodAggregateDto(int Count, DateOnly? EarliestStart, DateOnly? LatestEnd);

/// <summary>A most popular value plus how many candidates reported it.</summary>
public sealed record TopValueDto(string Value, int Count);
