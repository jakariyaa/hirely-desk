using AwesomeAssertions;
using CvPlatform.Infrastructure.Storage;
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
}
