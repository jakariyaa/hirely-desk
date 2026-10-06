using AwesomeAssertions;
using CvPlatform.Infrastructure.Storage;
using CvPlatform.Web.Storage;
using Microsoft.Extensions.Options;

namespace CvPlatform.Tests;

public class ImageUploadTests
{
    [Fact]
    public void Unconfigured_storage_reports_not_configured()
    {
        var storage = new B2ImageStorage(Options.Create(new B2Options()));

        storage.IsConfigured.Should().BeFalse();
        storage.CreateUploadTicket(Guid.NewGuid(), "image/png", 100).Should().BeNull();
    }

    [Fact]
    public void Presigned_upload_is_scoped_to_the_user_and_content_type()
    {
        var userId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var storage = new B2ImageStorage(Options.Create(new B2Options
        {
            Region = "us-west-004",
            BucketName = "images",
            ApplicationKeyId = "application-key-id",
            ApplicationKey = "application-key",
        }));

        var ticket = storage.CreateUploadTicket(userId, "image/png", 100);

        ticket.Should().NotBeNull();
        ticket!.ObjectKey.Should().MatchRegex(
            $"^users/{userId:D}/profile/[a-f0-9]{{32}}\\.png$");
        ticket.ContentType.Should().Be("image/png");
        ticket.UploadUrl.Should().Contain("s3.us-west-004.backblazeb2.com");
        ticket.UploadUrl.Should().Contain("X-Amz-Signature=");
        ticket.UploadUrl.Should().Contain("X-Amz-SignedHeaders=content-type%3Bhost");
        storage.IsOwnedObjectKey(ticket.ObjectKey, userId).Should().BeTrue();
        storage.IsOwnedObjectKey(ticket.ObjectKey, Guid.NewGuid()).Should().BeFalse();

        var download = storage.CreateDownloadTicket(ticket.ObjectKey);
        download.Should().NotBeNull();
        download!.DownloadUrl.Should().Contain("X-Amz-Signature=");
        download.DownloadUrl.Should().Contain("s3.us-west-004.backblazeb2.com");
    }

    [Theory]
    [InlineData("users/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa/profile/bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb.jpg", true, true)]
    [InlineData("users/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa/profile/bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb.png", true, true)]
    [InlineData("users/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa/profile/bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb.webp", true, true)]
    [InlineData("users/cccccccc-cccc-cccc-cccc-cccccccccccc/profile/bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb.jpg", false, true)]
    [InlineData("users/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa/avatar/bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb.jpg", false, false)]
    [InlineData("users/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa/profile/not-a-guid.jpg", false, false)]
    [InlineData("users/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa/profile/bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb.gif", false, false)]
    [InlineData("users/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa/profile/../photo.jpg", false, false)]
    [InlineData("https://images.example.test/photo.jpg", false, false)]
    public void Object_keys_are_strictly_validated(string objectKey, bool expectedOwned, bool expectedValid)
    {
        var userId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var storage = new B2ImageStorage(Options.Create(new B2Options
        {
            Region = "us-west-004",
            BucketName = "images",
            ApplicationKeyId = "application-key-id",
            ApplicationKey = "application-key",
        }));

        storage.IsOwnedObjectKey(objectKey, userId).Should().Be(expectedOwned);
        (storage.CreateDownloadTicket(objectKey) is not null).Should().Be(expectedValid);
    }

    [Theory]
    [InlineData("image/gif")]
    [InlineData("text/plain")]
    public void Unsupported_content_types_are_rejected(string contentType)
    {
        var storage = new B2ImageStorage(Options.Create(new B2Options
        {
            Region = "us-west-004",
            BucketName = "images",
            ApplicationKeyId = "application-key-id",
            ApplicationKey = "application-key",
        }));

        storage.CreateUploadTicket(Guid.NewGuid(), contentType, 100).Should().BeNull();
    }

    [Fact]
    public async Task Progressing_content_streams_the_body_and_reports_the_bytes_written()
    {
        var payload = new byte[ProgressStreamContent.BufferSize + 100];
        Random.Shared.NextBytes(payload);
        var reports = new List<int>();

        using var content = new ProgressStreamContent(
            new MemoryStream(payload), payload.Length, new CollectingProgress(reports));

        using var target = new MemoryStream();
        await content.CopyToAsync(target);

        target.ToArray().Should().Equal(payload);
        content.Headers.ContentLength.Should().Be(payload.Length);
        reports.Should().ContainInOrder(99, 100);
    }

    private sealed class CollectingProgress(List<int> reports) : IProgress<int>
    {
        public void Report(int value) => reports.Add(value);
    }
}
