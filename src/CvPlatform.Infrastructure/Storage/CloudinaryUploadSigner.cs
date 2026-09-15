using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace CvPlatform.Infrastructure.Storage;

public sealed record CloudinaryUploadSignature(
    string ApiKey,
    long Timestamp,
    string Folder,
    string PublicId,
    string Signature);

public interface ICloudinaryUploadSigner
{
    CloudinaryUploadSignature? Create(Guid userId);
}

public sealed class CloudinaryUploadSigner(IOptions<CloudinaryOptions> options)
    : ICloudinaryUploadSigner
{
    private readonly CloudinaryOptions _options = options.Value;

    public CloudinaryUploadSignature? Create(Guid userId)
    {
        if (!_options.IsSignedUploadConfigured)
            return null;

        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var folder = $"{_options.Folder.Trim('/')}/{userId:D}/profile";
        var publicId = Guid.NewGuid().ToString("N");
        var parameters = $"folder={folder}&public_id={publicId}&timestamp={timestamp}";
        var signature = Convert.ToHexString(
                SHA1.HashData(Encoding.UTF8.GetBytes($"{parameters}{_options.ApiSecret}")))
            .ToLowerInvariant();

        return new CloudinaryUploadSignature(
            _options.ApiKey, timestamp, folder, publicId, signature);
    }
}
