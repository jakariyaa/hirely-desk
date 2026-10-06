using CvPlatform.Application.Common;

namespace CvPlatform.Application.Attributes;

/// <summary>Read-only catalog of attribute categories and definitions for profile editing.</summary>
public interface IAttributeCatalog
{
    /// <summary>All categories with their definitions, ordered by category then definition name.</summary>
    Task<Result<IReadOnlyList<AttributeCategoryDto>>> GetCatalogAsync(CancellationToken ct = default);

    Task<Result<PagedResult<AttributeDefinitionDto>>> SearchAsync(
        AttributeCatalogQuery query, CancellationToken ct = default);
}
