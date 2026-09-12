namespace CvPlatform.Application.Home;

public sealed record LatestCvDto(
    Guid CvId,
    Guid PositionId,
    string PositionTitle,
    string CandidateName,
    DateTime? PublishedAt);

public sealed record PopularPositionDto(
    Guid PositionId,
    string Title,
    string? Company,
    int CvCount,
    int LikeCount);

public sealed record TagCountDto(string Tag, int Count);

public sealed record HomeStatsDto(
    IReadOnlyList<LatestCvDto> LatestCvs,
    IReadOnlyList<PopularPositionDto> PopularPositions,
    IReadOnlyList<TagCountDto> TagCloud,
    int TotalPositions,
    int TotalCvs);

public sealed record PublicHomeStatsDto(
    int PublicPositionCount,
    int PublishedCvCount,
    int NewPublishedCvsLast24Hours);
