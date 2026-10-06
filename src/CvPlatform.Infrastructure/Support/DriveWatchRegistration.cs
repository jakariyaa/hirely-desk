using Google.Apis.Drive.v3;
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
            Google.Apis.Drive.v3.Data.Channel channel = null!;
            var failures = 0;
            while (true)
            {
                try
                {
                    var drive = credentials.CreateDriveService();
                    var startToken = await drive.Changes.GetStartPageToken().ExecuteAsync(ct);
                    channel = await drive.Changes.Watch(
                        new Google.Apis.Drive.v3.Data.Channel
                        {
                            Id = Guid.NewGuid().ToString(),
                            Type = "web_hook",
                            Address = _options.WebhookUrl,
                            Token = _options.WebhookToken,
                        }, startToken.StartPageTokenValue).ExecuteAsync(ct);
                    break;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    failures++;
                    if (failures >= 3)
                        throw;
                    logger.LogWarning(ex, "Drive watch registration attempt {Attempt} failed; retrying in 10s", failures);
                    await Task.Delay(TimeSpan.FromSeconds(10), ct);
                }
            }
            logger.LogInformation("Drive change channel {ChannelId} expires at {Expiration}", channel.Id, channel.Expiration);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not register Drive change watch; support ticket sweep will still process tickets");
        }
    }
}
