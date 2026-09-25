using CvPlatform.Application.Access;
using CvPlatform.Application.Authorization;
using CvPlatform.Application.Common;
using CvPlatform.Application.Profiles;
using CvPlatform.Core.Data;
using CvPlatform.Core.Entities;
using CvPlatform.Core.Enums;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace CvPlatform.Application.Cvs;

public sealed class CvService(
    IAppDbContextFactory factory,
    IPositionAccessService positionAccess) : ICvService
{
    public async Task<Result<CvDto>> CreateAsync(ActorContext actor, Guid positionId, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();

        var position = await db.Positions.AsNoTracking()
            .SingleOrDefaultAsync(p => p.Id == positionId, ct);
        if (position is null)
            return Result<CvDto>.Failure(ErrorCodes.NotFound, "Position was not found.");

        var access = await positionAccess.CanAccessAsync(actor, actor.UserId, positionId, ct);
        if (!access.Succeeded)
            return Result<CvDto>.Failure(access.Error.Code, access.Error.Message);
        if (!access.Value)
            return Result<CvDto>.Failure(ErrorCodes.Forbidden, "You do not meet the requirements for this position.");

        var profile = await db.Profiles.AsNoTracking().SingleOrDefaultAsync(p => p.UserId == actor.UserId, ct);
        if (profile is null)
            return Result<CvDto>.Failure(ErrorCodes.NotFound, "Profile was not found.");

        var exists = await db.Cvs.AsNoTracking()
            .AnyAsync(c => c.ProfileId == profile.Id && c.PositionId == positionId, ct);
        if (exists)
            return Result<CvDto>.Failure(ErrorCodes.Conflict, "You already applied to this position.");

        var cv = new Cv
        {
            Id = Guid.NewGuid(),
            ProfileId = profile.Id,
            PositionId = positionId,
            CreatedAt = DateTime.UtcNow,
            Status = CvStatus.Draft,
        };
        db.Cvs.Add(cv);
        await db.SaveChangesAsync(ct);

        return Result<CvDto>.Success((await LoadDtoAsync(db, cv.Id, ct))!);
    }

    public async Task<Result<CvDetailDto>> GetRenderedAsync(ActorContext actor, Guid cvId, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();

        var cv = await LoadCvAsync(db, cvId, ct);
        if (cv is null)
            return Result<CvDetailDto>.Failure(ErrorCodes.NotFound, "CV was not found.");

        var detail = await RenderAsync(db, actor, cv, ct);
        return detail is null
            ? Result<CvDetailDto>.Failure(ErrorCodes.Forbidden, "You do not have access to this CV.")
            : Result<CvDetailDto>.Success(detail);
    }

    public async Task<Result<IReadOnlyList<CvDto>>> ListMineAsync(ActorContext actor, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();
        var result = await db.Cvs.AsNoTracking()
            .Where(c => c.Profile.UserId == actor.UserId)
            .OrderByDescending(c => c.PublishedAt)
            .ThenBy(c => c.Id)
            .Select(c => new CvDto(
                c.Id, c.ProfileId, c.Profile.UserId,
                c.Profile.User.UserName ?? string.Empty,
                c.PositionId, c.Position.Title, c.Position.Company,
                c.Status, c.PublishedAt, c.Version, c.Position.MaxProjects))
            .ToListAsync(ct);
        return Result<IReadOnlyList<CvDto>>.Success(result);
    }

    public async Task<Result<PagedResult<CvDto>>> ListMinePagedAsync(
        ActorContext actor, PageRequest page, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();
        var query = db.Cvs.AsNoTracking().Where(c => c.Profile.UserId == actor.UserId);
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(c => c.PublishedAt).ThenBy(c => c.Id)
            .Skip(page.Skip).Take(page.PageSize)
            .Select(c => new CvDto(
                c.Id, c.ProfileId, c.Profile.UserId,
                c.Profile.User.UserName ?? string.Empty,
                c.PositionId, c.Position.Title, c.Position.Company,
                c.Status, c.PublishedAt, c.Version, c.Position.MaxProjects))
            .ToListAsync(ct);
        return Result<PagedResult<CvDto>>.Success(new PagedResult<CvDto>(items, total, page.Page, page.PageSize));
    }

    public async Task<Result<IReadOnlyList<CvDto>>> ListByPositionAsync(
        ActorContext actor, Guid positionId, CancellationToken ct = default)
    {
        var paged = await ListByPositionPagedAsync(actor, positionId, null, ct);
        if (!paged.Succeeded)
            return Result<IReadOnlyList<CvDto>>.Failure(paged.Error.Code, paged.Error.Message);
        return Result<IReadOnlyList<CvDto>>.Success(paged.Value.Items);
    }

    public async Task<Result<PagedResult<CvDto>>> ListByPositionPagedAsync(
        ActorContext actor, Guid positionId, PageRequest? page, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();

        var position = await db.Positions.AsNoTracking()
            .SingleOrDefaultAsync(p => p.Id == positionId, ct);
        if (position is null)
            return Result<PagedResult<CvDto>>.Failure(ErrorCodes.NotFound, "Position was not found.");

        if (!actor.IsPrivileged)
            return Result<PagedResult<CvDto>>.Failure(
                ErrorCodes.Forbidden, "Only recruiters can browse submitted CVs.");
        IQueryable<Core.Entities.Cv> cvQuery = db.Cvs.AsNoTracking()
            .Where(c => c.PositionId == positionId &&
                        (actor.IsAdmin || (c.Status == CvStatus.Published && c.PublishedAt != null)));
        cvQuery = positionAccess.ApplyCvFilter(cvQuery, db, actor);

        if (page is null)
        {
            var allItems = await cvQuery
                .OrderByDescending(c => c.PublishedAt).ThenBy(c => c.Id)
                .Select(c => new CvDto(
                    c.Id, c.ProfileId, c.Profile.UserId,
                    c.Profile.User.UserName ?? string.Empty,
                    c.PositionId, c.Position.Title, c.Position.Company,
                    c.Status, c.PublishedAt, c.Version, c.Position.MaxProjects))
                .ToListAsync(ct);
            return Result<PagedResult<CvDto>>.Success(
                new PagedResult<CvDto>(allItems, allItems.Count, 1, Math.Max(1, allItems.Count)));
        }

        var total = await cvQuery.CountAsync(ct);
        var slice = await cvQuery
            .OrderByDescending(c => c.PublishedAt).ThenBy(c => c.Id)
            .Skip(page.Skip).Take(page.PageSize)
            .Select(c => new CvDto(
                c.Id, c.ProfileId, c.Profile.UserId,
                c.Profile.User.UserName ?? string.Empty,
                c.PositionId, c.Position.Title, c.Position.Company,
                c.Status, c.PublishedAt, c.Version, c.Position.MaxProjects))
            .ToListAsync(ct);
        return Result<PagedResult<CvDto>>.Success(
            new PagedResult<CvDto>(slice, total, page.Page, page.PageSize));
    }

    public async Task<Result<IReadOnlyList<CvExportRowDto>>> ListForPositionExportAsync(
        ActorContext actor, Guid positionId, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();

        var position = await db.Positions.AsNoTracking()
            .SingleOrDefaultAsync(p => p.Id == positionId, ct);
        if (position is null)
            return Result<IReadOnlyList<CvExportRowDto>>.Failure(
                ErrorCodes.NotFound, "Position was not found.");
        if (!actor.IsPrivileged)
            return Result<IReadOnlyList<CvExportRowDto>>.Failure(
                ErrorCodes.Forbidden, "Only recruiters can export submitted CVs.");

        IQueryable<Core.Entities.Cv> query = db.Cvs.AsNoTracking()
            .Where(c => c.PositionId == positionId &&
                        (actor.IsAdmin || (c.Status == CvStatus.Published && c.PublishedAt != null)));
        query = positionAccess.ApplyCvFilter(query, db, actor);

        var cvs = await query
            .AsSplitQuery()
            .Include(c => c.Profile).ThenInclude(p => p.User)
            .Include(c => c.Profile).ThenInclude(p => p.AttributeValues)
                .ThenInclude(v => v.AttributeDefinition).ThenInclude(d => d.Category)
            .Include(c => c.Position).ThenInclude(p => p.Attributes)
                .ThenInclude(a => a.AttributeDefinition).ThenInclude(d => d.Category)
            .Include(c => c.IncludedProjects).ThenInclude(cp => cp.Project)
                .ThenInclude(p => p.Tags)
            .OrderByDescending(c => c.PublishedAt).ThenBy(c => c.Id)
            .ToListAsync(ct);

        var result = cvs.Select(cv =>
        {
            var values = cv.Profile.AttributeValues.ToDictionary(v => v.AttributeDefinitionId);
            var rows = cv.Position.Attributes
                .OrderBy(a => a.SortOrder)
                .Select(a =>
                {
                    values.TryGetValue(a.AttributeDefinitionId, out var value);
                    return ToFieldRow(a, value);
                })
                .ToList();
            var projects = cv.IncludedProjects
                .OrderBy(cp => cp.SortOrder)
                .Select(cp => new CvProjectOptionDto(
                    cp.ProjectId,
                    cp.Project.Name,
                    true,
                    cp.Project.DescriptionMarkdown,
                    cp.Project.Tags.Select(t => t.Name).OrderBy(t => t).ToList(),
                    cp.Project.PeriodStart,
                    cp.Project.PeriodEnd))
                .ToList();
            var dto = new CvDto(
                cv.Id, cv.ProfileId, cv.Profile.UserId,
                cv.Profile.User.UserName ?? string.Empty,
                cv.PositionId, cv.Position.Title, cv.Position.Company,
                cv.Status, cv.PublishedAt, cv.Version, cv.Position.MaxProjects);
            var displayName = cv.Profile.AttributeValues
                .FirstOrDefault(v => v.AttributeDefinition.Name == ProfileAttributeNames.Name)
                ?.StringValue;
            return new CvExportRowDto(dto, rows, projects, displayName);
        }).ToList();

        return Result<IReadOnlyList<CvExportRowDto>>.Success(result);
    }

    public async Task<Result<CvDetailDto>> ToggleProjectAsync(
        ActorContext actor, Guid cvId, CvProjectToggleInput input, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();

        var cv = await LoadCvAsync(db, cvId, ct);
        if (cv is null)
            return Result<CvDetailDto>.Failure(ErrorCodes.NotFound, "CV was not found.");
        if (!actor.IsAdmin && cv.Profile.UserId != actor.UserId)
            return Result<CvDetailDto>.Failure(ErrorCodes.Forbidden, "You are not allowed to edit this CV.");

        if (input.ExpectedVersion != cv.Version)
            return Result<CvDetailDto>.Failure(
                ErrorCodes.ConcurrencyConflict, "The CV was modified by someone else. Reload and try again.");

        var project = await db.Projects.AsNoTracking()
            .SingleOrDefaultAsync(p => p.Id == input.ProjectId && p.ProfileId == cv.ProfileId, ct);
        if (project is null)
            return Result<CvDetailDto>.Failure(ErrorCodes.NotFound, "Project was not found on your profile.");

        var position = await db.Positions.AsNoTracking()
            .SingleAsync(p => p.Id == cv.PositionId, ct);
        var included = cv.IncludedProjects.FirstOrDefault(p => p.ProjectId == input.ProjectId);
        if (included is null && cv.IncludedProjects.Count >= position.MaxProjects)
            return Result<CvDetailDto>.Failure(
                ErrorCodes.ValidationFailed,
                $"This CV can include at most {position.MaxProjects} project(s). Remove one first.");

        if (included is not null)
            db.CvProjects.Remove(included);
        else
            db.CvProjects.Add(new CvProject
            {
                CvId = cv.Id,
                ProjectId = input.ProjectId,
                SortOrder = cv.IncludedProjects.Count,
            });

        db.Entry(cv).Property(c => c.Version).IsModified = true;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<CvDetailDto>.Failure(
                ErrorCodes.ConcurrencyConflict, "The CV was modified by someone else. Reload and try again.");
        }

        var reloaded = await LoadCvAsync(db, cvId, ct);
        return Result<CvDetailDto>.Success((await RenderAsync(db, actor, reloaded ?? cv, ct))!);
    }

    public Task<Result<CvDto>> PublishAsync(ActorContext actor, Guid cvId, CvStatusInput input, CancellationToken ct = default) =>
        SetStatusAsync(actor, cvId, CvStatus.Published, input.ExpectedVersion, ct);

    public Task<Result<CvDto>> UnpublishAsync(ActorContext actor, Guid cvId, CvStatusInput input, CancellationToken ct = default) =>
        SetStatusAsync(actor, cvId, CvStatus.Draft, input.ExpectedVersion, ct);

    public async Task<Result> DeleteAsync(ActorContext actor, Guid cvId, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();

        var cv = await db.Cvs
            .Include(c => c.Profile)
            .Include(c => c.IncludedProjects)
            .SingleOrDefaultAsync(c => c.Id == cvId, ct);
        if (cv is null)
            return Result.Failure(ErrorCodes.NotFound, "CV was not found.");
        if (!actor.IsAdmin && cv.Profile.UserId != actor.UserId)
            return Result.Failure(ErrorCodes.Forbidden, "You are not allowed to delete this CV.");

        db.CvProjects.RemoveRange(cv.IncludedProjects);
        db.Cvs.Remove(cv);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    private async Task<Result<CvDto>> SetStatusAsync(
        ActorContext actor, Guid cvId, CvStatus target, long expectedVersion, CancellationToken ct)
    {
        await using var db = factory.CreateDbContext();

        var cv = await LoadCvAsync(db, cvId, ct);
        if (cv is null)
            return Result<CvDto>.Failure(ErrorCodes.NotFound, "CV was not found.");
        if (!actor.IsAdmin && cv.Profile.UserId != actor.UserId)
            return Result<CvDto>.Failure(ErrorCodes.Forbidden, "You are not allowed to edit this CV.");

        if (expectedVersion != cv.Version)
            return Result<CvDto>.Failure(
                ErrorCodes.ConcurrencyConflict, "The CV was modified by someone else. Reload and try again.");

        if (target == CvStatus.Published)
        {
            var missing = CvPublishGate.Missing(
                cv.Position.Attributes, cv.Profile.AttributeValues.ToDictionary(v => v.AttributeDefinitionId));
            if (missing.Count > 0)
                return Result<CvDto>.Failure(
                    ErrorCodes.ValidationFailed,
                    "Fill all required attributes before publishing. Missing: " + string.Join(", ", missing));

            cv.SearchText = CvSearchTextBuilder.Build(cv);
            cv.PublishedAt = DateTime.UtcNow;
        }
        else
        {
            cv.PublishedAt = null;
            cv.SearchText = null;
        }

        cv.Status = target;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<CvDto>.Failure(
                ErrorCodes.ConcurrencyConflict, "The CV was modified by someone else. Reload and try again.");
        }

        return Result<CvDto>.Success((await LoadDtoAsync(db, cvId, ct))!);
    }

    private static async Task<Cv?> LoadCvAsync(IAppDbContext db, Guid cvId, CancellationToken ct) =>
        await db.Cvs
            .AsSplitQuery()
            .Include(c => c.Profile).ThenInclude(p => p.User)
            .Include(c => c.Profile).ThenInclude(p => p.AttributeValues)
                .ThenInclude(v => v.AttributeDefinition).ThenInclude(d => d.Category)
            .Include(c => c.Position).ThenInclude(p => p.Attributes)
                .ThenInclude(a => a.AttributeDefinition).ThenInclude(d => d.Category)
            .Include(c => c.IncludedProjects).ThenInclude(cp => cp.Project)
            .FirstOrDefaultAsync(c => c.Id == cvId, ct);

    private static async Task<CvDto?> LoadDtoAsync(IAppDbContext db, Guid cvId, CancellationToken ct) =>
        await db.Cvs.AsNoTracking()
            .Where(c => c.Id == cvId)
            .Select(c => new CvDto(
                c.Id, c.ProfileId, c.Profile.UserId,
                c.Profile.User.UserName ?? string.Empty,
                c.PositionId, c.Position.Title, c.Position.Company,
                c.Status, c.PublishedAt, c.Version, c.Position.MaxProjects))
            .SingleOrDefaultAsync(ct);

    private async Task<CvDetailDto?> RenderAsync(IAppDbContext db, ActorContext actor, Cv cv, CancellationToken ct)
    {
        var candidateId = cv.Profile.UserId;

        // Draft CVs are visible to their owner and admins only.
        if (!actor.IsAdmin && candidateId != actor.UserId && cv.Status != CvStatus.Published)
            return null;

        // Access-loss hiding (§7.5): third-party renders are filtered through the single
        // source of truth — admins bypass inside the engine.
        if (candidateId != actor.UserId && !actor.IsAdmin)
        {
            var access = await positionAccess.CanAccessAsync(actor, candidateId, cv.PositionId, ct);
            if (!access.Succeeded || access.Value != true)
                return null;
        }

        var values = cv.Profile.AttributeValues.ToDictionary(v => v.AttributeDefinitionId);
        var rows = cv.Position.Attributes
            .OrderBy(a => a.SortOrder)
            .Select(a =>
            {
                values.TryGetValue(a.AttributeDefinitionId, out var value);
                return ToFieldRow(a, value);
            })
            .ToList();

        var includedIds = cv.IncludedProjects.Select(cp => cp.ProjectId).ToHashSet();
        var projects = await db.Projects.AsNoTracking()
            .Where(p => p.ProfileId == cv.ProfileId)
            .OrderBy(p => p.Name)
            .Select(p => new CvProjectOptionDto(
                p.Id,
                p.Name,
                includedIds.Contains(p.Id),
                p.DescriptionMarkdown,
                p.Tags.Select(t => t.Name).OrderBy(t => t).ToList(),
                p.PeriodStart,
                p.PeriodEnd))
            .ToListAsync(ct);

        var missing = CvPublishGate.Missing(cv.Position.Attributes, values);
        var canEdit = actor.IsAdmin || candidateId == actor.UserId;
        var displayName = cv.Profile.AttributeValues
            .FirstOrDefault(v => v.AttributeDefinition.Name == ProfileAttributeNames.Name)
            ?.StringValue;
        var profilePhotoValueId = cv.Profile.AttributeValues
            .FirstOrDefault(v => v.AttributeDefinition.Name == ProfileAttributeNames.Photo)
            ?.Id;

        var dto = await LoadDtoAsync(db, cv.Id, ct) ?? new CvDto(
            cv.Id, cv.ProfileId, candidateId,
            cv.Profile.User.UserName ?? string.Empty,
            cv.PositionId, cv.Position.Title, cv.Position.Company,
            cv.Status, cv.PublishedAt, cv.Version, cv.Position.MaxProjects);

        return new CvDetailDto(
            dto, rows, projects, canEdit, missing.Count == 0, missing,
            displayName, profilePhotoValueId);
    }

    private static CvFieldRowDto ToFieldRow(
        PositionAttribute attribute, ProfileAttributeValue? value) => new(
            attribute.AttributeDefinitionId,
            attribute.AttributeDefinition.Name,
            attribute.AttributeDefinition.DataType,
            attribute.IsRequired,
            attribute.SortOrder,
            value?.StringValue,
            value?.TextValue,
            value?.NumericValue,
            value?.DateValue,
            value?.PeriodStart,
            value?.PeriodEnd,
            value?.BooleanValue,
            value?.DropdownOption,
            value?.ImageObjectKey,
            value?.Version ?? 0,
            ParseChoices(attribute.AttributeDefinition.DataType, attribute.AttributeDefinition.OptionsJson),
            attribute.AttributeDefinition.Category.Name,
            value?.Id);

    private static IReadOnlyList<string> ParseChoices(AttributeDataType dataType, string? optionsJson)
    {
        if (dataType != AttributeDataType.Dropdown || string.IsNullOrWhiteSpace(optionsJson))
            return [];
        try
        {
            var shape = JsonSerializer.Deserialize<DropdownOptionsShape>(
                optionsJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return shape?.Choices?.ToList() ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private sealed record DropdownOptionsShape(string[]? Choices);
}
