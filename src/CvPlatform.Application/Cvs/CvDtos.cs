using CvPlatform.Application.Common;
using CvPlatform.Core.Entities;
using CvPlatform.Core.Enums;

namespace CvPlatform.Application.Cvs;

/// <summary>Header info for a CV (list rows and detail header).</summary>
public sealed record CvDto(
    Guid Id,
    Guid ProfileId,
    Guid UserId,
    string CandidateName,
    Guid PositionId,
    string PositionTitle,
    string? Company,
    CvStatus Status,
    DateTime? PublishedAt,
    long Version,
    int MaxProjects);

/// <summary>One rendered CV field: a required attribute from the position template paired with the profile value.</summary>
public sealed record CvFieldRowDto(
    Guid AttributeDefinitionId,
    string Name,
    AttributeDataType DataType,
    bool IsRequired,
    int SortOrder,
    string? StringValue,
    string? TextValue,
    decimal? NumericValue,
    DateOnly? DateValue,
    DateOnly? PeriodStart,
    DateOnly? PeriodEnd,
    bool? BooleanValue,
    string? DropdownOption,
    string? ImageUrl,
    long ValueVersion,
    IReadOnlyList<string> Choices,
    string? CategoryName = null)
{
    public bool IsFilled =>
        StringValue is not null || TextValue is not null || NumericValue is not null ||
        DateValue is not null || PeriodStart is not null || BooleanValue is not null ||
        DropdownOption is not null || ImageUrl is not null;
}

/// <summary>A project the candidate may include in the CV.</summary>
public sealed record CvProjectOptionDto(
    Guid ProjectId,
    string Name,
    bool IsIncluded,
    string DescriptionMarkdown = "",
    IReadOnlyList<string>? Tags = null,
    DateOnly? PeriodStart = null,
    DateOnly? PeriodEnd = null);

/// <summary>Fully rendered CV for the detail page: header, ordered field rows, and project inclusion state.</summary>
public sealed record CvDetailDto(
    CvDto Cv,
    IReadOnlyList<CvFieldRowDto> Rows,
    IReadOnlyList<CvProjectOptionDto> Projects,
    bool CanEdit,
    bool PublishGateSatisfied,
    IReadOnlyList<string> MissingRequired,
    string? DisplayName = null,
    string? ProfilePhotoUrl = null);

public sealed record CvExportRowDto(
    CvDto Cv,
    IReadOnlyList<CvFieldRowDto> Rows,
    IReadOnlyList<CvProjectOptionDto> Projects,
    string? DisplayName = null);

/// <summary>Payload for including/excluding one project in a CV (version-guarded).</summary>
public sealed record CvProjectToggleInput(Guid ProjectId, long ExpectedVersion);

/// <summary>Payload for publish/unpublish (version-guarded).</summary>
public sealed record CvStatusInput(long ExpectedVersion);
