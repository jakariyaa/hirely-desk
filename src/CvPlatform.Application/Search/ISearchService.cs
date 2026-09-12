using CvPlatform.Application.Authorization;
using CvPlatform.Application.Common;
using CvPlatform.Application.Positions;

namespace CvPlatform.Application.Search;

public interface ISearchService
{
    Task<Result<SearchResultDto>> SearchAsync(
        ActorContext actor, string term, PageRequest? page = null, CancellationToken ct = default);

    Task<Result<PagedResult<PositionDto>>> SearchPositionsAsync(
        ActorContext actor, string term, PageRequest page, CancellationToken ct = default);

    Task<Result<PagedResult<CvSearchHitDto>>> SearchCvsAsync(
        ActorContext actor, string term, PageRequest page, CancellationToken ct = default);
}
