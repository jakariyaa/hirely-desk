using CvPlatform.Application.Access;
using CvPlatform.Application.Authorization;
using CvPlatform.Application.Common;
using CvPlatform.Core.Data;
using CvPlatform.Core.Entities;
using CvPlatform.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace CvPlatform.Application.Likes;

public sealed class LikeService(
    IAppDbContextFactory factory,
    IPositionAccessService positionAccess) : ILikeService
{
    public async Task<Result<LikeStateDto>> ToggleAsync(ActorContext actor, Guid cvId, CancellationToken ct = default)
    {
        if (!actor.IsPrivileged)
            return Result<LikeStateDto>.Failure(ErrorCodes.Forbidden, "Only recruiters can like CVs.");
        await using var db = factory.CreateDbContext();
        var cv = await db.Cvs.AsNoTracking()
            .Where(c => c.Id == cvId)
            .Select(c => new { c.PositionId, UserId = c.Profile.UserId, c.Status })
            .SingleOrDefaultAsync(ct);
        if (cv is null)
            return Result<LikeStateDto>.Failure(ErrorCodes.NotFound, "CV was not found.");
        if (!actor.IsAdmin && cv.UserId == actor.UserId)
            return Result<LikeStateDto>.Failure(ErrorCodes.Forbidden, "You cannot like your own CV.");
        if (!actor.IsAdmin && cv.Status != CvStatus.Published)
            return Result<LikeStateDto>.Failure(ErrorCodes.Forbidden, "Only published CVs can be liked.");

        var access = await positionAccess.CanAccessAsync(actor, cv.UserId, cv.PositionId, ct);
        if (!access.Succeeded)
            return Result<LikeStateDto>.Failure(access.Error.Code, access.Error.Message);
        if (!access.Value)
            return Result<LikeStateDto>.Failure(ErrorCodes.Forbidden, "You do not have access to this CV.");

        var existing = await db.CvLikes
            .SingleOrDefaultAsync(l => l.CvId == cvId && l.RecruiterId == actor.UserId, ct);
        if (existing is not null)
            db.CvLikes.Remove(existing);
        else
            db.CvLikes.Add(new CvLike { CvId = cvId, RecruiterId = actor.UserId });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            return Result<LikeStateDto>.Failure(ErrorCodes.Conflict, "Like could not be saved.");
        }

        var state = await LoadStateAsync(db, actor.UserId, cvId, ct);
        return Result<LikeStateDto>.Success(state);
    }

    public async Task<Result<LikeStateDto>> GetStateAsync(ActorContext actor, Guid cvId, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();
        var cv = await db.Cvs.AsNoTracking()
            .Where(c => c.Id == cvId)
            .Select(c => new { c.PositionId, UserId = c.Profile.UserId, c.Status })
            .SingleOrDefaultAsync(ct);
        if (cv is null)
            return Result<LikeStateDto>.Failure(ErrorCodes.NotFound, "CV was not found.");
        if (!actor.IsAdmin && cv.UserId != actor.UserId && cv.Status != CvStatus.Published)
            return Result<LikeStateDto>.Failure(ErrorCodes.Forbidden, "You do not have access to this CV.");
        if (cv.UserId != actor.UserId && !actor.IsAdmin)
        {
            var access = await positionAccess.CanAccessAsync(actor, cv.UserId, cv.PositionId, ct);
            if (!access.Succeeded)
                return Result<LikeStateDto>.Failure(access.Error.Code, access.Error.Message);
            if (!access.Value)
                return Result<LikeStateDto>.Failure(ErrorCodes.Forbidden, "You do not have access to this CV.");
        }

        return Result<LikeStateDto>.Success(await LoadStateAsync(db, actor.UserId, cvId, ct));
    }

    public async Task<Result<IReadOnlyDictionary<Guid, int>>> GetCountsAsync(
        ActorContext actor, IReadOnlyList<Guid> cvIds, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();
        if (cvIds.Count == 0)
            return Result<IReadOnlyDictionary<Guid, int>>.Success(new Dictionary<Guid, int>());
        var counts = await db.CvLikes.AsNoTracking()
            .Where(l => cvIds.Contains(l.CvId))
            .GroupBy(l => l.CvId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToListAsync(ct);
        return Result<IReadOnlyDictionary<Guid, int>>.Success(
            counts.ToDictionary(x => x.Key, x => x.Count));
    }

    private static async Task<LikeStateDto> LoadStateAsync(
        IAppDbContext db, Guid recruiterId, Guid cvId, CancellationToken ct)
    {
        var count = await db.CvLikes.AsNoTracking().CountAsync(l => l.CvId == cvId, ct);
        var liked = await db.CvLikes.AsNoTracking()
            .AnyAsync(l => l.CvId == cvId && l.RecruiterId == recruiterId, ct);
        return new LikeStateDto(cvId, liked, count);
    }
}
