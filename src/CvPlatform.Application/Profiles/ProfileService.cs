using CvPlatform.Application.Attributes;
using CvPlatform.Application.Authorization;
using CvPlatform.Application.Common;
using CvPlatform.Application.Cvs;
using CvPlatform.Core.Data;
using CvPlatform.Core.Entities;
using CvPlatform.Core.Enums;
using CvPlatform.Core.Storage;
using Microsoft.EntityFrameworkCore;

namespace CvPlatform.Application.Profiles;

public sealed class ProfileService(IAppDbContextFactory factory, IImageStorage imageStorage) : IProfileService
{
    public async Task<Result<ProfileSummaryDto>> GetSummaryForUserAsync(
        ActorContext actor, Guid userId, CancellationToken ct = default)
    {
        if (!actor.IsAdmin && actor.UserId != userId)
            return Result<ProfileSummaryDto>.Failure(
                ErrorCodes.Forbidden, "You are not allowed to access this profile.");

        await using var db = factory.CreateDbContext();
        var values = await db.ProfileAttributeValues
            .AsNoTracking()
            .Where(v => v.Profile.UserId == userId
                && (v.AttributeDefinition.Name == ProfileAttributeNames.Name
                    || v.AttributeDefinition.Name == ProfileAttributeNames.Photo))
            .Select(v => new
            {
                v.AttributeDefinition.Name,
                v.StringValue,
                v.Id,
                v.ImageObjectKey,
            })
            .ToListAsync(ct);

        return Result<ProfileSummaryDto>.Success(new ProfileSummaryDto(
            values.FirstOrDefault(v => v.Name == ProfileAttributeNames.Name)?.StringValue,
            values.FirstOrDefault(v => v.Name == ProfileAttributeNames.Photo &&
                !string.IsNullOrWhiteSpace(v.ImageObjectKey))?.Id));
    }

    public async Task<Result<ProfileDto>> GetForUserAsync(ActorContext actor, Guid userId, CancellationToken ct = default)
    {
        if (!actor.IsAdmin && actor.UserId != userId)
            return Result<ProfileDto>.Failure(ErrorCodes.Forbidden, "You are not allowed to access this profile.");

        await using var db = factory.CreateDbContext();
        var profile = await db.Profiles
            .AsNoTracking()
            .AsSplitQuery()
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

    public Task<Result<ProfileDto>> SaveAttributeValueAsync(
        ActorContext actor, Guid userId, AttributeValueInput input, CancellationToken ct = default) =>
        SaveAttributeValuesAsync(actor, userId, [input], ct);

    public async Task<Result<ProfileDto>> SaveAttributeValuesAsync(
        ActorContext actor, Guid userId, IReadOnlyList<AttributeValueInput> inputs, CancellationToken ct = default)
    {
        if (!actor.IsAdmin && actor.UserId != userId)
            return Result<ProfileDto>.Failure(ErrorCodes.Forbidden, "You are not allowed to modify this profile.");
        if (inputs.Count == 0)
            return await GetForUserAsync(actor, userId, ct);

        await using var db = factory.CreateDbContext();
        var profile = await db.Profiles.SingleOrDefaultAsync(p => p.UserId == userId, ct);
        if (profile is null)
        {
            var created = await CreateProfileAsync(userId, ct);
            if (!created.Succeeded)
                return Result<ProfileDto>.Failure(created.Error.Code, created.Error.Message);
            profile = created.Value!;
        }

        var ids = inputs.Select(i => i.AttributeDefinitionId).Distinct().ToList();
        var definitions = await db.AttributeDefinitions
            .Where(d => ids.Contains(d.Id))
            .ToDictionaryAsync(d => d.Id, ct);
        var missing = ids.FirstOrDefault(id => !definitions.ContainsKey(id));
        if (missing != Guid.Empty)
            return Result<ProfileDto>.Failure(
                ErrorCodes.NotFound,
                $"Attribute definition {missing} was not found.");

        var normalizedById = new Dictionary<Guid, AttributeValueInput>(ids.Count);
        foreach (var input in inputs)
        {
            var definition = definitions[input.AttributeDefinitionId];
            var normalized = AttributeValueRules.Normalize(definition.DataType, input);
            var validationError = AttributeValueRules.Validate(definition, normalized);
            if (validationError is not null)
                return Result<ProfileDto>.Failure(ErrorCodes.ValidationFailed, validationError);
            if (definition.DataType == AttributeDataType.Image &&
                normalized.ImageObjectKey is not null &&
                !imageStorage.IsOwnedObjectKey(normalized.ImageObjectKey, userId))
                return Result<ProfileDto>.Failure(
                    ErrorCodes.ValidationFailed,
                    "Image must reference an object uploaded by the current user.");
            normalizedById[input.AttributeDefinitionId] = normalized;
        }

        var existing = await db.ProfileAttributeValues
            .Where(v => v.ProfileId == profile.Id && ids.Contains(v.AttributeDefinitionId))
            .ToDictionaryAsync(v => v.AttributeDefinitionId, ct);

        foreach (var input in inputs)
        {
            var normalized = normalizedById[input.AttributeDefinitionId];
            if (!existing.TryGetValue(input.AttributeDefinitionId, out var value))
            {
                db.ProfileAttributeValues.Add(new ProfileAttributeValue
                {
                    ProfileId = profile.Id,
                    AttributeDefinitionId = input.AttributeDefinitionId,
                    StringValue = normalized.StringValue,
                    TextValue = normalized.TextValue,
                    NumericValue = normalized.NumericValue,
                    DateValue = normalized.DateValue,
                    PeriodStart = normalized.PeriodStart,
                    PeriodEnd = normalized.PeriodEnd,
                    BooleanValue = normalized.BooleanValue,
                    DropdownOption = normalized.DropdownOption,
                    ImageObjectKey = normalized.ImageObjectKey,
                });
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
                value.ImageObjectKey = normalized.ImageObjectKey;
            }
        }

        try
        {
            await using var transaction = db.Database.ProviderName != "Microsoft.EntityFrameworkCore.InMemory"
                ? await db.Database.BeginTransactionAsync(ct)
                : null;
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                return Result<ProfileDto>.Failure(
                    ErrorCodes.ConcurrencyConflict, "The value was modified by someone else. Reload and try again.");
            }

            await RefreshPublishedSearchTextsAsync(db, profile.Id, ct);

            if (transaction is not null)
                await transaction.CommitAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<ProfileDto>.Failure(
                ErrorCodes.ConcurrencyConflict, "The value was modified by someone else. Reload and try again.");
        }

        return await GetForUserAsync(actor, userId, ct);
    }

    private async Task RefreshPublishedSearchTextsAsync(IAppDbContext db, Guid profileId, CancellationToken ct)
    {
        var published = await db.Cvs
            .AsSplitQuery()
            .Include(c => c.Position).ThenInclude(p => p.Attributes).ThenInclude(a => a.AttributeDefinition)
            .Include(c => c.Profile).ThenInclude(p => p.User)
            .Include(c => c.Profile).ThenInclude(p => p.AttributeValues).ThenInclude(v => v.AttributeDefinition)
            .Where(c => c.ProfileId == profileId && c.Status == CvStatus.Published)
            .ToListAsync(ct);
        if (published.Count == 0)
            return;

        foreach (var cv in published)
            cv.SearchText = CvSearchTextBuilder.Build(cv);

        await db.SaveChangesAsync(ct);
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
