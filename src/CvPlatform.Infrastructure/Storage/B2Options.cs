namespace CvPlatform.Infrastructure.Storage;

public sealed class B2Options
{
    public const string SectionName = "B2";

    public string Region { get; set; } = "";
    public string BucketName { get; set; } = "";
    public string ApplicationKeyId { get; set; } = "";
    public string ApplicationKey { get; set; } = "";
    public string KeyPrefix { get; set; } = "users";
    public int PresignedUrlLifetimeSeconds { get; set; } = 300;
    public int DownloadUrlLifetimeSeconds { get; set; } = 60;
    public long MaxUploadBytes { get; set; } = 5 * 1024 * 1024;

    public string ServiceUrl => $"https://s3.{Region}.backblazeb2.com";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Region) &&
        !string.IsNullOrWhiteSpace(BucketName) &&
        !string.IsNullOrWhiteSpace(ApplicationKeyId) &&
        !string.IsNullOrWhiteSpace(ApplicationKey) &&
        !string.IsNullOrWhiteSpace(KeyPrefix);
}
