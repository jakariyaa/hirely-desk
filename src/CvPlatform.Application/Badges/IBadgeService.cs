using CvPlatform.Application.Authorization;
using CvPlatform.Application.Common;

namespace CvPlatform.Application.Badges;

public interface IBadgeService
{
    Task<Result<IReadOnlyList<BadgeDto>>> GetForUserAsync(
        ActorContext actor, Guid userId, CancellationToken ct = default);
}
