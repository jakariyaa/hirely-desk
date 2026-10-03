using CvPlatform.Application.Common;
using CvPlatform.Core.Data;
using CvPlatform.Core.Entities;
using CvPlatform.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace CvPlatform.Application.Integration;

public interface IPositionSummaryService
{
    Task<Result<PositionSummaryDto>> GetSummaryAsync(
        Guid positionId, Guid? tokenId = null, CancellationToken ct = default);
}

/// <summary>
/// Builds the anonymized, aggregated view of a position for the external integration API.
/// Statistics are aggregated in SQL (GROUP BY) over the profiles that submitted a CV to the
/// position; top values are ranked in memory because per-group ordering is not part of EF Core's
/// GroupBy translation.
/// </summary>
public sealed class PositionSummaryService(
    IAppDbContextFactory factory,
    TimeProvider? timeProvider = null) : IPositionSummaryService
{
    internal const int TopValuesCount = 5;
    internal static readonly TimeSpan LastUsedThrottle = TimeSpan.FromMinutes(1);

    private DateTime Now => (timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime;

    public async Task<Result<PositionSummaryDto>> GetSummaryAsync(
        Guid positionId, Guid? tokenId = null, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();

        var position = await db.Positions.AsNoTracking()
            .SingleOrDefaultAsync(p => p.Id == positionId, ct);
        if (position is null)
            return Result<PositionSummaryDto>.Failure(ErrorCodes.NotFound, "Position was not found.");

        var attributes = await db.PositionAttributes.AsNoTracking()
            .Where(a => a.PositionId == positionId)
            .OrderBy(a => a.SortOrder).ThenBy(a => a.AttributeDefinitionId)
            .Select(a => new AttributeMeta(
                a.AttributeDefinitionId,
                a.AttributeDefinition.Name,
                a.AttributeDefinition.Category.Name,
                a.AttributeDefinition.DataType))
            .ToListAsync(ct);

        var cvCount = await db.Cvs.AsNoTracking().CountAsync(c => c.PositionId == positionId, ct);
        var summaries = await LoadAttributeSummariesAsync(db, positionId, attributes, ct);

        if (tokenId is { } id)
            await TouchLastUsedAsync(db, id, ct);

        return Result<PositionSummaryDto>.Success(new PositionSummaryDto(
            position.Id,
            position.Title,
            position.Company,
            position.Level,
            position.ShortDescription,
            cvCount,
            Now,
            summaries));
    }

    private async Task TouchLastUsedAsync(IAppDbContext db, Guid tokenId, CancellationToken ct)
    {
        var cutoff = Now - LastUsedThrottle;
        await db.PositionApiTokens
            .Where(t => t.Id == tokenId && (t.LastUsedAt == null || t.LastUsedAt < cutoff))
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.LastUsedAt, Now), ct);
    }

    private static async Task<IReadOnlyList<PositionSummaryAttributeDto>> LoadAttributeSummariesAsync(
        IAppDbContext db,
        Guid positionId,
        IReadOnlyList<AttributeMeta> attributes,
        CancellationToken ct)
    {
        var numeric = await LoadNumericAsync(db, positionId, IdsOf(attributes, AttributeDataType.Numeric), ct);
        var boolean = await LoadBooleanAsync(db, positionId, IdsOf(attributes, AttributeDataType.Boolean), ct);
        var date = await LoadDateAsync(db, positionId, IdsOf(attributes, AttributeDataType.Date), ct);
        var period = await LoadPeriodAsync(db, positionId, IdsOf(attributes, AttributeDataType.Period), ct);
        var imageCounts = await LoadImageCountsAsync(db, positionId, IdsOf(attributes, AttributeDataType.Image), ct);
        var frequencies = await LoadValueFrequenciesAsync(db, positionId, attributes, ct);

        return attributes
            .Select(a => ToDto(a, numeric, boolean, date, period, imageCounts, frequencies))
            .ToList();
    }

    private static List<Guid> IdsOf(IReadOnlyList<AttributeMeta> attributes, AttributeDataType dataType) =>
        attributes.Where(a => a.DataType == dataType).Select(a => a.Id).ToList();

    private static IQueryable<ProfileAttributeValue> Values(
        IAppDbContext db, Guid positionId, IReadOnlyList<Guid> ids) =>
        db.ProfileAttributeValues.AsNoTracking()
            .Where(v => ids.Contains(v.AttributeDefinitionId) &&
                        db.Cvs.Any(c => c.PositionId == positionId && c.ProfileId == v.ProfileId));

    private static async Task<Dictionary<Guid, NumericAggregateDto>> LoadNumericAsync(
        IAppDbContext db, Guid positionId, IReadOnlyList<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0)
            return [];
        var rows = await Values(db, positionId, ids)
            .Where(v => v.NumericValue != null)
            .GroupBy(v => v.AttributeDefinitionId)
            .Select(g => new NumericRow(
                g.Key,
                g.Count(),
                g.Average(v => v.NumericValue),
                g.Min(v => v.NumericValue),
                g.Max(v => v.NumericValue)))
            .ToListAsync(ct);
        return rows.ToDictionary(r => r.Id, r => new NumericAggregateDto(r.Count, r.Average, r.Min, r.Max));
    }

    private static async Task<Dictionary<Guid, BooleanAggregateDto>> LoadBooleanAsync(
        IAppDbContext db, Guid positionId, IReadOnlyList<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0)
            return [];
        var rows = await Values(db, positionId, ids)
            .Where(v => v.BooleanValue != null)
            .GroupBy(v => new { v.AttributeDefinitionId, v.BooleanValue })
            .Select(g => new BooleanValueRow(g.Key.AttributeDefinitionId, g.Key.BooleanValue, g.Count()))
            .ToListAsync(ct);
        return rows
            .GroupBy(r => r.Id)
            .ToDictionary(
                g => g.Key,
                g => new BooleanAggregateDto(
                    g.Where(r => r.Value == true).Sum(r => r.Count),
                    g.Where(r => r.Value == false).Sum(r => r.Count)));
    }

    private static async Task<Dictionary<Guid, DateAggregateDto>> LoadDateAsync(
        IAppDbContext db, Guid positionId, IReadOnlyList<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0)
            return [];
        var rows = await Values(db, positionId, ids)
            .Where(v => v.DateValue != null)
            .GroupBy(v => v.AttributeDefinitionId)
            .Select(g => new DateRow(
                g.Key,
                g.Count(),
                g.Min(v => v.DateValue),
                g.Max(v => v.DateValue)))
            .ToListAsync(ct);
        return rows.ToDictionary(r => r.Id, r => new DateAggregateDto(r.Count, r.Earliest, r.Latest));
    }

    private static async Task<Dictionary<Guid, PeriodAggregateDto>> LoadPeriodAsync(
        IAppDbContext db, Guid positionId, IReadOnlyList<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0)
            return [];
        var rows = await Values(db, positionId, ids)
            .Where(v => v.PeriodStart != null || v.PeriodEnd != null)
            .GroupBy(v => v.AttributeDefinitionId)
            .Select(g => new PeriodRow(
                g.Key,
                g.Count(),
                g.Min(v => v.PeriodStart),
                g.Max(v => v.PeriodEnd)))
            .ToListAsync(ct);
        return rows.ToDictionary(
            r => r.Id, r => new PeriodAggregateDto(r.Count, r.EarliestStart, r.LatestEnd));
    }

    private static async Task<Dictionary<Guid, int>> LoadImageCountsAsync(
        IAppDbContext db, Guid positionId, IReadOnlyList<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0)
            return [];
        var rows = await Values(db, positionId, ids)
            .Where(v => v.ImageObjectKey != null)
            .GroupBy(v => v.AttributeDefinitionId)
            .Select(g => new CountRow(g.Key, g.Count()))
            .ToListAsync(ct);
        return rows.ToDictionary(r => r.Id, r => r.Count);
    }

    private static async Task<Dictionary<Guid, ValueFrequencies>> LoadValueFrequenciesAsync(
        IAppDbContext db,
        Guid positionId,
        IReadOnlyList<AttributeMeta> attributes,
        CancellationToken ct)
    {
        var frequencies = new Dictionary<Guid, ValueFrequencies>();

        var stringIds = IdsOf(attributes, AttributeDataType.String);
        if (stringIds.Count > 0)
            Collect(frequencies, await Values(db, positionId, stringIds)
                .Where(v => v.StringValue != null)
                .GroupBy(v => new { v.AttributeDefinitionId, Value = v.StringValue! })
                .Select(g => new ValueCountRow(g.Key.AttributeDefinitionId, g.Key.Value, g.Count()))
                .ToListAsync(ct));

        var textIds = IdsOf(attributes, AttributeDataType.Text);
        if (textIds.Count > 0)
            Collect(frequencies, await Values(db, positionId, textIds)
                .Where(v => v.TextValue != null)
                .GroupBy(v => new { v.AttributeDefinitionId, Value = v.TextValue! })
                .Select(g => new ValueCountRow(g.Key.AttributeDefinitionId, g.Key.Value, g.Count()))
                .ToListAsync(ct));

        var dropdownIds = IdsOf(attributes, AttributeDataType.Dropdown);
        if (dropdownIds.Count > 0)
            Collect(frequencies, await Values(db, positionId, dropdownIds)
                .Where(v => v.DropdownOption != null)
                .GroupBy(v => new { v.AttributeDefinitionId, Value = v.DropdownOption! })
                .Select(g => new ValueCountRow(g.Key.AttributeDefinitionId, g.Key.Value, g.Count()))
                .ToListAsync(ct));

        return frequencies;
    }

    private static void Collect(
        Dictionary<Guid, ValueFrequencies> frequencies, List<ValueCountRow> rows)
    {
        foreach (var group in rows.GroupBy(r => r.AttributeDefinitionId))
        {
            if (!frequencies.TryGetValue(group.Key, out var values))
            {
                values = new ValueFrequencies();
                frequencies[group.Key] = values;
            }

            values.Total += group.Sum(r => r.Count);
            values.DistinctCount = group.Count();
            values.Top.AddRange(group
                .OrderByDescending(r => r.Count)
                .ThenBy(r => r.Value, StringComparer.Ordinal)
                .Take(TopValuesCount)
                .Select(r => new TopValueDto(r.Value, r.Count)));
        }
    }

    private static readonly NumericAggregateDto EmptyNumeric = new(0, null, null, null);
    private static readonly BooleanAggregateDto EmptyBoolean = new(0, 0);
    private static readonly DateAggregateDto EmptyDate = new(0, null, null);
    private static readonly PeriodAggregateDto EmptyPeriod = new(0, null, null);

    private static PositionSummaryAttributeDto ToDto(
        AttributeMeta attribute,
        IReadOnlyDictionary<Guid, NumericAggregateDto> numeric,
        IReadOnlyDictionary<Guid, BooleanAggregateDto> boolean,
        IReadOnlyDictionary<Guid, DateAggregateDto> date,
        IReadOnlyDictionary<Guid, PeriodAggregateDto> period,
        IReadOnlyDictionary<Guid, int> imageCounts,
        IReadOnlyDictionary<Guid, ValueFrequencies> frequencies)
    {
        return attribute.DataType switch
        {
            AttributeDataType.Numeric => new PositionSummaryAttributeDto(
                attribute.Name, attribute.Category, attribute.DataType,
                numeric.TryGetValue(attribute.Id, out var n) ? n.Count : 0,
                Numeric: n ?? EmptyNumeric),

            AttributeDataType.Boolean => new PositionSummaryAttributeDto(
                attribute.Name, attribute.Category, attribute.DataType,
                boolean.TryGetValue(attribute.Id, out var b) ? b.TrueCount + b.FalseCount : 0,
                Boolean: b ?? EmptyBoolean),

            AttributeDataType.Date => new PositionSummaryAttributeDto(
                attribute.Name, attribute.Category, attribute.DataType,
                date.TryGetValue(attribute.Id, out var d) ? d.Count : 0,
                Date: d ?? EmptyDate),

            AttributeDataType.Period => new PositionSummaryAttributeDto(
                attribute.Name, attribute.Category, attribute.DataType,
                period.TryGetValue(attribute.Id, out var p) ? p.Count : 0,
                Period: p ?? EmptyPeriod),

            AttributeDataType.Image => new PositionSummaryAttributeDto(
                attribute.Name, attribute.Category, attribute.DataType,
                imageCounts.TryGetValue(attribute.Id, out var c) ? c : 0),

            AttributeDataType.String or AttributeDataType.Text or AttributeDataType.Dropdown =>
                new PositionSummaryAttributeDto(
                    attribute.Name, attribute.Category, attribute.DataType,
                    frequencies.TryGetValue(attribute.Id, out var f) ? f.Total : 0,
                    TopValues: f?.Top ?? [],
                    DistinctValueCount: f?.DistinctCount ?? 0),

            _ => new PositionSummaryAttributeDto(
                attribute.Name, attribute.Category, attribute.DataType, 0),
        };
    }

    private sealed record AttributeMeta(Guid Id, string Name, string Category, AttributeDataType DataType);

    private sealed record NumericRow(Guid Id, int Count, decimal? Average, decimal? Min, decimal? Max);

    private sealed record BooleanValueRow(Guid Id, bool? Value, int Count);

    private sealed record DateRow(Guid Id, int Count, DateOnly? Earliest, DateOnly? Latest);

    private sealed record PeriodRow(Guid Id, int Count, DateOnly? EarliestStart, DateOnly? LatestEnd);

    private sealed record CountRow(Guid Id, int Count);

    private sealed record ValueCountRow(Guid AttributeDefinitionId, string Value, int Count);

    private sealed class ValueFrequencies
    {
        public int Total { get; set; }
        public int DistinctCount { get; set; }
        public List<TopValueDto> Top { get; } = [];
    }
}
