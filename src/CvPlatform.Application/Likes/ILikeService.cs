using CvPlatform.Application.Authorization;
using CvPlatform.Application.Common;

namespace CvPlatform.Application.Likes;

public interface ILikeService
{
    Task<Result<LikeStateDto>> ToggleAsync(ActorContext actor, Guid cvId, CancellationToken ct = default);
    Task<Result<LikeStateDto>> GetStateAsync(ActorContext actor, Guid cvId, CancellationToken ct = default);
    Task<Result<IReadOnlyDictionary<Guid, int>>> GetCountsAsync(
        ActorContext actor, IReadOnlyList<Guid> cvIds, CancellationToken ct = default);
}
