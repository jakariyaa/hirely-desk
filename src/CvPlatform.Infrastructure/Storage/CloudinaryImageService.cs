using CvPlatform.Core.Storage;
using Microsoft.Extensions.Options;

namespace CvPlatform.Infrastructure.Storage;

public sealed class CloudinaryImageService(
    IOptions<CloudinaryOptions> options) : ICloudinaryClientConfiguration
{
    private readonly CloudinaryOptions _options = options.Value;

    public bool IsConfigured => _options.IsConfigured;

    public (string CloudName, string UploadPreset)? ClientConfig =>
        string.IsNullOrWhiteSpace(_options.CloudName) || string.IsNullOrWhiteSpace(_options.UploadPreset)
            ? null
            : (_options.CloudName, _options.UploadPreset);

    public (string CloudName, string ApiKey)? SignedClientConfig =>
        _options.IsSignedUploadConfigured
            ? (_options.CloudName, _options.ApiKey)
            : null;

}
