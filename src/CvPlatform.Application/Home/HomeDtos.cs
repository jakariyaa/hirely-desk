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

/// <summary>A published CV on a public position, safe to show to signed-out visitors.</summary>
public sealed record PublicLatestCvDto(
    Guid CvId,
    Guid PositionId,
    string PositionTitle,
    string CandidateName,
    DateTime? PublishedAt,
    int LikeCount);

/// <summary>A public position with its published CV and like totals.</summary>
public sealed record PublicPopularPositionDto(
    Guid PositionId,
    string Title,
    string? Company,
    int CvCount,
    int LikeCount);

public sealed record HomeStatsDto(
    IReadOnlyList<LatestCvDto> LatestCvs,
    IReadOnlyList<PopularPositionDto> PopularPositions,
    IReadOnlyList<TagCountDto> TagCloud,
    int TotalPositions,
    int TotalCvs);

public sealed record PublicHomeStatsDto(
    int PublicPositionCount,
    int PublishedCvCount,
    int NewPublishedCvsLast24Hours,
    IReadOnlyList<PublicLatestCvDto> LatestCvs,
    IReadOnlyList<PublicPopularPositionDto> PopularPositions);
