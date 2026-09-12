using CvPlatform.Application.Authorization;
using CvPlatform.Application.Common;

namespace CvPlatform.Application.Discussions;

public interface IDiscussionService
{
    Task<Result<PagedResult<DiscussionPostDto>>> ListAsync(
        ActorContext actor, Guid positionId, PageRequest page, CancellationToken ct = default);
    Task<Result<DiscussionPostDto>> AddAsync(
        ActorContext actor, Guid positionId, DiscussionPostInput input, CancellationToken ct = default);
}
