using CvPlatform.Application.Access;
using CvPlatform.Application.Authorization;
using CvPlatform.Application.Common;
using CvPlatform.Core.Data;
using CvPlatform.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace CvPlatform.Application.Home;

public sealed class HomeStatsService(
    IAppDbContextFactory factory,
    IPositionAccessService positionAccess,
    TimeProvider? timeProvider = null) : IHomeStatsService
{
    public async Task<Result<PublicHomeStatsDto>> GetPublicAsync(CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();
        var publicPositions = db.Positions.AsNoTracking().Where(p => p.IsPublic);
        var publishedCvs = db.Cvs.AsNoTracking()
            .Where(c => c.Status == CvStatus.Published && c.PublishedAt != null && c.Position.IsPublic);
        var cutoff = (timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime.AddHours(-24);

        var result = new PublicHomeStatsDto(
            await publicPositions.CountAsync(ct),
            await publishedCvs.CountAsync(ct),
            await publishedCvs.CountAsync(c => c.CreatedAt >= cutoff, ct));
        return Result<PublicHomeStatsDto>.Success(result);
    }

    public async Task<Result<HomeStatsDto>> GetAsync(ActorContext actor, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();

        var latest = await LoadLatestAsync(db, actor, ct);
        if (!latest.Succeeded)
            return Result<HomeStatsDto>.Failure(latest.Error.Code, latest.Error.Message);
        var popular = await LoadPopularAsync(db, actor, ct);
        if (!popular.Succeeded)
            return Result<HomeStatsDto>.Failure(popular.Error.Code, popular.Error.Message);
        var tags = await LoadTagCloudAsync(db, ct);

        return Result<HomeStatsDto>.Success(new HomeStatsDto(
            latest.Value!, popular.Value!, tags, popular.Value!.Count, latest.Value!.Count));
    }

    private static async Task<List<TagCountDto>> LoadTagCloudAsync(IAppDbContext db, CancellationToken ct)
    {
        var tags = await db.ProjectTags.AsNoTracking()
            .Select(t => new { t.Id, t.Name })
            .ToListAsync(ct);
        if (tags.Count == 0)
            return [];
        var ids = tags.Select(t => t.Id).ToList();
        var counts = await db.ProjectTagLinks.AsNoTracking()
            .Where(l => ids.Contains(l.ProjectTagId))
            .GroupBy(l => l.ProjectTagId)
            .Select(g => new { TagId = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var countMap = counts.ToDictionary(x => x.TagId, x => x.Count);
        return tags
            .Select(t => new TagCountDto(
                t.Name, countMap.TryGetValue(t.Id, out var count) ? count : 0))
            .Where(t => t.Count > 0)
            .OrderByDescending(t => t.Count)
            .ThenBy(t => t.Tag)
            .Take(20)
            .ToList();
    }

    private async Task<Result<List<LatestCvDto>>> LoadLatestAsync(
        IAppDbContext db, ActorContext actor, CancellationToken ct)
    {
        IQueryable<Core.Entities.Cv> query = db.Cvs.AsNoTracking();
        if (!actor.IsAdmin)
            query = query.Where(c => c.Status == CvStatus.Published && c.PublishedAt != null);
        if (!actor.IsAdmin && !actor.IsRecruiter)
            query = query.Where(c => c.Profile.UserId == actor.UserId);
        else
            query = positionAccess.ApplyCvFilter(query, db, actor);

        var result = await query.OrderByDescending(c => c.PublishedAt).ThenBy(c => c.Id)
            .Take(5)
            .Select(c => new LatestCvDto(
                c.Id,
                c.PositionId,
                c.Position.Title,
                c.Profile.User.UserName ?? string.Empty,
                c.PublishedAt))
            .ToListAsync(ct);
        return Result<List<LatestCvDto>>.Success(result);
    }

    private async Task<Result<List<PopularPositionDto>>> LoadPopularAsync(
        IAppDbContext db, ActorContext actor, CancellationToken ct)
    {
        var accessible = positionAccess.ApplyPositionFilter(
            db.Positions.AsNoTracking(), db, actor);

        var rows = await accessible
            .Select(p => new
            {
                p.Id,
                p.Title,
                p.Company,
                CvCount = db.Cvs.Count(c => c.PositionId == p.Id && c.Status == CvStatus.Published),
                LikeCount = db.CvLikes.Count(l => l.Cv.PositionId == p.Id),
            })
            .OrderByDescending(v => v.CvCount)
            .ThenByDescending(v => v.LikeCount)
            .ThenBy(v => v.Title)
            .ThenBy(v => v.Id)
            .Take(5)
            .Select(v => new PopularPositionDto(v.Id, v.Title, v.Company, v.CvCount, v.LikeCount))
            .ToListAsync(ct);

        return Result<List<PopularPositionDto>>.Success(rows);
    }
}
