namespace CvPlatform.Core.Crm;

public sealed record CrmAccountRequest(
    string AccountName,
    string? Industry,
    string? Phone,
    string? Website,
    string ContactFirstName,
    string ContactLastName,
    string? ContactTitle,
    string? ContactMobilePhone,
    string? ContactEmail,
    string? MailingCity,
    string? MailingCountry,
    string? Description);

public sealed record CrmSyncRecord(string AccountId, string ContactId);

public interface ICrmService
{
    bool IsConfigured { get; }

    Task<CrmSyncRecord?> CreateAccountWithContactAsync(
        CrmAccountRequest request,
        CancellationToken cancellationToken = default);
}
