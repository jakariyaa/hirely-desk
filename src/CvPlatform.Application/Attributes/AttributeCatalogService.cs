using CvPlatform.Application.Common;
using CvPlatform.Core.Data;
using CvPlatform.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace CvPlatform.Application.Attributes;

public sealed class AttributeCatalogService(
    IAppDbContextFactory factory, TimeProvider? timeProvider = null) : IAttributeCatalog
{
    public async Task<Result<IReadOnlyList<AttributeCategoryDto>>> GetCatalogAsync(CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();
        var today = AttributeDateRules.TodayFrom(timeProvider);
        var categories = await db.AttributeCategories
            .AsNoTracking()
            .OrderBy(c => c.Name)
            .Select(c => new AttributeCategoryDto(
                c.Id,
                c.Name,
                c.Definitions
                    .OrderBy(d => d.Name)
                    .Select(d => new AttributeDefinitionDto(
                        d.Id,
                        d.Name,
                        d.DataType,
                        d.IsBuiltIn,
                        ParseChoices(d.DataType, d.OptionsJson),
                        ParseDateRange(d.DataType, d.OptionsJson, today)))
                    .ToList()))
            .ToListAsync(ct);

        return Result<IReadOnlyList<AttributeCategoryDto>>.Success(categories);
    }

    public async Task<Result<PagedResult<AttributeDefinitionDto>>> SearchAsync(
        AttributeCatalogQuery request, CancellationToken ct = default)
    {
        var page = request.Page ?? new PageRequest();
        await using var db = factory.CreateDbContext();
        var today = AttributeDateRules.TodayFrom(timeProvider);
        var query = db.AttributeDefinitions.AsNoTracking().AsQueryable();
        if (request.CategoryId is { } categoryId)
            query = query.Where(d => d.CategoryId == categoryId);
        if (!string.IsNullOrWhiteSpace(request.Search))
            query = query.Where(d => d.Name.Contains(request.Search));
        if (!string.IsNullOrWhiteSpace(request.Prefix))
            query = query.Where(d => d.Name.StartsWith(request.Prefix));
        var total = await query.CountAsync(ct);
        var items = await query.OrderBy(d => d.Name).ThenBy(d => d.Id)
            .Skip(page.Skip).Take(page.PageSize)
            .Select(d => new AttributeDefinitionDto(
                d.Id, d.Name, d.DataType, d.IsBuiltIn, ParseChoices(d.DataType, d.OptionsJson),
                ParseDateRange(d.DataType, d.OptionsJson, today)))
            .ToListAsync(ct);
        return Result<PagedResult<AttributeDefinitionDto>>.Success(
            new PagedResult<AttributeDefinitionDto>(items, total, page.Page, page.PageSize));
    }

    private static List<DropdownChoice> ParseChoices(AttributeDataType dataType, string? optionsJson) =>
        dataType == AttributeDataType.Dropdown
            ? AttributeValueRules.Choices(optionsJson).Select(c => new DropdownChoice(c)).ToList()
            : [];

    private static DateRangeDto? ParseDateRange(
        AttributeDataType dataType, string? optionsJson, DateOnly today)
    {
        if (dataType is not AttributeDataType.Date and not AttributeDataType.Period)
            return null;

        var range = AttributeDateRules.Resolve(dataType, optionsJson, today);
        return new DateRangeDto(range.Min, AttributeDateRules.UpperBound(range, today));
    }
}
