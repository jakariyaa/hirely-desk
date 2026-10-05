namespace CvPlatform.Infrastructure.Support;

public sealed class GoogleOptions
{
    public const string SectionName = "Google";

    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string RefreshToken { get; set; } = "";
    public string DriveFolderId { get; set; } = "";
    public string DriveProcessedFolderId { get; set; } = "";
    public string WebhookUrl { get; set; } = "";
    public string WebhookToken { get; set; } = "";
    public string ApplicationName { get; set; } = "HirelyDesk";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ClientId) &&
        !string.IsNullOrWhiteSpace(ClientSecret) &&
        !string.IsNullOrWhiteSpace(RefreshToken) &&
        !string.IsNullOrWhiteSpace(DriveFolderId);

    public bool WebhookConfigured =>
        IsConfigured &&
        !string.IsNullOrWhiteSpace(DriveProcessedFolderId) &&
        !string.IsNullOrWhiteSpace(WebhookUrl) &&
        !string.IsNullOrWhiteSpace(WebhookToken) &&
        Uri.TryCreate(WebhookUrl, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps;
}

public sealed class SupportOptions
{
    public const string SectionName = "Support";

    public string AdminEmails { get; set; } = "";

    public IReadOnlyList<string> AdminEmailList =>
        AdminEmails.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
