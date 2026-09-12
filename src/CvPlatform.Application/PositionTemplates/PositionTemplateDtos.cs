using CvPlatform.Application.Common;
using CvPlatform.Application.Attributes;
using CvPlatform.Core.Enums;

namespace CvPlatform.Application.PositionTemplates;

public sealed record PositionTemplateInput(
    string Name,
    string Description,
    string? Company,
    string? Level,
    int MaxProjects = 3,
    bool IsActive = true,
    long? ExpectedVersion = null,
    IReadOnlyList<AttributeRequirementInput>? Attributes = null);

public sealed record PositionTemplateDto(
    Guid Id,
    Guid CreatedById,
    string Name,
    string Description,
    string? Company,
    string? Level,
    int MaxProjects,
    bool IsActive,
    long Version,
    int AttributeCount,
    int AccessRuleCount);

public sealed record PositionTemplateDeleteInput(Guid Id, long ExpectedVersion);

public sealed record PositionTemplateAttributeDto(
    Guid AttributeDefinitionId,
    string Name,
    AttributeDataType DataType,
    bool IsRequired,
    int SortOrder);

public sealed record PositionTemplateAccessRuleDto(
    Guid Id,
    Guid AttributeDefinitionId,
    string AttributeName,
    AttributeDataType DataType,
    RuleOperator Operator,
    string ComparisonValue);

public sealed record PositionTemplateDetailDto(
    PositionTemplateDto Template,
    IReadOnlyList<PositionTemplateAttributeDto> Attributes,
    IReadOnlyList<PositionTemplateAccessRuleDto> AccessRules);
