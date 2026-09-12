using CvPlatform.Application.Access;
using CvPlatform.Application.Authorization;
using CvPlatform.Application.Common;
using CvPlatform.Application.Validation;
using CvPlatform.Core.Data;
using CvPlatform.Core.Entities;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace CvPlatform.Application.Discussions;

public sealed class DiscussionService(
    IAppDbContextFactory factory,
    IPositionAccessService positionAccess,
    IDiscussionNotifier notifier,
    IValidator<DiscussionPostInput>? validator = null) : IDiscussionService
{
    public async Task<Result<PagedResult<DiscussionPostDto>>> ListAsync(
        ActorContext actor, Guid positionId, PageRequest page, CancellationToken ct = default)
    {
        var accessible = await positionAccess.GetAccessibleAsync(actor, positionId, ct);
        if (!accessible.Succeeded)
            return Result<PagedResult<DiscussionPostDto>>.Failure(accessible.Error.Code, accessible.Error.Message);

        await using var db = factory.CreateDbContext();
        var query = db.DiscussionPosts.AsNoTracking().Where(p => p.PositionId == positionId);
        var total = await query.CountAsync(ct);
        var items = await query.OrderBy(p => p.CreatedAt).ThenBy(p => p.Id)
            .Skip(page.Skip).Take(page.PageSize)
            .Select(p => new DiscussionPostDto(
                p.Id, p.PositionId, p.AuthorId,
                p.Author.UserName ?? string.Empty,
                p.CreatedAt, p.TextMarkdown))
            .ToListAsync(ct);
        return Result<PagedResult<DiscussionPostDto>>.Success(
            new PagedResult<DiscussionPostDto>(items, total, page.Page, page.PageSize));
    }

    public async Task<Result<DiscussionPostDto>> AddAsync(
        ActorContext actor, Guid positionId, DiscussionPostInput input, CancellationToken ct = default)
    {
        if (!actor.IsCandidate && !actor.IsPrivileged)
            return Result<DiscussionPostDto>.Failure(ErrorCodes.Forbidden, "Only authenticated users can post discussions.");
        var validation = (validator ?? new DiscussionPostInputValidator()).Validate(input);
        if (!validation.IsValid)
            return Result<DiscussionPostDto>.Failure(ErrorCodes.ValidationFailed, validation.Errors[0].ErrorMessage);
        var text = input.TextMarkdown.Trim();

        var accessible = await positionAccess.GetAccessibleAsync(actor, positionId, ct);
        if (!accessible.Succeeded)
            return Result<DiscussionPostDto>.Failure(accessible.Error.Code, accessible.Error.Message);

        await using var db = factory.CreateDbContext();
        var post = new DiscussionPost
        {
            Id = Guid.NewGuid(),
            PositionId = positionId,
            AuthorId = actor.UserId,
            CreatedAt = DateTime.UtcNow,
            TextMarkdown = text,
        };
        db.DiscussionPosts.Add(post);
        await db.SaveChangesAsync(ct);

        var authorName = await db.Users.AsNoTracking()
            .Where(u => u.Id == actor.UserId)
            .Select(u => u.UserName ?? string.Empty)
            .SingleOrDefaultAsync(ct) ?? string.Empty;
        var dto = new DiscussionPostDto(post.Id, post.PositionId, post.AuthorId, authorName, post.CreatedAt, post.TextMarkdown);
        await notifier.NotifyAsync(positionId, dto, ct);
        return Result<DiscussionPostDto>.Success(dto);
    }
}
