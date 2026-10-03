using CvPlatform.Application.Authorization;
using CvPlatform.Application.Common;
using CvPlatform.Core.Data;
using CvPlatform.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace CvPlatform.Application.Integration;

public interface IPositionApiTokenService
{
    Task<Result<PositionApiTokenCreatedDto>> GenerateAsync(
        ActorContext actor, Guid positionId, string? name, CancellationToken ct = default);
    Task<Result<IReadOnlyList<PositionApiTokenDto>>> ListAsync(
        ActorContext actor, Guid positionId, CancellationToken ct = default);
    Task<Result> RevokeAsync(
        ActorContext actor, Guid positionId, Guid tokenId, CancellationToken ct = default);
}

/// <summary>
/// Issues per-position API tokens for the external integration API. Several tokens can be active
/// for one position at the same time; each can be revoked independently.
/// </summary>
public sealed class PositionApiTokenService(
    IAppDbContextFactory factory,
    TimeProvider? timeProvider = null) : IPositionApiTokenService
{
    private DateTime Now => (timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime;

    public async Task<Result<PositionApiTokenCreatedDto>> GenerateAsync(
        ActorContext actor, Guid positionId, string? name, CancellationToken ct = default)
    {
        if (!actor.IsPrivileged)
            return Result<PositionApiTokenCreatedDto>.Failure(
                ErrorCodes.Forbidden, "Only recruiters can manage API tokens.");

        var tokenName = string.IsNullOrWhiteSpace(name) ? "API token" : name.Trim();
        if (tokenName.Length > PositionApiToken.NameMaxLength)
            return Result<PositionApiTokenCreatedDto>.Failure(
                ErrorCodes.ValidationFailed,
                $"Token name must be at most {PositionApiToken.NameMaxLength} characters.");

        await using var db = factory.CreateDbContext();
        var exists = await db.Positions.AsNoTracking().AnyAsync(p => p.Id == positionId, ct);
        if (!exists)
            return Result<PositionApiTokenCreatedDto>.Failure(ErrorCodes.NotFound, "Position was not found.");

        var token = PositionApiTokenSecret.Create();
        var entity = new PositionApiToken
        {
            Id = Guid.NewGuid(),
            PositionId = positionId,
            Name = tokenName,
            TokenHash = PositionApiTokenSecret.Hash(token),
            CreatedByUserId = actor.UserId,
            CreatedAt = Now,
        };
        db.PositionApiTokens.Add(entity);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            return Result<PositionApiTokenCreatedDto>.Failure(
                ErrorCodes.Conflict, "API token could not be created.");
        }

        return Result<PositionApiTokenCreatedDto>.Success(
            new PositionApiTokenCreatedDto(entity.Id, entity.Name, token, entity.CreatedAt));
    }

    public async Task<Result<IReadOnlyList<PositionApiTokenDto>>> ListAsync(
        ActorContext actor, Guid positionId, CancellationToken ct = default)
    {
        if (!actor.IsPrivileged)
            return Result<IReadOnlyList<PositionApiTokenDto>>.Failure(
                ErrorCodes.Forbidden, "Only recruiters can manage API tokens.");

        await using var db = factory.CreateDbContext();
        var exists = await db.Positions.AsNoTracking().AnyAsync(p => p.Id == positionId, ct);
        if (!exists)
            return Result<IReadOnlyList<PositionApiTokenDto>>.Failure(
                ErrorCodes.NotFound, "Position was not found.");

        var tokens = await db.PositionApiTokens.AsNoTracking()
            .Where(t => t.PositionId == positionId)
            .OrderByDescending(t => t.CreatedAt).ThenBy(t => t.Id)
            .Select(t => new PositionApiTokenDto(t.Id, t.Name, t.CreatedAt, t.LastUsedAt, t.RevokedAt))
            .ToListAsync(ct);
        return Result<IReadOnlyList<PositionApiTokenDto>>.Success(tokens);
    }

    public async Task<Result> RevokeAsync(
        ActorContext actor, Guid positionId, Guid tokenId, CancellationToken ct = default)
    {
        if (!actor.IsPrivileged)
            return Result.Failure(ErrorCodes.Forbidden, "Only recruiters can manage API tokens.");

        await using var db = factory.CreateDbContext();
        var token = await db.PositionApiTokens
            .SingleOrDefaultAsync(t => t.Id == tokenId && t.PositionId == positionId, ct);
        if (token is null)
            return Result.Failure(ErrorCodes.NotFound, "API token was not found.");
        if (token.RevokedAt is not null)
            return Result.Success();

        token.RevokedAt = Now;
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}
