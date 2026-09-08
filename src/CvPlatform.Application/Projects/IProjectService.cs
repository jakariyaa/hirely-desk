using CvPlatform.Application.Common;
using CvPlatform.Application.Authorization;

namespace CvPlatform.Application.Projects;

/// <summary>Portfolio project management scoped to the current user's profile.</summary>
public interface IProjectService
{
    /// <summary>Lists the user's projects, newest first, with server-side paging.</summary>
    Task<Result<PagedResult<ProjectDto>>> ListForProfileAsync(ActorContext actor, Guid userId, PageRequest page, CancellationToken ct = default);

    /// <summary>Returns a single project owned by the user.</summary>
    Task<Result<ProjectDto>> GetAsync(ActorContext actor, Guid projectId, CancellationToken ct = default);

    /// <summary>Creates a project on the user's profile.</summary>
    Task<Result<ProjectDto>> CreateAsync(ActorContext actor, ProjectInput input, CancellationToken ct = default);

    /// <summary>Updates a project owned by the user.</summary>
    Task<Result<ProjectDto>> UpdateAsync(ActorContext actor, Guid projectId, ProjectInput input, CancellationToken ct = default);

    /// <summary>Deletes a project owned by the user.</summary>
    Task<Result> DeleteAsync(ActorContext actor, Guid projectId, CancellationToken ct = default);

    Task<Result<IReadOnlyList<string>>> SearchTagsAsync(string? search, CancellationToken ct = default);
}
