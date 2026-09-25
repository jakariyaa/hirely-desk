using CvPlatform.Core.Storage;

namespace CvPlatform.Infrastructure.Exports;

public interface IProfileImageFetcher
{
    Task<byte[]?> FetchAsync(string? objectKey, CancellationToken ct = default);
}

public sealed class ProfileImageFetcher(
    IImageStorage imageStorage) : IProfileImageFetcher
{
    private const int MaxImageBytes = 5 * 1024 * 1024;

    public async Task<byte[]?> FetchAsync(string? objectKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(objectKey))
            return null;

        try
        {
            await using var image = await imageStorage.OpenObjectAsync(objectKey, ct);
            if (image is null ||
                !image.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) ||
                image.ContentLength > MaxImageBytes)
                return null;

            await using var destination = new MemoryStream();
            var buffer = new byte[81920];
            var total = 0;
            int read;
            while ((read = await image.Content.ReadAsync(buffer, ct)) > 0)
            {
                total += read;
                if (total > MaxImageBytes)
                    return null;
                await destination.WriteAsync(buffer.AsMemory(0, read), ct);
            }

            return destination.ToArray();
        }
        catch (IOException)
        {
            return null;
        }
    }
}
