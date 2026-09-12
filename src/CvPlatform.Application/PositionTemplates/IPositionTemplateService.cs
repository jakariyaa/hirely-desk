using CvPlatform.Application.Authorization;
using CvPlatform.Application.Common;
using CvPlatform.Application.Positions;

namespace CvPlatform.Application.PositionTemplates;

public interface IPositionTemplateService
{
    Task<Result<PagedResult<PositionTemplateDto>>> ListAsync(
        ActorContext actor, PageRequest page, string? search = null, CancellationToken ct = default);
    Task<Result<PositionTemplateDetailDto>> GetAsync(
        ActorContext actor, Guid templateId, CancellationToken ct = default);
    Task<Result<PositionTemplateDto>> CreateAsync(
        ActorContext actor, PositionTemplateInput input, CancellationToken ct = default);
    Task<Result<PositionTemplateDto>> UpdateAsync(
        ActorContext actor, Guid templateId, PositionTemplateInput input, CancellationToken ct = default);
    Task<Result> DeleteAsync(
        ActorContext actor, Guid templateId, long expectedVersion, CancellationToken ct = default);
    Task<Result> DeleteManyAsync(
        ActorContext actor, IReadOnlyList<PositionTemplateDeleteInput> templates, CancellationToken ct = default);
    Task<Result<PositionTemplateDto>> SaveFromPositionAsync(
        ActorContext actor, Guid positionId, string name, CancellationToken ct = default);
    Task<Result<PositionDto>> CreatePositionAsync(
        ActorContext actor, Guid templateId, CancellationToken ct = default);
}
