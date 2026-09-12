using CvPlatform.Application.Authorization;
using CvPlatform.Application.Common;
using CvPlatform.Application.Cvs;
using CvPlatform.Application.Validation;
using CvPlatform.Core.Data;
using CvPlatform.Core.Entities;
using CvPlatform.Core.Enums;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace CvPlatform.Application.Attributes;

public sealed class AttributeDefinitionService(
    IAppDbContextFactory factory,
    IValidator<AttributeDefinitionInput>? validator = null) : IAttributeDefinitionService
{
    public async Task<Result<PagedResult<AttributeDefinitionAdminDto>>> ListAsync(
        ActorContext actor, AttributeCatalogQuery request, CancellationToken ct = default)
    {
        if (!actor.IsPrivileged)
            return Result<PagedResult<AttributeDefinitionAdminDto>>.Failure(ErrorCodes.Forbidden, "Recruiter access required.");

        var page = request.Page ?? new PageRequest();
        await using var db = factory.CreateDbContext();
        var query = db.AttributeDefinitions.AsNoTracking().Include(d => d.Category).AsQueryable();
        if (request.CategoryId is { } categoryId)
            query = query.Where(d => d.CategoryId == categoryId);
        if (!string.IsNullOrWhiteSpace(request.Search))
            query = query.Where(d => d.Name.Contains(request.Search));
        if (!string.IsNullOrWhiteSpace(request.Prefix))
            query = query.Where(d => d.Name.StartsWith(request.Prefix));

        var total = await query.CountAsync(ct);
        var items = await query.OrderBy(d => d.Name).ThenBy(d => d.Id)
            .Skip(page.Skip).Take(page.PageSize)
            .Select(d => new AttributeDefinitionAdminDto(
                d.Id, d.CategoryId, d.Category.Name, d.Name, d.Description, d.DataType,
                d.IsBuiltIn, d.OptionsJson, d.Version))
            .ToListAsync(ct);

        return Result<PagedResult<AttributeDefinitionAdminDto>>.Success(
            new PagedResult<AttributeDefinitionAdminDto>(items, total, page.Page, page.PageSize));
    }

    public async Task<Result<AttributeDefinitionAdminDto>> CreateAsync(
        ActorContext actor, AttributeDefinitionInput input, CancellationToken ct = default)
    {
        if (!actor.IsPrivileged)
            return Result<AttributeDefinitionAdminDto>.Failure(ErrorCodes.Forbidden, "Recruiter access required.");
        var validation = Validate(input);
        if (validation is not null)
            return Result<AttributeDefinitionAdminDto>.Failure(ErrorCodes.ValidationFailed, validation);

        await using var db = factory.CreateDbContext();
        if (!await db.AttributeCategories.AnyAsync(c => c.Id == input.CategoryId, ct))
            return Result<AttributeDefinitionAdminDto>.Failure(ErrorCodes.NotFound, "Attribute category was not found.");
        var normalizedName = input.Name.Trim().ToLowerInvariant();
        if (await db.AttributeDefinitions.AnyAsync(d => d.Name.ToLower() == normalizedName, ct))
            return Result<AttributeDefinitionAdminDto>.Failure(ErrorCodes.Conflict, "Attribute name already exists.");

        var definition = new AttributeDefinition
        {
            Id = Guid.NewGuid(),
            CategoryId = input.CategoryId,
            Name = input.Name.Trim(),
            Description = input.Description?.Trim(),
            DataType = input.DataType,
            OptionsJson = input.OptionsJson?.Trim()
        };
        db.AttributeDefinitions.Add(definition);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { return Result<AttributeDefinitionAdminDto>.Failure(ErrorCodes.Conflict, "Attribute could not be created."); }
        return await GetAsync(db, definition.Id, ct);
    }

    public async Task<Result<AttributeDefinitionAdminDto>> UpdateAsync(
        ActorContext actor, Guid id, AttributeDefinitionInput input, CancellationToken ct = default)
    {
        if (!actor.IsPrivileged)
            return Result<AttributeDefinitionAdminDto>.Failure(ErrorCodes.Forbidden, "Recruiter access required.");
        var validation = Validate(input);
        if (validation is not null)
            return Result<AttributeDefinitionAdminDto>.Failure(ErrorCodes.ValidationFailed, validation);

        await using var db = factory.CreateDbContext();
        var definition = await db.AttributeDefinitions.SingleOrDefaultAsync(d => d.Id == id, ct);
        if (definition is null)
            return Result<AttributeDefinitionAdminDto>.Failure(ErrorCodes.NotFound, "Attribute definition was not found.");
        if (!await db.AttributeCategories.AnyAsync(c => c.Id == input.CategoryId, ct))
            return Result<AttributeDefinitionAdminDto>.Failure(ErrorCodes.NotFound, "Attribute category was not found.");
        if (input.ExpectedVersion is null || input.ExpectedVersion != definition.Version)
            return Result<AttributeDefinitionAdminDto>.Failure(ErrorCodes.ConcurrencyConflict, "The attribute was modified by someone else.");
        var normalizedUpdateName = input.Name.Trim().ToLowerInvariant();
        if (await db.AttributeDefinitions.AnyAsync(d => d.Id != id && d.Name.ToLower() == normalizedUpdateName, ct))
            return Result<AttributeDefinitionAdminDto>.Failure(ErrorCodes.Conflict, "Attribute name already exists.");

        if (input.DataType != definition.DataType)
        {
            var hasValues = await db.ProfileAttributeValues.AnyAsync(
                v => v.AttributeDefinitionId == id, ct);
            var hasRules = await db.AccessRules.AnyAsync(
                r => r.AttributeDefinitionId == id, ct);
            if (hasValues || hasRules)
                return Result<AttributeDefinitionAdminDto>.Failure(
                    ErrorCodes.Conflict,
                    "Attribute type cannot change while profile values or access rules reference it.");
        }

        var removed = RemovedChoices(definition.DataType, definition.OptionsJson, input.DataType, input.OptionsJson);
        if (removed.Count > 0 && !actor.IsAdmin)
            return Result<AttributeDefinitionAdminDto>.Failure(
                ErrorCodes.Forbidden, "Admin access required to remove shared dropdown options.");
        if (removed.Count > 0 && !input.ForceOptionRemoval)
        {
            var values = await db.ProfileAttributeValues.CountAsync(
                v => v.AttributeDefinitionId == id && v.DropdownOption != null && removed.Contains(v.DropdownOption), ct);
            var rules = await db.AccessRules.CountAsync(
                r => r.AttributeDefinitionId == id && removed.Contains(r.ComparisonValue), ct);
            if (values > 0 || rules > 0)
                return Result<AttributeDefinitionAdminDto>.Failure(ErrorCodes.Conflict,
                    $"{removed.Count} option(s) are referenced by {values} profile value(s) and {rules} access rule(s): {string.Join(", ", removed)}. Confirm removal to proceed.");
        }

        definition.CategoryId = input.CategoryId;
        definition.Name = input.Name.Trim();
        definition.Description = input.Description?.Trim();
        definition.DataType = input.DataType;
        definition.OptionsJson = input.OptionsJson?.Trim();
        await using var transaction = db.Database.ProviderName != "Microsoft.EntityFrameworkCore.InMemory"
            ? await db.Database.BeginTransactionAsync(ct)
            : null;
        var publishedCvs = await db.Cvs
            .Include(c => c.Position).ThenInclude(p => p.Attributes).ThenInclude(a => a.AttributeDefinition)
            .Include(c => c.Profile).ThenInclude(p => p.User)
            .Include(c => c.Profile).ThenInclude(p => p.AttributeValues).ThenInclude(v => v.AttributeDefinition)
            .AsSplitQuery()
            .Where(c => c.Status == CvStatus.Published &&
                (c.Position.Attributes.Any(a => a.AttributeDefinitionId == id) ||
                 c.Profile.AttributeValues.Any(v => v.AttributeDefinitionId == id)))
            .ToListAsync(ct);
        foreach (var cv in publishedCvs)
            cv.SearchText = CvSearchTextBuilder.Build(cv);

        try
        {
            await db.SaveChangesAsync(ct);
            if (transaction is not null)
                await transaction.CommitAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<AttributeDefinitionAdminDto>.Failure(
                ErrorCodes.ConcurrencyConflict, "The attribute was modified by someone else.");
        }
        catch (DbUpdateException)
        {
            return Result<AttributeDefinitionAdminDto>.Failure(
                ErrorCodes.Conflict, "Attribute name already exists.");
        }
        return await GetAsync(db, id, ct);
    }

    public async Task<Result<AttributeDeleteImpactDto>> GetDeleteImpactAsync(
        ActorContext actor, Guid id, CancellationToken ct = default)
    {
        if (!actor.IsAdmin)
            return Result<AttributeDeleteImpactDto>.Failure(ErrorCodes.Forbidden, "Admin access required.");
        await using var db = factory.CreateDbContext();
        if (!await db.AttributeDefinitions.AnyAsync(d => d.Id == id, ct))
            return Result<AttributeDeleteImpactDto>.Failure(ErrorCodes.NotFound, "Attribute definition was not found.");

        var positions = await db.PositionAttributes.Where(a => a.AttributeDefinitionId == id)
            .Select(a => a.PositionId).Distinct().ToListAsync(ct);
        var restricted = await db.AccessRules
            .Where(r => !r.Position.IsPublic && r.AttributeDefinitionId == id)
            .Select(r => r.PositionId)
            .Distinct()
            .Where(positionId => !db.AccessRules.Any(r =>
                r.PositionId == positionId && r.AttributeDefinitionId != id))
            .CountAsync(ct);
        var cvCount = await db.Cvs.CountAsync(c =>
            positions.Contains(c.PositionId) || db.ProfileAttributeValues.Any(v =>
                v.ProfileId == c.ProfileId && v.AttributeDefinitionId == id), ct);
        return Result<AttributeDeleteImpactDto>.Success(new AttributeDeleteImpactDto(
            await db.ProfileAttributeValues.CountAsync(v => v.AttributeDefinitionId == id, ct),
            positions.Count,
            await db.AccessRules.CountAsync(r => r.AttributeDefinitionId == id, ct),
            cvCount,
            restricted));
    }

    public async Task<Result<AttributeOptionImpactDto>> GetOptionChangeImpactAsync(
        ActorContext actor, Guid id, AttributeDefinitionInput input, CancellationToken ct = default)
    {
        if (!actor.IsAdmin)
            return Result<AttributeOptionImpactDto>.Failure(ErrorCodes.Forbidden, "Admin access required.");
        await using var db = factory.CreateDbContext();
        var definition = await db.AttributeDefinitions.AsNoTracking().SingleOrDefaultAsync(d => d.Id == id, ct);
        if (definition is null)
            return Result<AttributeOptionImpactDto>.Failure(ErrorCodes.NotFound, "Attribute definition was not found.");
        var removed = RemovedChoices(definition.DataType, definition.OptionsJson, input.DataType, input.OptionsJson);
        if (removed.Count == 0)
            return Result<AttributeOptionImpactDto>.Success(new AttributeOptionImpactDto([], 0, 0));
        var values = await db.ProfileAttributeValues.CountAsync(
            v => v.AttributeDefinitionId == id && v.DropdownOption != null && removed.Contains(v.DropdownOption), ct);
        var rules = await db.AccessRules.CountAsync(
            r => r.AttributeDefinitionId == id && removed.Contains(r.ComparisonValue), ct);
        return Result<AttributeOptionImpactDto>.Success(new AttributeOptionImpactDto(removed, values, rules));
    }

    public async Task<Result> DeleteAsync(ActorContext actor, Guid id, long expectedVersion, CancellationToken ct = default)
    {
        if (!actor.IsAdmin)
            return Result.Failure(ErrorCodes.Forbidden, "Admin access required.");
        await using var db = factory.CreateDbContext();
        var definition = await db.AttributeDefinitions.SingleOrDefaultAsync(d => d.Id == id, ct);
        if (definition is null)
            return Result.Failure(ErrorCodes.NotFound, "Attribute definition was not found.");
        if (definition.IsBuiltIn)
            return Result.Failure(ErrorCodes.Forbidden, "Built-in attributes cannot be deleted.");
        if (definition.Version != expectedVersion)
            return Result.Failure(ErrorCodes.ConcurrencyConflict, "The attribute was modified by someone else.");

        await using var transaction = db.Database.ProviderName != "Microsoft.EntityFrameworkCore.InMemory"
            ? await db.Database.BeginTransactionAsync(ct)
            : null;
        var publishedCvs = await db.Cvs
            .Include(c => c.Position).ThenInclude(p => p.Attributes).ThenInclude(a => a.AttributeDefinition)
            .Include(c => c.Profile).ThenInclude(p => p.User)
            .Include(c => c.Profile).ThenInclude(p => p.AttributeValues).ThenInclude(v => v.AttributeDefinition)
            .AsSplitQuery()
            .Where(c => c.Status == CvStatus.Published &&
                (c.Position.Attributes.Any(a => a.AttributeDefinitionId == id) ||
                 c.Profile.AttributeValues.Any(v => v.AttributeDefinitionId == id)))
            .ToListAsync(ct);
        foreach (var cv in publishedCvs)
            cv.SearchText = CvSearchTextBuilder.Build(cv, id);

        db.AttributeDefinitions.Remove(definition);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { return Result.Failure(ErrorCodes.ConcurrencyConflict, "The attribute was modified by someone else."); }
        if (transaction is not null)
            await transaction.CommitAsync(ct);
        return Result.Success();
    }

    private static async Task<Result<AttributeDefinitionAdminDto>> GetAsync(
        IAppDbContext db, Guid id, CancellationToken ct)
    {
        var dto = await db.AttributeDefinitions.AsNoTracking().Where(d => d.Id == id)
            .Select(d => new AttributeDefinitionAdminDto(
                d.Id, d.CategoryId, d.Category.Name, d.Name, d.Description, d.DataType,
                d.IsBuiltIn, d.OptionsJson, d.Version)).SingleAsync(ct);
        return Result<AttributeDefinitionAdminDto>.Success(dto);
    }

    private string? Validate(AttributeDefinitionInput input)
    {
        var result = (validator ?? new AttributeDefinitionInputValidator()).Validate(input);
        return result.IsValid ? null : result.Errors[0].ErrorMessage;
    }

    private static IReadOnlyList<string> RemovedChoices(
        Core.Enums.AttributeDataType oldType, string? oldJson,
        Core.Enums.AttributeDataType newType, string? newJson)
    {
        var oldChoices = ParseChoices(oldType, oldJson);
        if (oldChoices.Count == 0)
            return [];
        var newChoices = ParseChoices(newType, newJson);
        return oldChoices.Where(c => !newChoices.Contains(c, StringComparer.Ordinal)).ToList();
    }

    private static IReadOnlyList<string> ParseChoices(Core.Enums.AttributeDataType dataType, string? optionsJson)
    {
        if (dataType != Core.Enums.AttributeDataType.Dropdown || string.IsNullOrWhiteSpace(optionsJson))
            return [];
        try
        {
            var shape = System.Text.Json.JsonSerializer.Deserialize<DropdownShape>(
                optionsJson, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return shape?.Choices?.Select(c => c.Trim()).Where(c => c.Length > 0).Distinct(StringComparer.Ordinal).ToList() ?? [];
        }
        catch (System.Text.Json.JsonException)
        {
            return [];
        }
    }

    private sealed record DropdownShape(string[]? Choices);
}
