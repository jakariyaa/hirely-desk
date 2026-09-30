using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CvPlatform.Infrastructure.Crm;

public sealed class SalesforceTokenProvider(
    IOptions<SalesforceOptions> options,
    ILogger<SalesforceTokenProvider> logger) : IDisposable
{
    private readonly HttpClient _httpClient = new();
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(90);

    private readonly SalesforceOptions _options = options.Value;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _cachedToken;
    private DateTimeOffset _validUntil;

    public async Task<string?> GetTokenAsync(CancellationToken ct = default)
    {
        if (!_options.IsConfigured)
            return null;

        if (_cachedToken is not null && DateTimeOffset.UtcNow < _validUntil)
            return _cachedToken;

        await _gate.WaitAsync(ct);
        try
        {
            if (_cachedToken is not null && DateTimeOffset.UtcNow < _validUntil)
                return _cachedToken;

            var client = _httpClient;
            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = _options.ClientId,
                ["client_secret"] = _options.ClientSecret,
            });
            using var response = await client.PostAsync(
                $"{_options.InstanceUrl.TrimEnd('/')}/services/oauth2/token", content, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Salesforce token request failed with {Status}", response.StatusCode);
                return null;
            }

            using var doc = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            if (!doc.RootElement.TryGetProperty("access_token", out var token))
                return null;

            _cachedToken = token.GetString();
            _validUntil = DateTimeOffset.UtcNow.Add(TokenLifetime);
            return _cachedToken;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        _gate.Dispose();
    }
}
