using CvPlatform.Application.Common;
using CvPlatform.Application.Authorization;
using CvPlatform.Core.Data;
using CvPlatform.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace CvPlatform.Application.Projects;

public sealed class ProjectService(IAppDbContextFactory factory) : IProjectService
{
    public async Task<Result<IReadOnlyList<string>>> SearchTagsAsync(
        string? search, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();
        var query = db.ProjectTags.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(t => t.Name.Contains(search));
        var tags = await query.OrderBy(t => t.Name).Select(t => t.Name)
            .Take(20).ToListAsync(ct);
        return Result<IReadOnlyList<string>>.Success(tags);
    }

    public async Task<Result<PagedResult<ProjectDto>>> ListForProfileAsync(
        ActorContext actor, Guid userId, PageRequest page, CancellationToken ct = default)
    {
        if (!actor.IsAdmin && actor.UserId != userId)
            return Result<PagedResult<ProjectDto>>.Failure(ErrorCodes.Forbidden, "You are not allowed to access this profile.");

        await using var db = factory.CreateDbContext();
        var profileId = await GetProfileIdAsync(db, userId, ct);
        if (profileId is null)
            return Result<PagedResult<ProjectDto>>.Success(
                new PagedResult<ProjectDto>([], 0, page.Page, page.PageSize));

        // Offset pagination with a mandatory stable order (Name, then Id for determinism).
        var query = db.Projects
            .AsNoTracking()
            .Where(p => p.ProfileId == profileId)
            .OrderBy(p => p.Name).ThenBy(p => p.Id);

        var totalCount = await query.CountAsync(ct);
        var items = await query
            .Skip(page.Skip)
            .Take(page.PageSize)
            .Select(p => new ProjectDto(
                p.Id,
                p.ProfileId,
                p.Name,
                p.PeriodStart,
                p.PeriodEnd,
                p.DescriptionMarkdown,
                p.Tags.Select(t => t.Name).OrderBy(t => t).ToList(),
                p.Version))
            .ToListAsync(ct);

        return Result<PagedResult<ProjectDto>>.Success(
            new PagedResult<ProjectDto>(items, totalCount, page.Page, page.PageSize));
    }

    public async Task<Result<ProjectDto>> GetAsync(ActorContext actor, Guid projectId, CancellationToken ct = default)
    {
        var userId = actor.UserId;
        await using var db = factory.CreateDbContext();
        var dto = await db.Projects
            .AsNoTracking()
            .Where(p => p.Id == projectId && (actor.IsAdmin || p.Profile!.UserId == userId))
            .Select(p => new ProjectDto(
                p.Id,
                p.ProfileId,
                p.Name,
                p.PeriodStart,
                p.PeriodEnd,
                p.DescriptionMarkdown,
                p.Tags.Select(t => t.Name).OrderBy(t => t).ToList(),
                p.Version))
            .SingleOrDefaultAsync(ct);

        return dto is null
            ? Result<ProjectDto>.Failure(ErrorCodes.NotFound, $"Project {projectId} was not found.")
            : Result<ProjectDto>.Success(dto);
    }

    public async Task<Result<ProjectDto>> CreateAsync(ActorContext actor, ProjectInput input, CancellationToken ct = default)
    {
        var userId = actor.UserId;
        var error = Validate(input);
        if (error is not null)
            return Result<ProjectDto>.Failure(ErrorCodes.ValidationFailed, error);

        await using var db = factory.CreateDbContext();
        var profileId = await GetOrCreateProfileIdAsync(db, userId, ct);
        var project = new Project
        {
            Id = Guid.NewGuid(),
            ProfileId = profileId,
            Name = input.Name.Trim(),
            PeriodStart = input.PeriodStart,
            PeriodEnd = input.PeriodEnd,
            DescriptionMarkdown = input.DescriptionMarkdown ?? "",
            Tags = await ResolveTagsAsync(db, input.Tags, ct),
        };
        db.Projects.Add(project);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            return Result<ProjectDto>.Failure(
                ErrorCodes.Conflict, "Project could not be created. Please try again.");
        }

