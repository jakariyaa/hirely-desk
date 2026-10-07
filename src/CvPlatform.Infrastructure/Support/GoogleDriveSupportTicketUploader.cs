using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using CvPlatform.Core.Support;
using Google.Apis.Drive.v3;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CvPlatform.Infrastructure.Support;

public sealed class GoogleDriveSupportTicketUploader(
    IOptions<GoogleOptions> options,
    GoogleCredentialFactory credentials,
    RateLimiter ticketRateLimiter,
    ILogger<GoogleDriveSupportTicketUploader> logger) : ISupportTicketUploader
{
    public bool IsConfigured => _options.IsConfigured;
    private readonly GoogleOptions _options = options.Value;

    public async Task<bool> UploadAsync(SupportTicket ticket, CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            logger.LogDebug("Google Drive is not configured; support ticket not uploaded");
            return false;
        }

        using var lease = await ticketRateLimiter.AcquireAsync(1, ct);
        if (!lease.IsAcquired)
        {
            logger.LogWarning("Support ticket rate limit reached; ticket not uploaded");
            return false;
        }

        var json = JsonSerializer.Serialize(ticket, new JsonSerializerOptions { WriteIndented = true });

        var service = credentials.CreateDriveService();
        var metadata = new Google.Apis.Drive.v3.Data.File
        {
            Name = $"ticket-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.json",
            Parents = [_options.DriveFolderId],
            MimeType = "application/json",
        };
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var request = service.Files.Create(metadata, stream, "application/json");
        request.Fields = "id";
        await request.UploadAsync(ct);
        logger.LogInformation("Support ticket uploaded to Drive as {File}", metadata.Name);
        return request.ResponseBody is not null;
    }
}