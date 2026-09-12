using CvPlatform.Application.Authorization;
using CvPlatform.Application.Common;

namespace CvPlatform.Application.Attributes;

public interface IAttributeDefinitionService
{
    Task<Result<PagedResult<AttributeDefinitionAdminDto>>> ListAsync(
        ActorContext actor, AttributeCatalogQuery query, CancellationToken ct = default);

    Task<Result<AttributeDefinitionAdminDto>> CreateAsync(
        ActorContext actor, AttributeDefinitionInput input, CancellationToken ct = default);

    Task<Result<AttributeDefinitionAdminDto>> UpdateAsync(
        ActorContext actor, Guid id, AttributeDefinitionInput input, CancellationToken ct = default);

    Task<Result<AttributeDeleteImpactDto>> GetDeleteImpactAsync(
        ActorContext actor, Guid id, CancellationToken ct = default);

    Task<Result<AttributeOptionImpactDto>> GetOptionChangeImpactAsync(
        ActorContext actor, Guid id, AttributeDefinitionInput input, CancellationToken ct = default);

    Task<Result> DeleteAsync(ActorContext actor, Guid id, long expectedVersion, CancellationToken ct = default);
}
