using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using CvPlatform.Core.Storage;
using Microsoft.Extensions.Options;

namespace CvPlatform.Infrastructure.Storage;

public sealed class B2ImageStorage : IImageStorage, IDisposable
{
    private static readonly IReadOnlyDictionary<string, string> ContentTypeExtensions =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["image/jpeg"] = "jpg",
            ["image/png"] = "png",
            ["image/webp"] = "webp",
        };

    private readonly B2Options _options;
    private readonly IAmazonS3? _client;

    public B2ImageStorage(IOptions<B2Options> options)
    {
        _options = options.Value;
        if (_options.IsConfigured)
        {
            _client = new AmazonS3Client(
                new BasicAWSCredentials(_options.ApplicationKeyId, _options.ApplicationKey),
                new AmazonS3Config
                {
                    ServiceURL = _options.ServiceUrl,
                    AuthenticationRegion = _options.Region,
                    ForcePathStyle = true,
                });
        }
    }

    public bool IsConfigured => _options.IsConfigured && _client is not null;

    public long MaxUploadBytes => _options.MaxUploadBytes;

    public bool IsAllowedContentType(string contentType) =>
        ContentTypeExtensions.ContainsKey(contentType);

    public ImageUploadTicket? CreateUploadTicket(Guid userId, string contentType, long size)
    {
        if (!IsConfigured || !IsAllowedContentType(contentType) ||
            size <= 0 || size > MaxUploadBytes)
            return null;

        var extension = ContentTypeExtensions[contentType];
        var objectKey = BuildObjectKey(userId, extension);
        var expiresAt = DateTimeOffset.UtcNow.AddSeconds(_options.PresignedUrlLifetimeSeconds);
        var uploadUrl = _client!.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = _options.BucketName,
            Key = objectKey,
            Verb = HttpVerb.PUT,
            ContentType = contentType,
            Expires = expiresAt.UtcDateTime,
            Protocol = Protocol.HTTPS,
        });

        return new ImageUploadTicket(
            uploadUrl,
            objectKey,
            contentType,
            MaxUploadBytes,
            expiresAt);
    }

    public ImageDownloadTicket? CreateDownloadTicket(string objectKey)
    {
        if (!IsConfigured || !IsValidObjectKey(objectKey))
            return null;

        var expiresAt = DateTimeOffset.UtcNow.AddSeconds(_options.DownloadUrlLifetimeSeconds);
        var downloadUrl = _client!.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = _options.BucketName,
            Key = objectKey,
            Verb = HttpVerb.GET,
            Expires = expiresAt.UtcDateTime,
            Protocol = Protocol.HTTPS,
        });

        return new ImageDownloadTicket(downloadUrl, expiresAt);
    }

    public async Task<string?> CompleteUploadAsync(
        Guid userId,
        string objectKey,
        string contentType,
        long size,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured || !IsOwnedObjectKey(objectKey, userId) ||
            !IsAllowedContentType(contentType) || size <= 0 || size > MaxUploadBytes)
            return null;

        try
        {
            var metadata = await _client!.GetObjectMetadataAsync(new GetObjectMetadataRequest
            {
                BucketName = _options.BucketName,
                Key = objectKey,
            }, cancellationToken);

            if (metadata.ContentLength != size ||
                !string.Equals(metadata.Headers.ContentType, contentType, StringComparison.OrdinalIgnoreCase))
                return null;

            return objectKey;
        }
        catch (AmazonS3Exception)
        {
            return null;
        }
    }

    public async Task<StoredImage?> OpenObjectAsync(
        string objectKey,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured || !IsValidObjectKey(objectKey))
            return null;

        try
        {
            var response = await _client!.GetObjectAsync(new GetObjectRequest
            {
                BucketName = _options.BucketName,
                Key = objectKey,
            }, cancellationToken);

            var contentType = response.Headers.ContentType ?? "";
            if (response.ContentLength <= 0 || response.ContentLength > MaxUploadBytes ||
                !IsAllowedContentType(contentType))
            {
                await response.ResponseStream.DisposeAsync();
                return null;
            }

            return new StoredImage(response.ResponseStream, contentType, response.ContentLength);
        }
        catch (AmazonS3Exception)
        {
            return null;
        }
    }

    public bool IsOwnedObjectKey(string objectKey, Guid userId) =>
        IsValidObjectKey(objectKey) &&
        objectKey.StartsWith($"{NormalizePrefix()}/{userId:D}/profile/", StringComparison.Ordinal);

    public void Dispose() => _client?.Dispose();

    private string BuildObjectKey(Guid userId, string extension) =>
        $"{NormalizePrefix()}/{userId:D}/profile/{Guid.NewGuid():N}.{extension}";

    private string NormalizePrefix() => _options.KeyPrefix.Trim('/');

    private bool IsValidObjectKey(string objectKey)
    {
        var prefix = $"{NormalizePrefix()}/";
        if (string.IsNullOrWhiteSpace(objectKey) ||
            !objectKey.StartsWith(prefix, StringComparison.Ordinal) ||
            objectKey.Contains("..", StringComparison.Ordinal))
            return false;

        var parts = objectKey[prefix.Length..].Split('/');
        if (parts.Length != 3 || parts[1] != "profile" ||
            !Guid.TryParseExact(parts[0], "D", out _))
            return false;

        var fileParts = parts[2].Split('.', 2);
        return fileParts.Length == 2 &&
            Guid.TryParseExact(fileParts[0], "N", out _) &&
            ContentTypeExtensions.Values.Contains(fileParts[1], StringComparer.OrdinalIgnoreCase);
    }
}
