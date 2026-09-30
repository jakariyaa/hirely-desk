using CvPlatform.Application.Authorization;
using CvPlatform.Application.Common;

namespace CvPlatform.Application.Crm;

public sealed record CrmSyncInput(
    string AccountName,
    string? Industry,
    string? AccountPhone,
    string? Website,
    string? ContactTitle,
    string? ContactMobilePhone,
    string? MailingCity,
    string? MailingCountry,
    string? Description);

public sealed record CrmSyncOutcome(string AccountId, string ContactId);

public interface ICrmSyncService
{
    /// <summary>Whether the CRM integration is configured and ready to use.</summary>
    bool IsConfigured { get; }

    /// <summary>Whether the user was already synced to Salesforce.</summary>
    Task<bool> IsSyncedAsync(ActorContext actor, Guid userId, CancellationToken ct = default);

    /// <summary>Creates an Account with a linked Contact in Salesforce and stores the ids.</summary>
    Task<Result<CrmSyncOutcome>> SyncAsync(
        ActorContext actor, Guid userId, CrmSyncInput input, CancellationToken ct = default);
}
