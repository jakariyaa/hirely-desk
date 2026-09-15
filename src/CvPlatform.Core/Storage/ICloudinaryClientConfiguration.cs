namespace CvPlatform.Core.Storage;

public interface ICloudinaryClientConfiguration
{
    bool IsConfigured { get; }
    (string CloudName, string UploadPreset)? ClientConfig { get; }
    (string CloudName, string ApiKey)? SignedClientConfig { get; }
}
