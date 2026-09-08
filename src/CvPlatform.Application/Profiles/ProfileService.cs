using CvPlatform.Application.Attributes;
using CvPlatform.Application.Authorization;
using CvPlatform.Application.Common;
using CvPlatform.Core.Data;
using CvPlatform.Core.Entities;
using CvPlatform.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace CvPlatform.Application.Profiles;

public sealed class ProfileService(IAppDbContextFactory factory) : IProfileService
{
    public async Task<Result<ProfileDto>> GetForUserAsync(ActorContext actor, Guid userId, CancellationToken ct = default)
    {
        if (!actor.IsAdmin && actor.UserId != userId)
            return Result<ProfileDto>.Failure(ErrorCodes.Forbidden, "You are not allowed to access this profile.");

        await using var db = factory.CreateDbContext();
        var profile = await db.Profiles
            .AsNoTracking()
            .Include(p => p.AttributeValues).ThenInclude(v => v.AttributeDefinition)
            .Include(p => p.Projects)
            .SingleOrDefaultAsync(p => p.UserId == userId, ct);

        if (profile is null)
        {
            var created = await CreateProfileAsync(userId, ct);
            if (!created.Succeeded)
                return Result<ProfileDto>.Failure(created.Error.Code, created.Error.Message);

            profile = created.Value!;
        }

        return Result<ProfileDto>.Success(new ProfileDto(
            profile.Id,
            profile.UserId,
            profile.AttributeValues.Select(AttributeValueDto.FromEntity).ToList()));
    }

    public async Task<Result<ProfileDto>> SaveAttributeValueAsync(
        ActorContext actor, Guid userId, AttributeValueInput input, CancellationToken ct = default)
    {
        if (!actor.IsAdmin && actor.UserId != userId)
            return Result<ProfileDto>.Failure(ErrorCodes.Forbidden, "You are not allowed to modify this profile.");

        await using var db = factory.CreateDbContext();
        var profile = await db.Profiles.SingleOrDefaultAsync(p => p.UserId == userId, ct);
        if (profile is null)
        {
            var created = await CreateProfileAsync(userId, ct);
            if (!created.Succeeded)
                return Result<ProfileDto>.Failure(created.Error.Code, created.Error.Message);
            profile = created.Value!;
        }

        var definition = await db.AttributeDefinitions.FindAsync([input.AttributeDefinitionId], ct);
        if (definition is null)
            return Result<ProfileDto>.Failure(
                ErrorCodes.NotFound,
                $"Attribute definition {input.AttributeDefinitionId} was not found.");

        var attributeDefinition = definition;

        var normalized = AttributeValueRules.Normalize(attributeDefinition.DataType, input);
        var validationError = AttributeValueRules.Validate(attributeDefinition, normalized);
        if (validationError is not null)
            return Result<ProfileDto>.Failure(ErrorCodes.ValidationFailed, validationError!);

        var value = await db.ProfileAttributeValues.SingleOrDefaultAsync(
            v => v.ProfileId == profile.Id && v.AttributeDefinitionId == attributeDefinition.Id, ct);

        if (value is null)
        {
            value = new ProfileAttributeValue
            {
                ProfileId = profile.Id,
                AttributeDefinitionId = attributeDefinition.Id,
                StringValue = normalized.StringValue,
                TextValue = normalized.TextValue,
                NumericValue = normalized.NumericValue,
                DateValue = normalized.DateValue,
                PeriodStart = normalized.PeriodStart,
                PeriodEnd = normalized.PeriodEnd,
                BooleanValue = normalized.BooleanValue,
                DropdownOption = normalized.DropdownOption,
                ImageUrl = normalized.ImageUrl,
            };
            db.ProfileAttributeValues.Add(value);
        }
        else
        {
            if (input.ExpectedVersion is null || input.ExpectedVersion.Value != value.Version)
                return Result<ProfileDto>.Failure(
                    ErrorCodes.ConcurrencyConflict,
                    "The value was modified by someone else. Reload and try again.");

            value.StringValue = normalized.StringValue;
            value.TextValue = normalized.TextValue;
            value.NumericValue = normalized.NumericValue;
            value.DateValue = normalized.DateValue;
            value.PeriodStart = normalized.PeriodStart;
            value.PeriodEnd = normalized.PeriodEnd;
            value.BooleanValue = normalized.BooleanValue;
            value.DropdownOption = normalized.DropdownOption;
            value.ImageUrl = normalized.ImageUrl;
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<ProfileDto>.Failure(
                ErrorCodes.ConcurrencyConflict, "The value was modified by someone else. Reload and try again.");
        }

        return await GetForUserAsync(actor, userId, ct);
    }

    public Task<Result<IReadOnlyList<AttributeCategoryDto>>> GetCatalogAsync(CancellationToken ct = default) =>
        new AttributeCatalogService(factory).GetCatalogAsync(ct);

    private async Task<Result<Profile>> CreateProfileAsync(Guid userId, CancellationToken ct)
    {
        await using var db = factory.CreateDbContext();
        var profile = new Profile { Id = Guid.NewGuid(), UserId = userId };
        db.Profiles.Add(profile);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Lost a race with another request creating the same profile; re-read.
            var existing = await db.Profiles.AsNoTracking().SingleOrDefaultAsync(p => p.UserId == userId, ct);
            if (existing is null)
                return Result<Profile>.Failure(
                    ErrorCodes.Conflict, "Profile could not be created. Please try again.");
            return Result<Profile>.Success(existing);
        }

        return Result<Profile>.Success(profile);
    }

}
