using System.Text.Json.Serialization;

namespace CvPlatform.Core.Support;

public sealed record SupportTicket(
    [property: JsonPropertyName("Reported by")] string ReportedBy,
    [property: JsonPropertyName("Position")] string? Position,
    [property: JsonPropertyName("Link")] string Link,
    [property: JsonPropertyName("Priority")] string Priority,
    [property: JsonPropertyName("Summary")] string Summary,
    [property: JsonPropertyName("Admins")] IReadOnlyList<string> AdminEmails);

public interface ISupportTicketUploader
{
    bool IsConfigured { get; }
    Task<bool> UploadAsync(SupportTicket ticket, CancellationToken ct = default);
}
