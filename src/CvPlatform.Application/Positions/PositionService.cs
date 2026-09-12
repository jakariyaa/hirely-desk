using CvPlatform.Application.Authorization;
using CvPlatform.Application.Attributes;
using CvPlatform.Application.Common;
using CvPlatform.Application.Cvs;
using CvPlatform.Application.Validation;
using CvPlatform.Core.Data;
using CvPlatform.Core.Entities;
using CvPlatform.Core.Enums;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace CvPlatform.Application.Positions;

public sealed class PositionService(
    IAppDbContextFactory factory,
    IValidator<PositionInput>? validator = null) : IPositionService
{
    public async Task<Result<PagedResult<PositionDto>>> ListAsync(
        ActorContext actor, PageRequest page, string? search = null, CancellationToken ct = default)
    {
        if (!actor.IsPrivileged)
            return Result<PagedResult<PositionDto>>.Failure(ErrorCodes.Forbidden, "Only recruiters can manage positions.");
        await using var db = factory.CreateDbContext();
        var query = db.Positions.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(p =>
                p.Title.Contains(search) || p.ShortDescription.Contains(search) ||
                (p.Company != null && p.Company.Contains(search)));

        var total = await query.CountAsync(ct);
        var items = await query.OrderBy(p => p.Title).ThenBy(p => p.Id)
            .Skip(page.Skip).Take(page.PageSize)
            .Select(ToDtoExpression())
            .ToListAsync(ct);
        items = (await PositionStats.WithCountsAsync(db, items, ct)).ToList();

        return Result<PagedResult<PositionDto>>.Success(
            new PagedResult<PositionDto>(items, total, page.Page, page.PageSize));
    }

    public async Task<Result<PositionDto>> GetAsync(
        ActorContext actor, Guid positionId, CancellationToken ct = default)
    {
        if (!actor.IsPrivileged)
            return Result<PositionDto>.Failure(ErrorCodes.Forbidden, "Only recruiters can manage positions.");
        await using var db = factory.CreateDbContext();
        var item = await db.Positions.AsNoTracking()
            .Where(p => p.Id == positionId)
            .Select(ToDtoExpression())
            .SingleOrDefaultAsync(ct);
        return item is null
            ? Result<PositionDto>.Failure(ErrorCodes.NotFound, "Position was not found.")
            : Result<PositionDto>.Success(item);
    }

    public async Task<Result<PositionDto>> CreateAsync(
        ActorContext actor, PositionInput input, CancellationToken ct = default)
    {
        if (!actor.IsPrivileged)
            return Result<PositionDto>.Failure(ErrorCodes.Forbidden, "Only recruiters can create positions.");
        var validation = Validate(input);
        if (validation is not null)
            return Result<PositionDto>.Failure(ErrorCodes.ValidationFailed, validation);
        var attributeValidation = AttributeRequirementValidation.Validate(input.Attributes);
        if (attributeValidation is not null)
            return Result<PositionDto>.Failure(ErrorCodes.ValidationFailed, attributeValidation);

        await using var db = factory.CreateDbContext();
        if (input.Attributes is { Count: > 0 })
        {
            var definitionIds = input.Attributes.Select(a => a.AttributeDefinitionId).ToArray();
            var definitionCount = await db.AttributeDefinitions
                .CountAsync(d => definitionIds.Contains(d.Id), ct);
            if (definitionCount != definitionIds.Length)
                return Result<PositionDto>.Failure(
                    ErrorCodes.NotFound, "One or more attribute definitions were not found.");
        }
        var position = new Position
        {
            Id = Guid.NewGuid(),
            OwnerId = actor.UserId,
            Title = input.Title.Trim(),
            ShortDescription = input.ShortDescription?.Trim() ?? "",
            Company = input.Company?.Trim(),
            Level = input.Level?.Trim(),
            IsPublic = input.IsPublic,
            MaxProjects = input.MaxProjects,
            Attributes = input.Attributes?.Select(a => new PositionAttribute
            {
                AttributeDefinitionId = a.AttributeDefinitionId,
                IsRequired = a.IsRequired,
                SortOrder = a.SortOrder,
            }).ToList() ?? [],
        };
        db.Positions.Add(position);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            return Result<PositionDto>.Failure(ErrorCodes.Conflict, "Position could not be created.");
        }
        return await GetAsync(actor, position.Id, ct);
    }

    public async Task<Result<PositionDto>> UpdateAsync(
        ActorContext actor, Guid positionId, PositionInput input, CancellationToken ct = default)
    {
        if (!actor.IsPrivileged)
            return Result<PositionDto>.Failure(ErrorCodes.Forbidden, "Only recruiters can edit positions.");
        var validation = Validate(input);
        if (validation is not null)
            return Result<PositionDto>.Failure(ErrorCodes.ValidationFailed, validation);

        await using var db = factory.CreateDbContext();
        var position = await db.Positions.SingleOrDefaultAsync(p => p.Id == positionId, ct);
        if (position is null)
            return Result<PositionDto>.Failure(ErrorCodes.NotFound, "Position was not found.");
        if (input.ExpectedVersion is null || input.ExpectedVersion != position.Version)
            return Result<PositionDto>.Failure(
                ErrorCodes.ConcurrencyConflict, "The position was modified by someone else.");

        position.Title = input.Title.Trim();
        position.ShortDescription = input.ShortDescription?.Trim() ?? "";
        position.Company = input.Company?.Trim();
        position.Level = input.Level?.Trim();
        position.IsPublic = input.IsPublic;
        position.MaxProjects = input.MaxProjects;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<PositionDto>.Failure(
                ErrorCodes.ConcurrencyConflict, "The position was modified by someone else.");
        }
        return await GetAsync(actor, position.Id, ct);
    }

    public async Task<Result<PositionDeleteImpactDto>> GetDeleteImpactAsync(
        ActorContext actor, Guid positionId, CancellationToken ct = default)
    {
        if (!actor.IsPrivileged)
            return Result<PositionDeleteImpactDto>.Failure(ErrorCodes.Forbidden, "Only recruiters can delete positions.");
        await using var db = factory.CreateDbContext();
        var exists = await db.Positions.AsNoTracking().AnyAsync(p => p.Id == positionId, ct);
        if (!exists)
            return Result<PositionDeleteImpactDto>.Failure(ErrorCodes.NotFound, "Position was not found.");

        var cvCount = await db.Cvs.AsNoTracking()
            .CountAsync(c => c.PositionId == positionId, ct);
        var discussions = await db.DiscussionPosts.AsNoTracking().CountAsync(p => p.PositionId == positionId, ct);
        var likes = await db.CvLikes.AsNoTracking()
            .CountAsync(l => l.Cv.PositionId == positionId, ct);
        var projects = await db.CvProjects.AsNoTracking()
            .CountAsync(cp => cp.Cv.PositionId == positionId, ct);

        return Result<PositionDeleteImpactDto>.Success(new PositionDeleteImpactDto(cvCount, discussions, likes, projects));
    }

    public async Task<Result> DeleteAsync(
        ActorContext actor, Guid positionId, long expectedVersion, CancellationToken ct = default)
    {
        if (!actor.IsPrivileged)
            return Result.Failure(ErrorCodes.Forbidden, "Only recruiters can delete positions.");
        await using var db = factory.CreateDbContext();
        var position = await db.Positions.SingleOrDefaultAsync(p => p.Id == positionId, ct);
        if (position is null)
            return Result.Failure(ErrorCodes.NotFound, "Position was not found.");
        if (position.Version != expectedVersion)
            return Result.Failure(ErrorCodes.ConcurrencyConflict, "The position was modified by someone else.");
        db.Positions.Remove(position);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure(ErrorCodes.ConcurrencyConflict, "The position was modified by someone else.");
        }
        catch (DbUpdateException)
        {
            return Result.Failure(ErrorCodes.Conflict, "Position could not be deleted.");
        }
        return Result.Success();
    }

    public async Task<Result> SaveRuleAsync(
        ActorContext actor, Guid positionId, AccessRuleInput input, CancellationToken ct = default)
    {
        if (!actor.IsPrivileged)
            return Result.Failure(ErrorCodes.Forbidden, "Only recruiters can manage access rules.");
        await using var db = factory.CreateDbContext();
        var position = await db.Positions.Include(p => p.AccessRules)
            .SingleOrDefaultAsync(p => p.Id == positionId, ct);
        if (position is null)
            return Result.Failure(ErrorCodes.NotFound, "Position was not found.");
        if (input.ExpectedVersion is null || input.ExpectedVersion != position.Version)
            return Result.Failure(ErrorCodes.ConcurrencyConflict, "The position was modified by someone else.");

        var definition = await db.AttributeDefinitions.SingleOrDefaultAsync(
            d => d.Id == input.AttributeDefinitionId, ct);
        if (definition is null)
            return Result.Failure(ErrorCodes.NotFound, "Attribute definition was not found.");
        var comparison = input.ComparisonValue?.Trim() ?? "";
        var validation = AttributeValueRules.ValidateComparison(definition, input.Operator, comparison);
        if (validation is not null)
            return Result.Failure(ErrorCodes.ValidationFailed, validation);

        var rule = input.RuleId is { } ruleId
            ? position.AccessRules.SingleOrDefault(r => r.Id == ruleId)
            : null;
        if (input.RuleId is not null && rule is null)
            return Result.Failure(ErrorCodes.NotFound, "Access rule was not found.");
        var isNew = rule is null;
        rule ??= new AccessRule { Id = Guid.NewGuid(), PositionId = positionId };
        rule.AttributeDefinitionId = definition.Id;
        rule.DataType = definition.DataType;
        rule.Operator = input.Operator;
        rule.ComparisonValue = comparison;
        rule.NumericComparison = null;
        rule.DateComparison = null;
        if (definition.DataType == AttributeDataType.Numeric &&
            decimal.TryParse(comparison, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var numericComparison))
            rule.NumericComparison = numericComparison;
        if ((definition.DataType == AttributeDataType.Date || definition.DataType == AttributeDataType.Period) &&
            DateOnly.TryParse(comparison, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var dateComparison))
            rule.DateComparison = dateComparison;
        if (isNew)
            db.AccessRules.Add(rule);
        db.Entry(position).Property(p => p.Version).IsModified = true;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure(ErrorCodes.ConcurrencyConflict, "The position was modified by someone else.");
        }
        return Result.Success();
    }

    public async Task<Result<PositionDto>> AssignOwnerAsync(
        ActorContext actor, Guid positionId, Guid ownerId, long expectedVersion, CancellationToken ct = default)
    {
        if (!actor.IsAdmin)
            return Result<PositionDto>.Failure(ErrorCodes.Forbidden, "Admin access required.");

        await using var db = factory.CreateDbContext();
        var position = await db.Positions.SingleOrDefaultAsync(p => p.Id == positionId, ct);
        if (position is null)
            return Result<PositionDto>.Failure(ErrorCodes.NotFound, "Position was not found.");
        if (position.Version != expectedVersion)
            return Result<PositionDto>.Failure(
                ErrorCodes.ConcurrencyConflict, "The position was modified by someone else.");
        if (!await db.Users.AnyAsync(u => u.Id == ownerId, ct))
            return Result<PositionDto>.Failure(ErrorCodes.NotFound, "Owner was not found.");

        position.OwnerId = ownerId;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<PositionDto>.Failure(
                ErrorCodes.ConcurrencyConflict, "The position was modified by someone else.");
        }
        return await GetAsync(actor, position.Id, ct);
    }

    public async Task<Result> RemoveRuleAsync(
        ActorContext actor, Guid positionId, Guid ruleId, long expectedVersion, CancellationToken ct = default)
    {
        if (!actor.IsPrivileged)
            return Result.Failure(ErrorCodes.Forbidden, "Only recruiters can manage access rules.");
        await using var db = factory.CreateDbContext();
        var position = await db.Positions.Include(p => p.AccessRules)
            .SingleOrDefaultAsync(p => p.Id == positionId, ct);
        if (position is null)
            return Result.Failure(ErrorCodes.NotFound, "Position was not found.");
        if (position.Version != expectedVersion)
            return Result.Failure(ErrorCodes.ConcurrencyConflict, "The position was modified by someone else.");

        var rule = position.AccessRules.SingleOrDefault(r => r.Id == ruleId);
        if (rule is null)
            return Result.Failure(ErrorCodes.NotFound, "Access rule was not found.");
        db.AccessRules.Remove(rule);
        db.Entry(position).Property(p => p.Version).IsModified = true;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure(ErrorCodes.ConcurrencyConflict, "The position was modified by someone else.");
        }
        return Result.Success();
    }

    public async Task<Result> SaveAttributeAsync(
        ActorContext actor, Guid positionId, PositionAttributeInput input, CancellationToken ct = default)
    {
        if (!actor.IsPrivileged)
            return Result.Failure(ErrorCodes.Forbidden, "Only recruiters can edit positions.");
        await using var db = factory.CreateDbContext();
        var position = await db.Positions.Include(p => p.Attributes)
            .SingleOrDefaultAsync(p => p.Id == positionId, ct);
        if (position is null)
            return Result.Failure(ErrorCodes.NotFound, "Position was not found.");
        if (input.ExpectedVersion is { } expected && expected != position.Version)
            return Result.Failure(ErrorCodes.ConcurrencyConflict, "The position was modified by someone else.");

        if (!await db.AttributeDefinitions.AnyAsync(d => d.Id == input.AttributeDefinitionId, ct))
            return Result.Failure(ErrorCodes.NotFound, "Attribute definition was not found.");

        var attribute = position.Attributes.SingleOrDefault(a => a.AttributeDefinitionId == input.AttributeDefinitionId);
        var isNew = attribute is null;
        if (isNew && input.SortOrder is null)
        {
            input = input with
            {
                SortOrder = position.Attributes.Count == 0
                    ? 0
                    : position.Attributes.Max(a => a.SortOrder) + 1,
            };
        }
        else if (isNew && position.Attributes.Any(a => a.SortOrder == input.SortOrder))
        {
            return Result.Failure(ErrorCodes.ValidationFailed, "Another attribute already uses this order.");
        }

        if (isNew)
        {
            var created = new PositionAttribute { PositionId = positionId, AttributeDefinitionId = input.AttributeDefinitionId };
            position.Attributes.Add(created);
            attribute = created;
        }
        attribute!.IsRequired = input.IsRequired;
        if (input.SortOrder is { } sortOrder)
            attribute.SortOrder = sortOrder;
        db.Entry(position).Property(p => p.Version).IsModified = true;

        await using var saveAttributeTx = db.Database.ProviderName != "Microsoft.EntityFrameworkCore.InMemory"
            ? await db.Database.BeginTransactionAsync(ct)
            : null;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure(ErrorCodes.ConcurrencyConflict, "The position was modified by someone else.");
        }
        catch (DbUpdateException)
        {
            return Result.Failure(ErrorCodes.Conflict, "Attribute could not be saved.");
        }

        await RefreshPublishedSearchTextsAsync(db, positionId, ct);
        if (saveAttributeTx is not null)
            await saveAttributeTx.CommitAsync(ct);
        return Result.Success();
    }

    public async Task<Result> RemoveAttributeAsync(
        ActorContext actor, Guid positionId, Guid attributeDefinitionId, long expectedVersion, CancellationToken ct = default)
    {
        if (!actor.IsPrivileged)
            return Result.Failure(ErrorCodes.Forbidden, "Only recruiters can edit positions.");
        await using var db = factory.CreateDbContext();
        var position = await db.Positions.AsSplitQuery().Include(p => p.Attributes).Include(p => p.AccessRules)
            .SingleOrDefaultAsync(p => p.Id == positionId, ct);
        if (position is null)
            return Result.Failure(ErrorCodes.NotFound, "Position was not found.");
        if (position.Version != expectedVersion)
            return Result.Failure(ErrorCodes.ConcurrencyConflict, "The position was modified by someone else.");

        var attribute = position.Attributes.SingleOrDefault(a => a.AttributeDefinitionId == attributeDefinitionId);
        if (attribute is null)
            return Result.Failure(ErrorCodes.NotFound, "Attribute was not found on this position.");

        // §5: removing a template attribute also removes access rules referencing it, in the same save.
        var dependentRules = position.AccessRules
            .Where(r => r.AttributeDefinitionId == attributeDefinitionId)
            .ToList();
        foreach (var rule in dependentRules)
            db.AccessRules.Remove(rule);
        position.Attributes.Remove(attribute);
        db.PositionAttributes.Remove(attribute);
        db.Entry(position).Property(p => p.Version).IsModified = true;

        await using var removeAttributeTx = db.Database.ProviderName != "Microsoft.EntityFrameworkCore.InMemory"
            ? await db.Database.BeginTransactionAsync(ct)
            : null;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure(ErrorCodes.ConcurrencyConflict, "The position was modified by someone else.");
        }

        await RefreshPublishedSearchTextsAsync(db, positionId, ct);
        if (removeAttributeTx is not null)
            await removeAttributeTx.CommitAsync(ct);
        return Result.Success();
    }

    public async Task<Result<PositionDto>> DuplicateAsync(
        ActorContext actor, Guid positionId, CancellationToken ct = default)
    {
        if (!actor.IsPrivileged)
            return Result<PositionDto>.Failure(ErrorCodes.Forbidden, "Only recruiters can duplicate positions.");
        await using var db = factory.CreateDbContext();
        var source = await db.Positions.AsNoTracking()
            .AsSplitQuery()
            .Include(p => p.Attributes)
            .Include(p => p.AccessRules)
            .SingleOrDefaultAsync(p => p.Id == positionId, ct);
        if (source is null)
            return Result<PositionDto>.Failure(ErrorCodes.NotFound, "Position was not found.");

        var copyId = Guid.NewGuid();
        var copy = new Position
        {
            Id = copyId,
            OwnerId = actor.UserId,
            Title = $"{source.Title} (copy)",
            ShortDescription = source.ShortDescription,
            Company = source.Company,
            Level = source.Level,
            IsPublic = source.IsPublic,
            MaxProjects = source.MaxProjects,
            Attributes = source.Attributes.Select(a => new PositionAttribute
            {
                PositionId = copyId,
                AttributeDefinitionId = a.AttributeDefinitionId,
                IsRequired = a.IsRequired,
                SortOrder = a.SortOrder,
            }).ToList(),
            AccessRules = source.AccessRules.Select(r => new AccessRule
            {
                PositionId = copyId,
                AttributeDefinitionId = r.AttributeDefinitionId,
                DataType = r.DataType,
                Operator = r.Operator,
                ComparisonValue = r.ComparisonValue,
                NumericComparison = r.NumericComparison,
                DateComparison = r.DateComparison,
            }).ToList(),
        };
        db.Positions.Add(copy);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            return Result<PositionDto>.Failure(ErrorCodes.Conflict, "Position could not be duplicated.");
        }
        return await GetAsync(actor, copy.Id, ct);
    }

    public async Task<Result<PositionDetailDto>> GetForEditAsync(
        ActorContext actor, Guid positionId, CancellationToken ct = default)
    {
        if (!actor.IsPrivileged)
            return Result<PositionDetailDto>.Failure(ErrorCodes.Forbidden, "Only recruiters can edit positions.");
        await using var db = factory.CreateDbContext();
        var position = await db.Positions.AsNoTracking()
            .SingleOrDefaultAsync(p => p.Id == positionId, ct);
        if (position is null)
            return Result<PositionDetailDto>.Failure(ErrorCodes.NotFound, "Position was not found.");

        var attributes = await db.PositionAttributes.AsNoTracking()
            .Where(a => a.PositionId == positionId)
            .OrderBy(a => a.SortOrder).ThenBy(a => a.AttributeDefinitionId)
            .Select(a => new PositionAttributeDto(
                a.AttributeDefinitionId, a.AttributeDefinition.Name, a.AttributeDefinition.Category.Name,
                a.AttributeDefinition.DataType, a.IsRequired, a.SortOrder))
            .ToListAsync(ct);
        var rules = await db.AccessRules.AsNoTracking()
            .Where(r => r.PositionId == positionId)
            .OrderBy(r => r.AttributeDefinition.Name).ThenBy(r => r.Id)
            .Select(r => new AccessRuleDto(
                r.Id, r.AttributeDefinitionId, r.AttributeDefinition.Name, r.DataType, r.Operator, r.ComparisonValue))
            .ToListAsync(ct);

        return Result<PositionDetailDto>.Success(new PositionDetailDto(
            new PositionDto(position.Id, position.OwnerId, position.Title, position.ShortDescription,
                position.Company, position.Level, position.IsPublic, position.MaxProjects, position.Version),
            attributes, rules));
    }

    private string? Validate(PositionInput input)
    {
        var result = (validator ?? new PositionInputValidator()).Validate(input);
        return result.IsValid ? null : result.Errors[0].ErrorMessage;
    }

    private static System.Linq.Expressions.Expression<Func<Position, PositionDto>> ToDtoExpression() =>
        p => new PositionDto(
            p.Id, p.OwnerId, p.Title, p.ShortDescription, p.Company, p.Level,
            p.IsPublic, p.MaxProjects, p.Version);

    private async Task RefreshPublishedSearchTextsAsync(
        Core.Data.IAppDbContext db, Guid positionId, CancellationToken ct)
    {
        var published = await db.Cvs
            .AsSplitQuery()
            .Include(c => c.Position).ThenInclude(p => p.Attributes).ThenInclude(a => a.AttributeDefinition)
            .Include(c => c.Profile).ThenInclude(p => p.User)
            .Include(c => c.Profile).ThenInclude(p => p.AttributeValues).ThenInclude(v => v.AttributeDefinition)
            .Where(c => c.PositionId == positionId && c.Status == CvStatus.Published)
            .ToListAsync(ct);
        if (published.Count == 0)
            return;

        foreach (var cv in published)
            cv.SearchText = CvSearchTextBuilder.Build(cv);

        await db.SaveChangesAsync(ct);
    }
}
