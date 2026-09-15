using System.Net;

namespace CvPlatform.Infrastructure.Exports;

public interface IProfileImageFetcher
{
    Task<byte[]?> FetchAsync(string? imageUrl, CancellationToken ct = default);
}

public sealed class ProfileImageFetcher(HttpClient httpClient) : IProfileImageFetcher
{
    private const int MaxImageBytes = 5 * 1024 * 1024;

    public async Task<byte[]?> FetchAsync(string? imageUrl, CancellationToken ct = default)
    {
        if (!Uri.TryCreate(imageUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !IsAllowedHost(uri.Host))
            return null;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            using var response = await httpClient.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (response.StatusCode != HttpStatusCode.OK ||
                response.Content.Headers.ContentType?.MediaType is not { } mediaType ||
                !mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) ||
                response.Content.Headers.ContentLength > MaxImageBytes)
                return null;

            await using var source = await response.Content.ReadAsStreamAsync(ct);
            await using var destination = new MemoryStream();
            var buffer = new byte[81920];
            var total = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, ct)) > 0)
            {
                total += read;
                if (total > MaxImageBytes)
                    return null;
                await destination.WriteAsync(buffer.AsMemory(0, read), ct);
            }

            return destination.ToArray();
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return null;
        }
    }

    private static bool IsAllowedHost(string host) =>
        host.Equals("cloudinary.com", StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith(".cloudinary.com", StringComparison.OrdinalIgnoreCase);
}
