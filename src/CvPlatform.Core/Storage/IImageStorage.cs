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

/// <summary>
/// The upload rules the server enforces. Exposed so clients can validate a file before spending a
/// round trip, instead of duplicating constants that silently drift from configuration.
/// </summary>
public sealed record ImageUploadLimits(
    long MaxBytes,
    IReadOnlyCollection<string> AllowedContentTypes)
{
    public bool Allows(string contentType) =>
        contentType is not null &&
        AllowedContentTypes.Contains(contentType, StringComparer.OrdinalIgnoreCase);

    public string AcceptAttribute => string.Join(',', AllowedContentTypes);
}

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
    IReadOnlyCollection<string> AllowedContentTypes { get; }
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
