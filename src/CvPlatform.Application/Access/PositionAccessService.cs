using CvPlatform.Application.Authorization;
using CvPlatform.Application.Common;
using CvPlatform.Application.Positions;
using CvPlatform.Core.Access;
using CvPlatform.Core.Data;
using CvPlatform.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace CvPlatform.Application.Access;

public interface IPositionAccessService
{
    IQueryable<Core.Entities.Position> ApplyPositionFilter(
        IQueryable<Core.Entities.Position> query, IAppDbContext db, ActorContext actor);

    IQueryable<Core.Entities.Cv> ApplyCvFilter(
        IQueryable<Core.Entities.Cv> query, IAppDbContext db, ActorContext actor);

    Task<Result<bool>> CanAccessAsync(
        ActorContext actor, Guid candidateUserId, Guid positionId, CancellationToken ct = default);

    Task<Result<Dictionary<(Guid CandidateUserId, Guid PositionId), bool>>> CanAccessManyAsync(
        ActorContext actor,
        IReadOnlyCollection<(Guid CandidateUserId, Guid PositionId)> checks,
        CancellationToken ct = default);

    Task<Result<PagedResult<PositionDto>>> BrowseAsync(
        ActorContext actor, PageRequest page, string? search = null, CancellationToken ct = default);

    Task<Result<PagedResult<PositionDto>>> BrowsePublicAsync(
        PageRequest page, string? search = null, CancellationToken ct = default);

    Task<Result<PositionDto>> GetAccessibleAsync(
        ActorContext actor, Guid positionId, CancellationToken ct = default);

    Task<Result<PositionDto>> GetPublicAsync(Guid positionId, CancellationToken ct = default);
}

