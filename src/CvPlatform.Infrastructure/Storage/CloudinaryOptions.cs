namespace CvPlatform.Infrastructure.Storage;

public sealed class CloudinaryOptions
{
    public const string SectionName = "Cloudinary";

    public string CloudName { get; set; } = "";
    public string UploadPreset { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public string ApiSecret { get; set; } = "";
    public string Folder { get; set; } = "cvplatform";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(CloudName) &&
        ((!string.IsNullOrWhiteSpace(UploadPreset)) ||
         (!string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(ApiSecret)));

    public bool IsSignedUploadConfigured =>
        !string.IsNullOrWhiteSpace(CloudName) &&
        !string.IsNullOrWhiteSpace(ApiKey) &&
        !string.IsNullOrWhiteSpace(ApiSecret);
}
