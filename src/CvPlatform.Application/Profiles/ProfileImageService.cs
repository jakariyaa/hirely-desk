using CvPlatform.Application.Access;
using CvPlatform.Application.Authorization;
using CvPlatform.Application.Common;
using CvPlatform.Core.Data;
using CvPlatform.Core.Enums;
using CvPlatform.Core.Storage;
using Microsoft.EntityFrameworkCore;

namespace CvPlatform.Application.Profiles;

public interface IProfileImageService
{
    Task<Result<ImageDownloadTicket>> CreateDownloadTicketAsync(
        ActorContext actor,
        Guid imageValueId,
        Guid? cvId = null,
        CancellationToken cancellationToken = default);
}

public sealed class ProfileImageService(
    IAppDbContextFactory factory,
    IPositionAccessService positionAccess,
    IImageStorage imageStorage) : IProfileImageService
{
    public async Task<Result<ImageDownloadTicket>> CreateDownloadTicketAsync(
        ActorContext actor,
        Guid imageValueId,
        Guid? cvId = null,
        CancellationToken cancellationToken = default)
    {
        await using var db = factory.CreateDbContext();
        var image = await db.ProfileAttributeValues
            .AsNoTracking()
            .Where(v => v.Id == imageValueId &&
                        v.AttributeDefinition.DataType == AttributeDataType.Image)
            .Select(v => new
            {
                v.ProfileId,
                ProfileUserId = v.Profile.UserId,
                v.ImageObjectKey,
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (image is null || string.IsNullOrWhiteSpace(image.ImageObjectKey))
            return Result<ImageDownloadTicket>.Failure(ErrorCodes.NotFound, "Image was not found.");

        if (cvId is Guid requestedCvId)
        {
            var cv = await db.Cvs
                .AsNoTracking()
                .Where(c => c.Id == requestedCvId && c.ProfileId == image.ProfileId)
                .Select(c => new
                {
                    c.Profile.UserId,
                    c.PositionId,
                    c.Status,
                })
                .SingleOrDefaultAsync(cancellationToken);

            if (cv is null)
                return Result<ImageDownloadTicket>.Failure(ErrorCodes.NotFound, "CV was not found.");

            if (!actor.IsAdmin && cv.UserId != actor.UserId)
            {
                if (cv.Status != CvStatus.Published)
                    return Result<ImageDownloadTicket>.Failure(
                        ErrorCodes.Forbidden, "You do not have access to this image.");

                var access = await positionAccess.CanAccessAsync(
                    actor, cv.UserId, cv.PositionId, cancellationToken);
                if (!access.Succeeded || access.Value != true)
                    return Result<ImageDownloadTicket>.Failure(
                        ErrorCodes.Forbidden, "You do not have access to this image.");
            }
        }
        else if (!actor.IsAdmin && image.ProfileUserId != actor.UserId)
        {
            return Result<ImageDownloadTicket>.Failure(
                ErrorCodes.Forbidden, "You do not have access to this image.");
        }

        if (!imageStorage.IsOwnedObjectKey(image.ImageObjectKey, image.ProfileUserId))
            return Result<ImageDownloadTicket>.Failure(ErrorCodes.NotFound, "Image was not found.");

        var ticket = imageStorage.CreateDownloadTicket(image.ImageObjectKey);
        return ticket is null
            ? Result<ImageDownloadTicket>.Failure(ErrorCodes.NotFound, "Image was not found.")
            : Result<ImageDownloadTicket>.Success(ticket);
    }
}
