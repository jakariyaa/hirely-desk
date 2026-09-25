namespace CvPlatform.Core.Storage;

public sealed record ImageUploadTicket(
    string UploadUrl,
    string ObjectKey,
    string ContentType,
    long MaxBytes,
    DateTimeOffset ExpiresAt);

public sealed record ImageDownloadTicket(
    string DownloadUrl,
    DateTimeOffset ExpiresAt);

public sealed class StoredImage(Stream content, string contentType, long contentLength) : IAsyncDisposable
{
    public Stream Content { get; } = content;
    public string ContentType { get; } = contentType;
    public long ContentLength { get; } = contentLength;

    public ValueTask DisposeAsync() => Content.DisposeAsync();
}

public interface IImageStorage
{
    bool IsConfigured { get; }
    long MaxUploadBytes { get; }
    bool IsAllowedContentType(string contentType);
    ImageUploadTicket? CreateUploadTicket(Guid userId, string contentType, long size);
    ImageDownloadTicket? CreateDownloadTicket(string objectKey);
    Task<string?> CompleteUploadAsync(
        Guid userId,
        string objectKey,
        string contentType,
        long size,
        CancellationToken cancellationToken = default);
    Task<StoredImage?> OpenObjectAsync(
        string objectKey,
        CancellationToken cancellationToken = default);
    bool IsOwnedObjectKey(string objectKey, Guid userId);
}
