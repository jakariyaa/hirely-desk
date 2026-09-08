using System.Text.Json;
using CvPlatform.Application.Common;
using CvPlatform.Core.Data;
using CvPlatform.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace CvPlatform.Application.Attributes;

public sealed class AttributeCatalogService(IAppDbContextFactory factory) : IAttributeCatalog
{
    private static readonly JsonSerializerOptions OptionsJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public async Task<Result<IReadOnlyList<AttributeCategoryDto>>> GetCatalogAsync(CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();
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
                        ParseChoices(d.DataType, d.OptionsJson)))
                    .ToList()))
            .ToListAsync(ct);

        return Result<IReadOnlyList<AttributeCategoryDto>>.Success(categories);
    }

    public async Task<Result<PagedResult<AttributeDefinitionDto>>> SearchAsync(
        AttributeCatalogQuery request, CancellationToken ct = default)
    {
        var page = request.Page ?? new PageRequest();
        await using var db = factory.CreateDbContext();
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
                d.Id, d.Name, d.DataType, d.IsBuiltIn, ParseChoices(d.DataType, d.OptionsJson)))
            .ToListAsync(ct);
        return Result<PagedResult<AttributeDefinitionDto>>.Success(
            new PagedResult<AttributeDefinitionDto>(items, total, page.Page, page.PageSize));
    }

    public async Task<Result<IReadOnlyList<DropdownChoice>>> GetChoicesAsync(
        Guid attributeDefinitionId, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();
        var definition = await db.AttributeDefinitions
            .AsNoTracking()
            .SingleOrDefaultAsync(d => d.Id == attributeDefinitionId, ct);

        if (definition is null)
            return Result<IReadOnlyList<DropdownChoice>>.Failure(
                ErrorCodes.NotFound, $"Attribute definition {attributeDefinitionId} was not found.");

        return Result<IReadOnlyList<DropdownChoice>>.Success(
            ParseChoices(definition.DataType, definition.OptionsJson));
    }

    private static List<DropdownChoice> ParseChoices(AttributeDataType dataType, string? optionsJson) =>
        dataType == AttributeDataType.Dropdown && !string.IsNullOrWhiteSpace(optionsJson)
            ? TryParse(optionsJson)
            : [];

    private static List<DropdownChoice> TryParse(string optionsJson)
    {
        try
        {
            return JsonSerializer.Deserialize<DropdownOptionsShape>(optionsJson, OptionsJsonOptions)?
                .Choices?.Select(c => new DropdownChoice(c)).ToList() ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private sealed record DropdownOptionsShape(string[]? Choices);
}
