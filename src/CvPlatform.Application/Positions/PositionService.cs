using CvPlatform.Application.Authorization;
using CvPlatform.Application.Attributes;
using CvPlatform.Application.Common;
using CvPlatform.Core.Data;
using CvPlatform.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace CvPlatform.Application.Positions;

public sealed class PositionService(IAppDbContextFactory factory) : IPositionService
{
    public async Task<Result<PagedResult<PositionDto>>> ListAsync(
        ActorContext actor, PageRequest page, string? search = null, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();
        var query = db.Positions.AsNoTracking()
            .Where(p => actor.IsAdmin || (p.OwnerId.HasValue && p.OwnerId == actor.UserId));
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(p => p.Title.Contains(search) || p.ShortDescription.Contains(search));

        var total = await query.CountAsync(ct);
        var items = await query.OrderBy(p => p.Title).ThenBy(p => p.Id)
            .Skip(page.Skip).Take(page.PageSize)
            .Select(ToDtoExpression())
            .ToListAsync(ct);

        return Result<PagedResult<PositionDto>>.Success(
            new PagedResult<PositionDto>(items, total, page.Page, page.PageSize));
    }

    public async Task<Result<PositionDto>> GetAsync(
        ActorContext actor, Guid positionId, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();
        var item = await db.Positions.AsNoTracking()
            .Where(p => p.Id == positionId && (actor.IsAdmin || (p.OwnerId.HasValue && p.OwnerId == actor.UserId)))
            .Select(ToDtoExpression())
            .SingleOrDefaultAsync(ct);
        return item is null
            ? Result<PositionDto>.Failure(ErrorCodes.NotFound, "Position was not found.")
            : Result<PositionDto>.Success(item);
    }

    public async Task<Result<PositionDto>> CreateAsync(
        ActorContext actor, PositionInput input, CancellationToken ct = default)
    {
        var validation = Validate(input);
        if (validation is not null)
            return Result<PositionDto>.Failure(ErrorCodes.ValidationFailed, validation);

        await using var db = factory.CreateDbContext();
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
        var validation = Validate(input);
        if (validation is not null)
            return Result<PositionDto>.Failure(ErrorCodes.ValidationFailed, validation);

        await using var db = factory.CreateDbContext();
        var position = await db.Positions.SingleOrDefaultAsync(
            p => p.Id == positionId && (actor.IsAdmin || (p.OwnerId.HasValue && p.OwnerId == actor.UserId)), ct);
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

    public async Task<Result> DeleteAsync(
        ActorContext actor, Guid positionId, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();
        var position = await db.Positions.SingleOrDefaultAsync(
            p => p.Id == positionId && (actor.IsAdmin || p.OwnerId == actor.UserId), ct);
        if (position is null)
            return Result.Failure(ErrorCodes.NotFound, "Position was not found.");
        db.Positions.Remove(position);
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

    public async Task<Result> SaveRuleAsync(
        ActorContext actor, Guid positionId, AccessRuleInput input, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();
        var position = await db.Positions.Include(p => p.AccessRules)
            .SingleOrDefaultAsync(
                p => p.Id == positionId && (actor.IsAdmin || (p.OwnerId.HasValue && p.OwnerId == actor.UserId)), ct);
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

    private static string? Validate(PositionInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Title) || input.Title.Trim().Length > 200)
            return "Position title is required and cannot exceed 200 characters.";
        if (input.ShortDescription?.Length > 2_000)
            return "Position description cannot exceed 2000 characters.";
        if (input.Company?.Length > 200 || input.Level?.Length > 100)
            return "Position metadata is too long.";
        if (input.MaxProjects is < 0 or > 100)
            return "Maximum projects must be between 0 and 100.";
        return null;
    }

    private static System.Linq.Expressions.Expression<Func<Position, PositionDto>> ToDtoExpression() =>
        p => new PositionDto(
            p.Id, p.OwnerId, p.Title, p.ShortDescription, p.Company, p.Level,
            p.IsPublic, p.MaxProjects, p.Version);
}