public sealed class PositionAccessService(IAppDbContextFactory factory, IAccessRuleEngine engine)
    : IPositionAccessService
{
    private const int BatchSize = 500;

    public IQueryable<Core.Entities.Position> ApplyPositionFilter(
        IQueryable<Core.Entities.Position> query, IAppDbContext db, ActorContext actor) =>
        PositionAccessQuery.WhereAccessibleToCandidate(query, db, actor);

    public IQueryable<Core.Entities.Cv> ApplyCvFilter(
        IQueryable<Core.Entities.Cv> query, IAppDbContext db, ActorContext actor) =>
        PositionAccessQuery.WhereCvAccessible(query, db, actor);

    public async Task<Result<bool>> CanAccessAsync(
        ActorContext actor, Guid candidateUserId, Guid positionId, CancellationToken ct = default)
    {
        var batch = await CanAccessManyAsync(actor, [(candidateUserId, positionId)], ct);
        if (!batch.Succeeded)
            return Result<bool>.Failure(batch.Error.Code, batch.Error.Message);
        if (batch.Value is null || !batch.Value.TryGetValue((candidateUserId, positionId), out var allowed))
            return Result<bool>.Failure(ErrorCodes.NotFound, "Position was not found.");
        return Result<bool>.Success(allowed);
    }

    public async Task<Result<Dictionary<(Guid CandidateUserId, Guid PositionId), bool>>> CanAccessManyAsync(
        ActorContext actor,
        IReadOnlyCollection<(Guid CandidateUserId, Guid PositionId)> checks,
        CancellationToken ct = default)
    {
        var result = new Dictionary<(Guid, Guid), bool>(checks.Count);
        if (checks.Count == 0)
            return Result<Dictionary<(Guid, Guid), bool>>.Success(result);

        await using var db = factory.CreateDbContext();

        foreach (var chunk in checks.Chunk(BatchSize))
        {
            var chunkResult = await EvaluateChunkAsync(db, actor, chunk, ct);
            foreach (var (key, value) in chunkResult)
                result[key] = value;
        }

        return Result<Dictionary<(Guid, Guid), bool>>.Success(result);
    }

    private async Task<Dictionary<(Guid, Guid), bool>> EvaluateChunkAsync(
        Core.Data.IAppDbContext db,
        ActorContext actor,
        IReadOnlyList<(Guid CandidateUserId, Guid PositionId)> checks,
        CancellationToken ct)
    {
        var result = new Dictionary<(Guid, Guid), bool>(checks.Count);
        var positionIds = checks.Select(c => c.PositionId).Distinct().ToList();
        var positions = await db.Positions.AsNoTracking()
            .Where(p => positionIds.Contains(p.Id))
            .Include(p => p.AccessRules)
            .ToListAsync(ct);
        var byPosition = positions.ToDictionary(p => p.Id);

        foreach (var (candidateUserId, positionId) in checks)
        {
            if (!byPosition.TryGetValue(positionId, out var position))
                continue;
            if (actor.IsPrivileged || position.IsPublic)
                result[(candidateUserId, positionId)] = true;
            else if (position.OwnerId == actor.UserId && candidateUserId == actor.UserId)
                result[(candidateUserId, positionId)] = true;
        }

        var pending = checks.Where(c => !result.ContainsKey(c)).ToList();
        if (pending.Count == 0)
            return result;

        var pendingPositionIds = pending.Select(c => c.PositionId).Distinct().ToList();
        var ruleAttributeIds = positions
            .Where(p => pendingPositionIds.Contains(p.Id))
            .SelectMany(p => p.AccessRules.Select(r => r.AttributeDefinitionId))
            .Distinct()
            .ToList();

        var candidateIds = pending.Select(c => c.CandidateUserId).Distinct().ToList();
        var valuesQuery = db.ProfileAttributeValues.AsNoTracking()
            .Where(v => candidateIds.Contains(v.Profile.UserId));
        if (ruleAttributeIds.Count > 0)
            valuesQuery = valuesQuery.Where(v => ruleAttributeIds.Contains(v.AttributeDefinitionId));
        var values = await valuesQuery
            .Select(v => new
            {
                CandidateUserId = v.Profile.UserId,
                v.AttributeDefinitionId,
                v.AttributeDefinition.DataType,
                v.StringValue,
                v.TextValue,
                v.NumericValue,
                v.DateValue,
                v.PeriodStart,
                v.BooleanValue,
                v.DropdownOption,
            })
            .ToListAsync(ct);
        var byCandidate = values
            .GroupBy(v => v.CandidateUserId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyDictionary<Guid, TypedValue>)g
                    .GroupBy(v => v.AttributeDefinitionId)
                    .ToDictionary(
                        gv => gv.Key,
                        gv =>
                        {
                            var first = gv.First();
                            return new TypedValue(
                                first.DataType,
                                first.StringValue,
                                first.TextValue,
                                first.NumericValue,
                                first.DateValue,
                                first.PeriodStart,
                                first.BooleanValue,
                                first.DropdownOption);
                        }));

        foreach (var (candidateUserId, positionId) in pending)
        {
            if (!byPosition.TryGetValue(positionId, out var position))
                continue;
            byCandidate.TryGetValue(candidateUserId, out var typed);
            typed ??= new Dictionary<Guid, TypedValue>();
            result[(candidateUserId, positionId)] = engine.CanAccess(position, actor.IsAdmin, typed);
        }

        return result;
    }

    public async Task<Result<PagedResult<PositionDto>>> BrowseAsync(
        ActorContext actor, PageRequest page, string? search = null, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();
        IQueryable<Core.Entities.Position> baseQuery = db.Positions.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
            baseQuery = baseQuery.Where(p =>
                p.Title.Contains(search) || p.ShortDescription.Contains(search) ||
                (p.Company != null && p.Company.Contains(search)));

        var accessible = ApplyPositionFilter(baseQuery, db, actor);
        var visibleTotal = await accessible.CountAsync(ct);
        var slice = await accessible.OrderBy(p => p.Title).ThenBy(p => p.Id)
            .Skip(page.Skip).Take(page.PageSize)
            .Select(p => new PositionDto(
                p.Id, p.OwnerId, p.Title, p.ShortDescription, p.Company, p.Level,
                p.IsPublic, p.MaxProjects, p.Version))
            .ToListAsync(ct);
        slice = (await Positions.PositionStats.WithCountsAsync(db, slice, ct)).ToList();
        return Result<PagedResult<PositionDto>>.Success(
            new PagedResult<PositionDto>(slice, visibleTotal, page.Page, page.PageSize));
    }

    public async Task<Result<PagedResult<PositionDto>>> BrowsePublicAsync(
        PageRequest page, string? search = null, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();
        IQueryable<Core.Entities.Position> query = db.Positions.AsNoTracking().Where(p => p.IsPublic);
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(p =>
                p.Title.Contains(search) || p.ShortDescription.Contains(search) ||
                (p.Company != null && p.Company.Contains(search)));

        var total = await query.CountAsync(ct);
        var items = await query.OrderBy(p => p.Title).ThenBy(p => p.Id)
            .Skip(page.Skip).Take(page.PageSize)
            .Select(p => new PositionDto(
                p.Id, null, p.Title, p.ShortDescription, p.Company, p.Level,
                p.IsPublic, p.MaxProjects, p.Version))
            .ToListAsync(ct);
        items = (await Positions.PositionStats.WithCountsAsync(db, items, ct)).ToList();
        return Result<PagedResult<PositionDto>>.Success(
            new PagedResult<PositionDto>(items, total, page.Page, page.PageSize));
    }

    public async Task<Result<PositionDto>> GetAccessibleAsync(
        ActorContext actor, Guid positionId, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();
        var dto = await db.Positions.AsNoTracking()
            .Where(p => p.Id == positionId)
            .Select(p => new PositionDto(
                p.Id, p.OwnerId, p.Title, p.ShortDescription, p.Company, p.Level,
                p.IsPublic, p.MaxProjects, p.Version))
            .SingleOrDefaultAsync(ct);
        if (dto is null)
            return Result<PositionDto>.Failure(ErrorCodes.NotFound, "Position was not found.");

        var access = await CanAccessAsync(actor, actor.UserId, positionId, ct);
        if (!access.Succeeded)
            return Result<PositionDto>.Failure(access.Error.Code, access.Error.Message);
        return access.Value
            ? Result<PositionDto>.Success(dto)
            : Result<PositionDto>.Failure(ErrorCodes.Forbidden, "You do not meet the requirements for this position.");
    }

    public async Task<Result<PositionDto>> GetPublicAsync(Guid positionId, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();
        var dto = await db.Positions.AsNoTracking()
            .Where(p => p.Id == positionId && p.IsPublic)
            .Select(p => new PositionDto(
                p.Id, null, p.Title, p.ShortDescription, p.Company, p.Level,
                p.IsPublic, p.MaxProjects, p.Version))
            .SingleOrDefaultAsync(ct);
        return dto is null
            ? Result<PositionDto>.Failure(ErrorCodes.NotFound, "Position was not found.")
            : Result<PositionDto>.Success(dto);
    }
}
