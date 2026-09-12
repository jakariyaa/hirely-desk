using CvPlatform.Core.Enums;

namespace CvPlatform.Application.Profiles;

/// <summary>A candidate profile with its current attribute values.</summary>
public sealed record ProfileDto(Guid ProfileId, Guid UserId, IReadOnlyList<AttributeValueDto> Values);

public sealed record ProfileSummaryDto(string? Name, string? ImageUrl);

public static class ProfileAttributeNames
{
    public const string Name = "Me.Name";
    public const string Photo = "Me.Photo";
}

/// <summary>One attribute value on a profile.</summary>
public sealed record AttributeValueDto(
    Guid AttributeDefinitionId,
    string AttributeName,
    AttributeDataType DataType,
    string? StringValue,
    string? TextValue,
    decimal? NumericValue,
    DateOnly? DateValue,
    DateOnly? PeriodStart,
    DateOnly? PeriodEnd,
    bool? BooleanValue,
    string? DropdownOption,
    string? ImageUrl,
    long Version)
{
    public static AttributeValueDto FromEntity(CvPlatform.Core.Entities.ProfileAttributeValue v) => new(
        v.AttributeDefinitionId,
        v.AttributeDefinition.Name,
        v.AttributeDefinition.DataType,
        v.StringValue,
        v.TextValue,
        v.NumericValue,
        v.DateValue,
        v.PeriodStart,
        v.PeriodEnd,
        v.BooleanValue,
        v.DropdownOption,
        v.ImageUrl,
        v.Version);
}

/// <summary>Payload for creating or updating one attribute value on the current user's profile.</summary>
public sealed record AttributeValueInput(
    Guid AttributeDefinitionId,
    string? StringValue = null,
    string? TextValue = null,
    decimal? NumericValue = null,
    DateOnly? DateValue = null,
    DateOnly? PeriodStart = null,
    DateOnly? PeriodEnd = null,
    bool? BooleanValue = null,
    string? DropdownOption = null,
    string? ImageUrl = null,
    long? ExpectedVersion = null);
