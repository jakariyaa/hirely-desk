using CvPlatform.Application.Attributes;
using CvPlatform.Application.Authorization;
using CvPlatform.Application.Common;

namespace CvPlatform.Application.Profiles;

/// <summary>Profile and attribute-value management for the current user.</summary>
public interface IProfileService
{
    /// <summary>Returns the profile for a user, creating an empty one on first access.</summary>
    Task<Result<ProfileDto>> GetForUserAsync(ActorContext actor, Guid userId, CancellationToken ct = default);

    /// <summary>Validates and saves one attribute value on the user's profile.</summary>
    Task<Result<ProfileDto>> SaveAttributeValueAsync(
        ActorContext actor, Guid userId, AttributeValueInput input, CancellationToken ct = default);


    /// <summary>The attribute catalog, for building the profile editor.</summary>
    Task<Result<IReadOnlyList<AttributeCategoryDto>>> GetCatalogAsync(CancellationToken ct = default);
}
