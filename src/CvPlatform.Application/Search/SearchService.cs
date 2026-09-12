using CvPlatform.Application.Access;
using CvPlatform.Application.Authorization;
using CvPlatform.Application.Common;
using CvPlatform.Application.Positions;
using CvPlatform.Core.Data;
using CvPlatform.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace CvPlatform.Application.Search;

public sealed class SearchService(
    IAppDbContextFactory factory,
    IPositionAccessService positionAccess,
    IFullTextMatcher? matcher = null) : ISearchService
{
    public async Task<Result<SearchResultDto>> SearchAsync(
        ActorContext actor, string term, PageRequest? page = null, CancellationToken ct = default)
    {
        var request = page ?? new PageRequest(1, 10);
        var positions = await SearchPositionsAsync(actor, term, request, ct);
        if (!positions.Succeeded)
            return Result<SearchResultDto>.Failure(positions.Error.Code, positions.Error.Message);
        var cvs = await SearchCvsAsync(actor, term, request, ct);
        if (!cvs.Succeeded)
            return Result<SearchResultDto>.Failure(cvs.Error.Code, cvs.Error.Message);
        return Result<SearchResultDto>.Success(new SearchResultDto(positions.Value, cvs.Value));
    }

    public async Task<Result<PagedResult<PositionDto>>> SearchPositionsAsync(
        ActorContext actor, string term, PageRequest page, CancellationToken ct = default)
    {
        var q = term?.Trim() ?? string.Empty;
        if (q.Length == 0)
            return Result<PagedResult<PositionDto>>.Success(
                new PagedResult<PositionDto>([], 0, page.Page, page.PageSize));
        await using var db = factory.CreateDbContext();
        var useFullText = matcher?.IsSupported(db) == true;

        IQueryable<Core.Entities.Position> filtered = db.Positions.AsNoTracking();
        if (useFullText && matcher is not null)
            filtered = matcher.MatchPositions(filtered, q);
        else
            filtered = filtered.Where(p =>
                p.Title.Contains(q) || p.ShortDescription.Contains(q) ||
                (p.Company != null && p.Company.Contains(q)));

        var accessible = positionAccess.ApplyPositionFilter(filtered, db, actor);
        var visibleTotal = await accessible.CountAsync(ct);
        var pageItems = await accessible.OrderBy(p => p.Title).ThenBy(p => p.Id)
            .Skip(page.Skip).Take(page.PageSize)
            .Select(p => new PositionDto(
                p.Id, p.OwnerId, p.Title, p.ShortDescription, p.Company, p.Level,
                p.IsPublic, p.MaxProjects, p.Version))
            .ToListAsync(ct);
        return Result<PagedResult<PositionDto>>.Success(
            new PagedResult<PositionDto>(pageItems, visibleTotal, page.Page, page.PageSize));
    }

    public async Task<Result<PagedResult<CvSearchHitDto>>> SearchCvsAsync(
        ActorContext actor, string term, PageRequest page, CancellationToken ct = default)
    {
        var q = term?.Trim() ?? string.Empty;
        if (q.Length == 0)
            return Result<PagedResult<CvSearchHitDto>>.Success(
                new PagedResult<CvSearchHitDto>([], 0, page.Page, page.PageSize));
        await using var db = factory.CreateDbContext();
        var useFullText = matcher?.IsSupported(db) == true;

        IQueryable<Core.Entities.Cv> filtered = db.Cvs.AsNoTracking();
        if (!actor.IsAdmin)
            filtered = filtered.Where(c => c.Status == CvStatus.Published && c.PublishedAt != null);
        if (!actor.IsAdmin && !actor.IsRecruiter)
            filtered = filtered.Where(c => c.Profile.UserId == actor.UserId);
        if (useFullText && matcher is not null)
            filtered = matcher.MatchCvs(filtered, q);
        else
            filtered = filtered.Where(c => c.SearchText != null && c.SearchText.Contains(q));

        var accessible = positionAccess.ApplyCvFilter(filtered, db, actor);
        var visibleTotal = await accessible.CountAsync(ct);
        var pageItems = await accessible.OrderByDescending(c => c.PublishedAt).ThenBy(c => c.Id)
            .Skip(page.Skip).Take(page.PageSize)
            .Select(c => new CvSearchHitDto(
                c.Id, c.PositionId, c.Position.Title, c.Position.Company,
                c.Profile.User.UserName ?? string.Empty, c.PublishedAt))
            .ToListAsync(ct);
        return Result<PagedResult<CvSearchHitDto>>.Success(
            new PagedResult<CvSearchHitDto>(pageItems, visibleTotal, page.Page, page.PageSize));
    }
}
