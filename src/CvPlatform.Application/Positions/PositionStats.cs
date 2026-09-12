using CvPlatform.Core.Data;
using CvPlatform.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace CvPlatform.Application.Positions;

internal static class PositionStats
{
    internal static async Task<IReadOnlyList<PositionDto>> WithCountsAsync(
        IAppDbContext db, IReadOnlyList<PositionDto> items, CancellationToken ct)
    {
        if (items.Count == 0)
            return items;
        var ids = items.Select(p => p.Id).ToList();
        var cvCounts = await db.Cvs.AsNoTracking()
            .Where(c => ids.Contains(c.PositionId) && c.Status == CvStatus.Published)
            .GroupBy(c => c.PositionId)
            .Select(g => new { PositionId = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var likeCounts = await db.CvLikes.AsNoTracking()
            .Where(l => ids.Contains(l.Cv.PositionId))
            .GroupBy(l => l.Cv.PositionId)
            .Select(g => new { PositionId = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var cvMap = cvCounts.ToDictionary(x => x.PositionId, x => x.Count);
        var likeMap = likeCounts.ToDictionary(x => x.PositionId, x => x.Count);
        return items.Select(p => p with
        {
            CvCount = cvMap.TryGetValue(p.Id, out var cvs) ? cvs : 0,
            LikeCount = likeMap.TryGetValue(p.Id, out var likes) ? likes : 0,
        }).ToList();
    }
}
