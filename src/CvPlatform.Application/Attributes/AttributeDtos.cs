using CvPlatform.Core.Enums;
using CvPlatform.Application.Common;

namespace CvPlatform.Application.Attributes;

/// <summary>A single dropdown choice parsed from AttributeDefinition.OptionsJson.</summary>
public sealed record DropdownChoice(string Value);

/// <summary>An attribute definition as exposed to the profile editor UI.</summary>
public sealed record AttributeDefinitionDto(
    Guid Id,
    string Name,
    AttributeDataType DataType,
    bool IsBuiltIn,
    IReadOnlyList<DropdownChoice> Choices);

/// <summary>Attribute definitions grouped by category, ordered for display.</summary>
public sealed record AttributeCategoryDto(
    Guid Id,
    string Name,
    IReadOnlyList<AttributeDefinitionDto> Definitions);

public sealed record AttributeCatalogQuery(
    Guid? CategoryId = null,
    string? Search = null,
    string? Prefix = null,
    PageRequest? Page = null);

public sealed record AttributeDefinitionInput(
    Guid CategoryId,
    string Name,
    string? Description,
    AttributeDataType DataType,
    string? OptionsJson,
    long? ExpectedVersion = null,
    bool ForceOptionRemoval = false);

public sealed record AttributeRequirementInput(
    Guid AttributeDefinitionId,
    bool IsRequired,
    int SortOrder);

public sealed record AttributeOptions(
    string[]? Choices = null,
    decimal? Min = null,
    decimal? Max = null,
    int? MaxLength = null,
    string? Regex = null);

public sealed record AttributeDefinitionAdminDto(
    Guid Id,
    Guid CategoryId,
    string CategoryName,
    string Name,
    string? Description,
    AttributeDataType DataType,
    bool IsBuiltIn,
    string? OptionsJson,
    long Version);

public sealed record AttributeDeleteImpactDto(
    int ProfileValues,
    int Positions,
    int AccessRules,
    int Cvs,
    int RestrictedPositionsLosingGating);

public sealed record AttributeOptionImpactDto(
    IReadOnlyList<string> RemovedOptions,
    int ProfileValues,
    int AccessRules);
