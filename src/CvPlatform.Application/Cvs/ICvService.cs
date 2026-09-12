using CvPlatform.Application.Authorization;
using CvPlatform.Application.Common;

namespace CvPlatform.Application.Cvs;

public interface ICvService
{
    /// <summary>Creates a Draft CV for the actor's profile against a position, gated by position access.</summary>
    Task<Result<CvDto>> CreateAsync(ActorContext actor, Guid positionId, CancellationToken ct = default);

    /// <summary>Loads the rendered CV (computed view over position attributes × profile values).</summary>
    Task<Result<CvDetailDto>> GetRenderedAsync(ActorContext actor, Guid cvId, CancellationToken ct = default);

    /// <summary>The actor's own CVs.</summary>
    Task<Result<IReadOnlyList<CvDto>>> ListMineAsync(ActorContext actor, CancellationToken ct = default);

    Task<Result<PagedResult<CvDto>>> ListMinePagedAsync(
        ActorContext actor, PageRequest page, CancellationToken ct = default);

    /// <summary>All CVs submitted to a position (recruiter/admin browse, access-filtered).</summary>
    Task<Result<IReadOnlyList<CvDto>>> ListByPositionAsync(
        ActorContext actor, Guid positionId, CancellationToken ct = default);

    /// <summary>Paged variant used by server-side grids; filters then pages in memory over the position scope.</summary>
    Task<Result<PagedResult<CvDto>>> ListByPositionPagedAsync(
        ActorContext actor, Guid positionId, PageRequest? page, CancellationToken ct = default);

    Task<Result<IReadOnlyList<CvExportRowDto>>> ListForPositionExportAsync(
        ActorContext actor, Guid positionId, CancellationToken ct = default);

    /// <summary>Includes or excludes one of the candidate's projects in the CV.</summary>
    Task<Result<CvDetailDto>> ToggleProjectAsync(
        ActorContext actor, Guid cvId, CvProjectToggleInput input, CancellationToken ct = default);

    /// <summary>Publishes the CV if the publish gate is satisfied; computes SearchText.</summary>
    Task<Result<CvDto>> PublishAsync(ActorContext actor, Guid cvId, CvStatusInput input, CancellationToken ct = default);

    /// <summary>Returns a published CV to Draft.</summary>
    Task<Result<CvDto>> UnpublishAsync(ActorContext actor, Guid cvId, CvStatusInput input, CancellationToken ct = default);

    /// <summary>Deletes the CV (owner or admin).</summary>
    Task<Result> DeleteAsync(ActorContext actor, Guid cvId, CancellationToken ct = default);
}
