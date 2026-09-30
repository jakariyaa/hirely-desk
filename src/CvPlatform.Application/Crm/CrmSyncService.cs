using CvPlatform.Application.Authorization;
using CvPlatform.Application.Common;
using CvPlatform.Application.Profiles;
using CvPlatform.Core.Crm;
using CvPlatform.Core.Data;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace CvPlatform.Application.Crm;

public sealed class CrmSyncService(
    IAppDbContextFactory factory,
    ICrmService crm,
    IValidator<CrmSyncInput>? validator = null) : ICrmSyncService
{
    public bool IsConfigured => crm.IsConfigured;

    public async Task<bool> IsSyncedAsync(ActorContext actor, Guid userId, CancellationToken ct = default)
    {
        if (!actor.IsAdmin && actor.UserId != userId)
            return false;

        await using var db = factory.CreateDbContext();
        return await db.Users
            .AsNoTracking()
            .AnyAsync(u => u.Id == userId && u.SalesforceAccountId != null, ct);
    }

    public async Task<Result<CrmSyncOutcome>> SyncAsync(
        ActorContext actor, Guid userId, CrmSyncInput input, CancellationToken ct = default)
    {
        if (!actor.IsAdmin && actor.UserId != userId)
            return Result<CrmSyncOutcome>.Failure(
                ErrorCodes.Forbidden, "You are not allowed to sync this user.");

        var validation = (validator ?? new Validation.CrmSyncInputValidator()).Validate(input);
        if (!validation.IsValid)
            return Result<CrmSyncOutcome>.Failure(
                ErrorCodes.ValidationFailed,
                string.Join(" ", validation.Errors.Select(e => e.ErrorMessage)));

        if (!crm.IsConfigured)
            return Result<CrmSyncOutcome>.Failure(
                ErrorCodes.ServiceUnavailable, "Salesforce integration is not configured.");

        await using var db = factory.CreateDbContext();
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
            return Result<CrmSyncOutcome>.Failure(ErrorCodes.NotFound, "User not found.");

        if (!string.IsNullOrWhiteSpace(user.SalesforceAccountId))
            return Result<CrmSyncOutcome>.Failure(
                ErrorCodes.Conflict, "This user is already synced to Salesforce.");

        var displayName = await db.ProfileAttributeValues
            .AsNoTracking()
            .Where(v => v.Profile.UserId == userId && v.AttributeDefinition.Name == ProfileAttributeNames.Name)
            .Select(v => v.StringValue)
            .FirstOrDefaultAsync(ct);

        var fullName = !string.IsNullOrWhiteSpace(displayName) ? displayName! : "Unknown";
        var (firstName, lastName) = SplitName(fullName);

        CrmSyncRecord? record;
        try
        {
            record = await crm.CreateAccountWithContactAsync(new CrmAccountRequest(
                input.AccountName,
                input.Industry,
                input.AccountPhone,
                input.Website,
                firstName,
                lastName,
                input.ContactTitle,
                input.ContactMobilePhone,
                user.Email,
                input.MailingCity,
                input.MailingCountry,
                input.Description), ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException
            or System.Text.Json.JsonException)
        {
            return Result<CrmSyncOutcome>.Failure(
                ErrorCodes.ServiceUnavailable, "Failed to reach Salesforce. Try again later.");
        }

        if (record is null)
            return Result<CrmSyncOutcome>.Failure(
                ErrorCodes.ServiceUnavailable, "Failed to create records in Salesforce.");

        user.SalesforceAccountId = record.AccountId;
        user.SalesforceContactId = record.ContactId;
        await db.SaveChangesAsync(ct);

        return Result<CrmSyncOutcome>.Success(new CrmSyncOutcome(record.AccountId, record.ContactId));
    }

    private static (string FirstName, string LastName) SplitName(string fullName)
    {
        var parts = fullName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            0 => ("", "Unknown"),
            1 => ("", parts[0]),
            _ => (string.Join(' ', parts[..^1]), parts[^1]),
        };
    }
}
