using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CvPlatform.Core.Crm;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CvPlatform.Infrastructure.Crm;

public sealed class SalesforceCrmService(
    HttpClient httpClient,
    SalesforceTokenProvider tokenProvider,
    IOptions<SalesforceOptions> options,
    ILogger<SalesforceCrmService> logger) : ICrmService
{
    private readonly SalesforceOptions _options = options.Value;

    public bool IsConfigured => _options.IsConfigured;

    public async Task<CrmSyncRecord?> CreateAccountWithContactAsync(
        CrmAccountRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
            return null;

        var token = await tokenProvider.GetTokenAsync(cancellationToken);
        if (token is null)
            return null;

        var apiBase = $"{_options.InstanceUrl.TrimEnd('/')}/services/data/{_options.ApiVersion}";

        var accountId = await CreateSObjectAsync(
            apiBase, token, "Account",
            new Dictionary<string, string?>
            {
                ["Name"] = request.AccountName,
                ["Industry"] = request.Industry,
                ["Phone"] = request.Phone,
                ["Website"] = request.Website,
                ["Description"] = request.Description,
            },
            cancellationToken);
        if (accountId is null)
            return null;

        var contactId = await CreateSObjectAsync(
            apiBase, token, "Contact",
            new Dictionary<string, string?>
            {
                ["AccountId"] = accountId,
                ["FirstName"] = request.ContactFirstName,
                ["LastName"] = request.ContactLastName,
                ["Title"] = request.ContactTitle,
                ["MobilePhone"] = request.ContactMobilePhone,
                ["Email"] = request.ContactEmail,
                ["MailingCity"] = request.MailingCity,
                ["MailingCountry"] = request.MailingCountry,
                ["Description"] = request.Description,
            },
            cancellationToken);
        if (contactId is null)
        {
            await DeleteSObjectAsync(apiBase, token, "Account", accountId, cancellationToken);
            return null;
        }

        return new CrmSyncRecord(accountId, contactId);
    }

    private async Task<string?> CreateSObjectAsync(
        string apiBase,
        string accessToken,
        string objectName,
        Dictionary<string, string?> fields,
        CancellationToken ct)
    {
        using var http = new HttpRequestMessage(HttpMethod.Post, $"{apiBase}/sobjects/{objectName}");
        http.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        http.Content = JsonContent.Create(fields
            .Where(f => !string.IsNullOrWhiteSpace(f.Value))
            .ToDictionary(f => f.Key, f => f.Value));

        using var response = await httpClient.SendAsync(http, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            logger.LogWarning("Salesforce {Object} create failed with {Status}: {Body}",
                objectName, response.StatusCode, body);
            return null;
        }

        using var doc = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        return doc.RootElement.TryGetProperty("id", out var id) ? id.GetString() : null;
    }

    private async Task DeleteSObjectAsync(
        string apiBase,
        string accessToken,
        string objectName,
        string id,
        CancellationToken ct)
    {
        try
        {
            using var http = new HttpRequestMessage(
                HttpMethod.Delete, $"{apiBase}/sobjects/{objectName}/{id}");
            http.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var response = await httpClient.SendAsync(http, ct);
            if (!response.IsSuccessStatusCode)
                logger.LogWarning("Salesforce {Object} rollback delete failed with {Status}",
                    objectName, response.StatusCode);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Salesforce {Object} rollback delete failed", objectName);
        }
    }
}

public sealed class NoOpCrmService(ILogger<NoOpCrmService> logger) : ICrmService
{
    public bool IsConfigured => false;

    public Task<CrmSyncRecord?> CreateAccountWithContactAsync(
        CrmAccountRequest request,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Salesforce is not configured; skipping CRM sync.");
        return Task.FromResult<CrmSyncRecord?>(null);
    }
}
