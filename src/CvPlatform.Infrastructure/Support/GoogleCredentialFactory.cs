using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Drive.v3;
using Google.Apis.Gmail.v1;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using Microsoft.Extensions.Options;

namespace CvPlatform.Infrastructure.Support;

public sealed class GoogleCredentialFactory(IOptions<GoogleOptions> options)
{
    private const string ApplicationName = "HirelyDesk";

    private static readonly string[] Scopes =
    [
        DriveService.Scope.Drive,
        GmailService.Scope.GmailSend,
    ];

    private readonly GoogleOptions _options = options.Value;

    public DriveService CreateDriveService() => new(new BaseClientService.Initializer
    {
        HttpClientInitializer = Create(),
        ApplicationName = ApplicationName,
    });

    public GmailService CreateGmailService() => new(new BaseClientService.Initializer
    {
        HttpClientInitializer = Create(),
        ApplicationName = ApplicationName,
    });

    private UserCredential Create()
    {
        var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
        {
            ClientSecrets = new ClientSecrets
            {
                ClientId = _options.ClientId,
                ClientSecret = _options.ClientSecret,
            },
            Scopes = Scopes,
            DataStore = new NullDataStore(),
        });
        return new UserCredential(flow, "support-integration", new Google.Apis.Auth.OAuth2.Responses.TokenResponse
        {
            RefreshToken = _options.RefreshToken,
        });
    }

    private sealed class NullDataStore : IDataStore
    {
        public Task ClearAsync() => Task.CompletedTask;
        public Task DeleteAsync<T>(string key) => Task.CompletedTask;
        public Task<T> GetAsync<T>(string key) => Task.FromResult<T>(default!);
        public Task StoreAsync<T>(string key, T value) => Task.CompletedTask;
    }
}
