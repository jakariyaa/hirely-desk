namespace CvPlatform.Application.Projects;

/// <summary>A portfolio project belonging to a profile.</summary>
public sealed record ProjectDto(
    Guid Id,
    Guid ProfileId,
    string Name,
    DateOnly? PeriodStart,
    DateOnly? PeriodEnd,
    string DescriptionMarkdown,
    IReadOnlyList<string> Tags,
    long Version);

/// <summary>Payload for creating or updating a project.</summary>
public sealed record ProjectInput(
    string Name,
    DateOnly? PeriodStart,
    DateOnly? PeriodEnd,
    string DescriptionMarkdown,
    IReadOnlyList<string> Tags,
    long? ExpectedVersion = null);
