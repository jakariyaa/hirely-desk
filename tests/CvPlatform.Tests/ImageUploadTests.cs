using AwesomeAssertions;
using CvPlatform.Core.Storage;
using CvPlatform.Infrastructure.Storage;
using Microsoft.Extensions.Options;

namespace CvPlatform.Tests;

public class ImageUploadTests
{
    [Fact]
    public void Unconfigured_service_reports_not_configured()
    {
        var service = new CloudinaryImageService(
            Options.Create(new CloudinaryOptions()));
        service.IsConfigured.Should().BeFalse();
        service.ClientConfig.Should().BeNull();
    }

    [Fact]
    public void Missing_upload_configuration_is_not_available()
    {
        var service = new CloudinaryImageService(
            Options.Create(new CloudinaryOptions()));
        service.ClientConfig.Should().BeNull();
    }

    [Fact]
    public void Configured_widget_exposes_only_public_configuration()
    {
        var service = new CloudinaryImageService(
            Options.Create(new CloudinaryOptions
            {
                CloudName = "demo",
                UploadPreset = "unsigned-preset",
            }));
        service.IsConfigured.Should().BeTrue();
        service.ClientConfig.Should().Be(("demo", "unsigned-preset"));
        service.SignedClientConfig.Should().BeNull();
    }

    [Fact]
    public void Signed_configuration_exposes_only_the_api_key_to_the_client()
    {
        var service = new CloudinaryImageService(
            Options.Create(new CloudinaryOptions
            {
                CloudName = "demo",
                ApiKey = "public-key",
                ApiSecret = "server-secret",
            }));

        service.IsConfigured.Should().BeTrue();
        service.ClientConfig.Should().BeNull();
        service.SignedClientConfig.Should().Be(("demo", "public-key"));
    }

    [Fact]
    public void Signed_upload_signature_is_scoped_to_the_user_folder()
    {
        var userId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var signer = new CloudinaryUploadSigner(
            Options.Create(new CloudinaryOptions
            {
                CloudName = "demo",
                ApiKey = "public-key",
                ApiSecret = "server-secret",
                Folder = "cvplatform",
            }));

        var signature = signer.Create(userId);

        signature.Should().NotBeNull();
        signature!.ApiKey.Should().Be("public-key");
        signature.Folder.Should().Be($"cvplatform/{userId:D}/profile");
        signature.PublicId.Should().HaveLength(32);
        signature.Signature.Should().HaveLength(40);
        signature.Signature.Should().MatchRegex("^[0-9a-f]+$");
    }
}
