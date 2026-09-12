using CvPlatform.Application.Common;
using CvPlatform.Application.Positions;

namespace CvPlatform.Application.Search;

public sealed record CvSearchHitDto(
    Guid CvId,
    Guid PositionId,
    string PositionTitle,
    string? Company,
    string CandidateName,
    DateTime? PublishedAt);

public sealed record SearchResultDto(
    PagedResult<PositionDto> Positions,
    PagedResult<CvSearchHitDto> Cvs);