        return await GetAsync(actor, project.Id, ct);
    }

    public async Task<Result<ProjectDto>> UpdateAsync(
        ActorContext actor, Guid projectId, ProjectInput input, CancellationToken ct = default)
    {
        var userId = actor.UserId;
        var error = Validate(input);
        if (error is not null)
            return Result<ProjectDto>.Failure(ErrorCodes.ValidationFailed, error);

        await using var db = factory.CreateDbContext();
        var project = await db.Projects
            .Include(p => p.Tags)
            .SingleOrDefaultAsync(p => p.Id == projectId && (actor.IsAdmin || p.Profile!.UserId == userId), ct);

        if (project is null)
            return Result<ProjectDto>.Failure(ErrorCodes.NotFound, $"Project {projectId} was not found.");

        if (input.ExpectedVersion is null || input.ExpectedVersion.Value != project.Version)
            return Result<ProjectDto>.Failure(
                ErrorCodes.ConcurrencyConflict,
                "The project was modified by someone else. Reload and try again.");

        project.Name = input.Name.Trim();
        project.PeriodStart = input.PeriodStart;
        project.PeriodEnd = input.PeriodEnd;
        project.DescriptionMarkdown = input.DescriptionMarkdown ?? "";
        project.Tags = await ResolveTagsAsync(db, input.Tags, ct);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<ProjectDto>.Failure(
                ErrorCodes.ConcurrencyConflict, "The project was modified by someone else. Reload and try again.");
        }

        return await GetAsync(actor, project.Id, ct);
    }

    public async Task<Result> DeleteAsync(ActorContext actor, Guid projectId, CancellationToken ct = default)
    {
        var userId = actor.UserId;
        await using var db = factory.CreateDbContext();
        var project = await db.Projects
            .AsTracking()
            .SingleOrDefaultAsync(p => p.Id == projectId && (actor.IsAdmin || p.Profile!.UserId == userId), ct);

        if (project is null)
            return Result.Failure(ErrorCodes.NotFound, $"Project {projectId} was not found.");

        db.Projects.Remove(project);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure(
                ErrorCodes.ConcurrencyConflict, "The project was modified by someone else. Reload and try again.");
        }

        return Result.Success();
    }

    private static string? Validate(ProjectInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Name))
            return "Project name is required.";
        if (input.Name.Trim().Length > 200)
            return "Project name cannot exceed 200 characters.";
        if (input.PeriodStart is not null && input.PeriodEnd is not null && input.PeriodEnd < input.PeriodStart)
            return "Period end cannot be earlier than period start.";
        return null;
    }

    private static async Task<Guid?> GetProfileIdAsync(IAppDbContext db, Guid userId, CancellationToken ct) =>
        await db.Profiles
            .AsNoTracking()
            .Where(p => p.UserId == userId)
            .Select(p => (Guid?)p.Id)
            .SingleOrDefaultAsync(ct);

    private static async Task<Guid> GetOrCreateProfileIdAsync(IAppDbContext db, Guid userId, CancellationToken ct)
    {
        var profileId = await GetProfileIdAsync(db, userId, ct);
        if (profileId is not null)
            return profileId.Value;

        var profile = new Profile { Id = Guid.NewGuid(), UserId = userId };
        db.Profiles.Add(profile);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Lost a create race; re-read the winner below.
        }

        return await db.Profiles
            .AsNoTracking()
            .Where(p => p.UserId == userId)
            .Select(p => p.Id)
            .SingleAsync(ct);
    }

    private static async Task<List<ProjectTag>> ResolveTagsAsync(
        IAppDbContext db, IReadOnlyList<string>? tagNames, CancellationToken ct)
    {
        var desired = (tagNames ?? [])
            .Select(t => t.Trim())
            .Where(t => t.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (desired.Count == 0)
            return [];

        var existing = await db.ProjectTags
            .Where(t => desired.Contains(t.Name))
            .ToDictionaryAsync(t => t.Name, StringComparer.OrdinalIgnoreCase);

        var resolved = new List<ProjectTag>();
        foreach (var name in desired)
        {
            if (!existing.TryGetValue(name, out var tag))
            {
                tag = new ProjectTag { Id = Guid.NewGuid(), Name = name };
                db.ProjectTags.Add(tag);
                existing[name] = tag;
            }
            resolved.Add(tag);
        }

        return resolved;
    }
}
