using System.Net;
using System.Net.Http.Headers;
using CvPlatform.Core.Storage;

namespace CvPlatform.Web.Storage;

public interface IImageUploadClient
{
    Task<bool> PutAsync(
        ImageUploadTicket ticket,
        Stream content,
        long size,
        IProgress<int>? progress,
        CancellationToken cancellationToken);
}

public sealed class ImageUploadClient(HttpClient http) : IImageUploadClient
{
    public async Task<bool> PutAsync(
        ImageUploadTicket ticket,
        Stream content,
        long size,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, ticket.UploadUrl);
        request.Content = new ProgressStreamContent(content, size, progress);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(ticket.ContentType);
        using var response = await http.SendAsync(request, cancellationToken);
        return response.IsSuccessStatusCode;
    }
}

public sealed class ProgressStreamContent(Stream content, long contentLength, IProgress<int>? progress)
    : HttpContent
{
    public const int BufferSize = 81_920;

    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
        SerializeToStreamAsync(stream, context, CancellationToken.None);

    protected override async Task SerializeToStreamAsync(
        Stream stream, TransportContext? context, CancellationToken cancellationToken)
    {
        var buffer = new byte[BufferSize];
        long written = 0;
        int read;
        while ((read = await content.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await stream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            written += read;
            if (contentLength > 0)
                progress?.Report((int)Math.Min(100, written * 100 / contentLength));
        }
    }

    protected override bool TryComputeLength(out long length)
    {
        length = contentLength;
        return true;
    }
}
