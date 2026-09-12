using CvPlatform.Application.Authorization;
using CvPlatform.Application.Common;
using CvPlatform.Core.Data;
using CvPlatform.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace CvPlatform.Application.Badges;

public sealed class BadgeService(IAppDbContextFactory factory) : IBadgeService
{
    public async Task<Result<IReadOnlyList<BadgeDto>>> GetForUserAsync(
        ActorContext actor, Guid userId, CancellationToken ct = default)
    {
        if (!actor.IsAdmin && actor.UserId != userId)
            return Result<IReadOnlyList<BadgeDto>>.Failure(ErrorCodes.Forbidden, "You can only view your own badges.");

        await using var db = factory.CreateDbContext();
        var badges = new List<BadgeDto>();

        if (await db.ProfileAttributeValues.AsNoTracking().AnyAsync(v => v.Profile.UserId == userId, ct))
            badges.Add(new BadgeDto("ProfileStarter"));
        if (await db.Cvs.AsNoTracking().AnyAsync(c => c.Profile.UserId == userId, ct))
            badges.Add(new BadgeDto("CvCreator"));
        if (await db.Cvs.AsNoTracking()
                .AnyAsync(c => c.Profile.UserId == userId && c.Status == CvStatus.Published, ct))
            badges.Add(new BadgeDto("Publisher"));
        if (await db.CvLikes.AsNoTracking().AnyAsync(l => l.Cv.Profile.UserId == userId, ct))
            badges.Add(new BadgeDto("LikedAuthor"));
        if (await db.Positions.AsNoTracking().AnyAsync(p => p.OwnerId == userId, ct))
            badges.Add(new BadgeDto("Recruiter"));

        return Result<IReadOnlyList<BadgeDto>>.Success(badges);
    }
}
