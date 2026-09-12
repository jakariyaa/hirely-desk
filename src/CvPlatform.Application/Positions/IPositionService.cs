using CvPlatform.Application.Authorization;
using CvPlatform.Application.Common;

namespace CvPlatform.Application.Positions;

public interface IPositionService
{
    Task<Result<PagedResult<PositionDto>>> ListAsync(
        ActorContext actor, PageRequest page, string? search = null, CancellationToken ct = default);
    Task<Result<PositionDto>> GetAsync(ActorContext actor, Guid positionId, CancellationToken ct = default);
    Task<Result<PositionDto>> CreateAsync(ActorContext actor, PositionInput input, CancellationToken ct = default);
    Task<Result<PositionDto>> UpdateAsync(
        ActorContext actor, Guid positionId, PositionInput input, CancellationToken ct = default);
    Task<Result> DeleteAsync(ActorContext actor, Guid positionId, long expectedVersion, CancellationToken ct = default);
    Task<Result<PositionDeleteImpactDto>> GetDeleteImpactAsync(
        ActorContext actor, Guid positionId, CancellationToken ct = default);
    Task<Result> SaveRuleAsync(
        ActorContext actor, Guid positionId, AccessRuleInput input, CancellationToken ct = default);
    Task<Result> RemoveRuleAsync(
        ActorContext actor, Guid positionId, Guid ruleId, long expectedVersion, CancellationToken ct = default);
    Task<Result> SaveAttributeAsync(
        ActorContext actor, Guid positionId, PositionAttributeInput input, CancellationToken ct = default);
    Task<Result> RemoveAttributeAsync(
        ActorContext actor, Guid positionId, Guid attributeDefinitionId, long expectedVersion, CancellationToken ct = default);
    Task<Result<PositionDto>> DuplicateAsync(
        ActorContext actor, Guid positionId, CancellationToken ct = default);
    Task<Result<PositionDetailDto>> GetForEditAsync(
        ActorContext actor, Guid positionId, CancellationToken ct = default);
    Task<Result<PositionDto>> AssignOwnerAsync(
        ActorContext actor, Guid positionId, Guid ownerId, long expectedVersion, CancellationToken ct = default);
}
