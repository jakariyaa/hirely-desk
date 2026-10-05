using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CvPlatform.Infrastructure.Support;

public sealed class DriveWatchRegistration(
    IOptions<GoogleOptions> options,
    GoogleCredentialFactory credentials,
    ILogger<DriveWatchRegistration> logger)
{
    private readonly GoogleOptions _options = options.Value;

    public async Task EnsureWatchAsync(CancellationToken ct = default)
    {
        if (!_options.WebhookConfigured)
        {
            logger.LogInformation("Drive webhook not configured; skipping change watch registration");
            return;
        }

        try
        {
            var drive = new DriveService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credentials.Create(),
                ApplicationName = _options.ApplicationName,
            });
            var startToken = await drive.Changes.GetStartPageToken().ExecuteAsync(ct);
            var channel = await drive.Changes.Watch(
                new Google.Apis.Drive.v3.Data.Channel
                {
                    Id = Guid.NewGuid().ToString(),
                    Type = "web_hook",
                    Address = _options.WebhookUrl,
                    Token = _options.WebhookToken,
                }, startToken.StartPageTokenValue).ExecuteAsync(ct);
            logger.LogInformation("Drive change channel {ChannelId} expires at {Expiration}", channel.Id, channel.Expiration);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not register Drive change watch; support upload trigger disabled until next restart");
        }
    }
}
