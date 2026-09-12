using CvPlatform.Application.Common;
using CvPlatform.Application.Attributes;
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
    long Version,
    int CvCount = 0,
    int LikeCount = 0);

public sealed record PositionInput(
    string Title,
    string ShortDescription,
    string? Company,
    string? Level,
    bool IsPublic,
    int MaxProjects = 3,
    long? ExpectedVersion = null,
    IReadOnlyList<AttributeRequirementInput>? Attributes = null);

public sealed record AccessRuleInput(
    Guid AttributeDefinitionId,
    RuleOperator Operator,
    string? ComparisonValue,
    Guid? RuleId = null,
    long? ExpectedVersion = null);

/// <summary>Payload for adding or updating one attribute requirement on a position.</summary>
public sealed record PositionAttributeInput(
    Guid AttributeDefinitionId,
    bool IsRequired,
    int? SortOrder = null,
    long? ExpectedVersion = null);

/// <summary>One attribute requirement on a position template, with definition metadata for the builder UI.</summary>
public sealed record PositionAttributeDto(
    Guid AttributeDefinitionId,
    string Name,
    string CategoryName,
    AttributeDataType DataType,
    bool IsRequired,
    int SortOrder);

/// <summary>One access rule with attribute metadata for the rule-builder UI.</summary>
public sealed record AccessRuleDto(
    Guid Id,
    Guid AttributeDefinitionId,
    string AttributeName,
    AttributeDataType DataType,
    RuleOperator Operator,
    string ComparisonValue);

/// <summary>Full position detail for the editor: position, ordered attribute requirements, and access rules.</summary>
public sealed record PositionDetailDto(
    PositionDto Position,
    IReadOnlyList<PositionAttributeDto> Attributes,
    IReadOnlyList<AccessRuleDto> AccessRules);

public sealed record PositionDeleteImpactDto(
    int Cvs,
    int DiscussionPosts,
    int Likes,
    int IncludedProjects);
