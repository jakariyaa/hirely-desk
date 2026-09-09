using CvPlatform.Application.Common;
using CvPlatform.Core.Enums;

namespace CvPlatform.Application.Positions;

public sealed record PositionDto(
    Guid Id,
    Guid? OwnerId,
    string Title,
    string ShortDescription,
    string? Company,
    string? Level,
    bool IsPublic,
    int MaxProjects,
    long Version);

public sealed record PositionInput(
    string Title,
    string ShortDescription,
    string? Company,
    string? Level,
    bool IsPublic,
    int MaxProjects = 3,
    long? ExpectedVersion = null);

public sealed record AccessRuleInput(
    Guid AttributeDefinitionId,
    RuleOperator Operator,
    string? ComparisonValue,
    Guid? RuleId = null,
    long? ExpectedVersion = null);
