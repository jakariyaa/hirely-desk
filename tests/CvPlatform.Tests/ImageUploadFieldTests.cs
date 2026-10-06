using AwesomeAssertions;
using Bunit;
using CvPlatform.Application.Attributes;
using CvPlatform.Core.Enums;
using CvPlatform.Core.Storage;
using CvPlatform.Web.Components.Shared;
using CvPlatform.Web.ErrorHandling;
using CvPlatform.Web.Resources;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using MudBlazor.Services;
using System.Globalization;

namespace CvPlatform.Tests;

public class ImageUploadFieldTests : BunitContext, IAsyncLifetime
{
    public ImageUploadFieldTests()
    {
        Services.AddMudServices();
        Services.AddCvErrorHandling();
        Services.AddSingleton<IStringLocalizer<SharedResource>, UploadLocalizer>();
        Services.AddSingleton<IImageStorage>(new StubImageStorage());
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void The_drop_zone_advertises_the_limits_the_server_enforces()
    {
        var cut = Render<ImageUploadField>(parameters => parameters
            .Add(p => p.Value, new AttributeValueDraft()));

        var input = cut.Find("input[type=file]");
        input.GetAttribute("accept").Should().Be("image/jpeg,image/png,image/webp");
        input.HasAttribute("disabled").Should().BeFalse();
        cut.Markup.Should().Contain("image/jpeg, image/png, image/webp");
        cut.Markup.Should().Contain("5 MB");
    }

    [Fact]
    public void The_file_input_is_label_associated_rather_than_hidden_from_assistive_technology()
    {
        var cut = Render<ImageUploadField>(parameters => parameters
            .Add(p => p.Value, new AttributeValueDraft()));

        var input = cut.Find("input[type=file]");
        var label = cut.Find($"label[for='{input.Id}']");

        input.ClassList.Should().Contain("cv-image-sr-only");
        input.HasAttribute("style").Should().BeFalse();
        label.Should().NotBeNull();
    }

    [Fact]
    public void A_stored_image_is_previewed_from_the_auth_gated_content_endpoint()
    {
        var valueId = Guid.NewGuid();

        var cut = Render<ImageUploadField>(parameters => parameters
            .Add(p => p.Value, new AttributeValueDraft { ImageValueId = valueId }));

        cut.Find(".cv-image-preview").Should().NotBeNull();
        cut.Find("img").GetAttribute("src").Should().Be($"/api/profile-images/{valueId:D}/content");
        cut.Find("img").GetAttribute("alt").Should().Be("Profile image");
    }

    [Fact]
    public void No_preview_and_no_remove_action_before_an_image_exists()
    {
        var cut = Render<ImageUploadField>(parameters => parameters
            .Add(p => p.Value, new AttributeValueDraft()));

        cut.FindAll(".cv-image-preview").Should().BeEmpty();
        cut.FindAll("button").Should().BeEmpty();
    }

    [Fact]
    public void Disabling_the_field_disables_the_native_input()
    {
        var cut = Render<ImageUploadField>(parameters => parameters
            .Add(p => p.Value, new AttributeValueDraft())
            .Add(p => p.Disabled, true));

        cut.Find("input[type=file]").HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void An_unconfigured_storage_block_disables_uploads_and_explains_why()
    {
        Services.AddSingleton<IImageStorage>(new StubImageStorage { IsConfigured = false });

        var cut = Render<ImageUploadField>(parameters => parameters
            .Add(p => p.Value, new AttributeValueDraft()));

        cut.Find("input[type=file]").HasAttribute("disabled").Should().BeTrue();
        cut.Markup.Should().Contain("Image uploads are not available right now.");
    }

    [Fact]
    public async Task A_successful_upload_publishes_the_object_key_and_asks_for_a_save()
    {
        var draft = new AttributeValueDraft { ImageValueId = Guid.NewGuid() };
        var changed = 0;
        var cut = Render<ImageUploadField>(parameters => parameters
            .Add(p => p.Value, draft)
            .Add(p => p.OnChanged, () => changed++));

        await cut.InvokeAsync(() => cut.Instance.ReportSucceededAsync("users/x/profile/a.jpg", "blob:preview"));

        draft.ImageObjectKey.Should().Be("users/x/profile/a.jpg");
        // The stored id still points at the previous image until the save round-trips.
        draft.ImageValueId.Should().BeNull();
        changed.Should().Be(1);
    }

    [Fact]
    public async Task A_successful_upload_shows_the_local_preview_before_the_save_round_trips()
    {
        var cut = Render<ImageUploadField>(parameters => parameters
            .Add(p => p.Value, new AttributeValueDraft()));

        await cut.InvokeAsync(() => cut.Instance.ReportSucceededAsync("users/x/profile/a.jpg", "blob:preview"));

        cut.Find(".cv-image-preview").Should().NotBeNull();
        cut.Find("img").GetAttribute("src").Should().Be("blob:preview");
    }

    [Fact]
    public async Task A_failure_code_is_shown_as_localized_text_without_the_javascript_detail()
    {
        var cut = Render<ImageUploadField>(parameters => parameters
            .Add(p => p.Value, new AttributeValueDraft()));

        await cut.InvokeAsync(() => cut.Instance.ReportFailedAsync("too_large_to_downscale"));

        cut.Find("[role=alert]").TextContent.Should()
            .Contain("This image is too large even after scaling it down.");
        cut.Markup.Should().NotContain("too_large_to_downscale");
    }

    [Fact]
    public async Task A_failed_upload_drops_the_pending_preview()
    {
        var draft = new AttributeValueDraft();
        var cut = Render<ImageUploadField>(parameters => parameters.Add(p => p.Value, draft));

        await cut.InvokeAsync(() => cut.Instance.ReportSucceededAsync("users/x/profile/a.jpg", "blob:preview"));
        await cut.InvokeAsync(() => cut.Instance.ReportFailedAsync("network"));

        cut.FindAll(".cv-image-preview").Should().BeEmpty();
        cut.Markup.Should().Contain("The connection was lost.");
    }

    [Theory]
    [InlineData(-5)]
    [InlineData(0)]
    [InlineData(42)]
    [InlineData(140)]
    public async Task Progress_is_clamped_to_a_percentage_reported_by_a_live_region(int percent)
    {
        var cut = Render<ImageUploadField>(parameters => parameters
            .Add(p => p.Value, new AttributeValueDraft()));

        await cut.InvokeAsync(() => cut.Instance.ReportProgressAsync(percent));

        var expected = Math.Clamp(percent, 0, 100);
        cut.Markup.Should().Contain($"Uploading: {expected}%");
        cut.FindAll("button").Should().ContainSingle();
    }

    [Fact]
    public void Removing_the_image_clears_the_draft_and_asks_for_a_save()
    {
        var draft = new AttributeValueDraft
        {
            ImageValueId = Guid.NewGuid(),
            ImageObjectKey = "users/x/profile/a.jpg",
        };
        var changed = 0;
        var cut = Render<ImageUploadField>(parameters => parameters
            .Add(p => p.Value, draft)
            .Add(p => p.OnChanged, () => changed++));

        cut.FindAll("button").Single(b => b.TextContent.Contains("Remove image")).Click();

        draft.ImageValueId.Should().BeNull();
        draft.ImageObjectKey.Should().BeNull();
        changed.Should().Be(1);
        cut.FindAll(".cv-image-preview").Should().BeEmpty();
    }

    [Fact]
    public void The_server_limits_are_handed_to_the_upload_module_instead_of_being_duplicated()
    {
        Render<ImageUploadField>(parameters => parameters
            .Add(p => p.Value, new AttributeValueDraft()));

        var invocation = JSInterop.Invocations["createImageUploader"].Single();
        invocation.Arguments.Should().HaveCount(7);
        invocation.Arguments[5].Should().BeEquivalentTo(new
        {
            MaxBytes = 5L * 1024 * 1024,
            MaxEdge = 1600,
            AllowedContentTypes = new[] { "image/jpeg", "image/png", "image/webp" },
        });
    }

    [Fact]
    public async Task Dragging_toggles_the_visual_and_aria_affordance()
    {
        var cut = Render<ImageUploadField>(parameters => parameters
            .Add(p => p.Value, new AttributeValueDraft()));

        await cut.InvokeAsync(() => cut.Instance.ReportDragStateAsync(true));

        cut.Find("label").ClassList.Should().Contain("cv-image-dropzone-dragging");
    }

    [Fact]
    public async Task Releasing_the_field_tears_the_module_down()
    {
        var cut = Render<ImageUploadField>(parameters => parameters
            .Add(p => p.Value, new AttributeValueDraft()));

        await cut.Instance.DisposeAsync();

        JSInterop.VerifyInvoke("releaseUploader");
    }

    [Fact]
    public void AttributeValueEditor_delegates_image_attributes_to_the_upload_field()
    {
        var definition = new AttributeDefinitionDto(
            Guid.NewGuid(), "Me.Photo", AttributeDataType.Image, true, [], null);

        var cut = Render<AttributeValueEditor>(parameters => parameters
            .Add(p => p.Definition, definition)
            .Add(p => p.Value, new AttributeValueDraft()));

        cut.FindComponent<ImageUploadField>().Should().NotBeNull();
    }

    [Fact]
    public void Losing_focus_reports_the_change_so_autosave_can_start()
    {
        var definition = new AttributeDefinitionDto(
            Guid.NewGuid(), "Me.Name", AttributeDataType.String, true, [], null);
        var changed = 0;
        var cut = Render<AttributeValueEditor>(parameters => parameters
            .Add(p => p.Definition, definition)
            .Add(p => p.Value, new AttributeValueDraft())
            .Add(p => p.OnChanged, () => changed++));

        cut.Find("input").TriggerEvent("onfocusout", new FocusEventArgs());

        changed.Should().Be(1);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    async Task IAsyncLifetime.DisposeAsync() => await base.DisposeAsync();

    private sealed class StubImageStorage : IImageStorage
    {
        public bool IsConfigured { get; init; } = true;

        public long MaxUploadBytes => 5 * 1024 * 1024;

        public IReadOnlyCollection<string> AllowedContentTypes =>
            ["image/jpeg", "image/png", "image/webp"];

        public bool IsAllowedContentType(string contentType) => true;

        public ImageUploadTicket? CreateUploadTicket(Guid userId, string contentType, long size) => null;

        public ImageDownloadTicket? CreateDownloadTicket(string objectKey) => null;

        public Task<string?> CompleteUploadAsync(
            Guid userId, string objectKey, string contentType, long size,
            CancellationToken cancellationToken = default) => Task.FromResult<string?>(objectKey);

        public Task<StoredImage?> OpenObjectAsync(
            string objectKey, CancellationToken cancellationToken = default) =>
            Task.FromResult<StoredImage?>(null);

        public bool IsOwnedObjectKey(string objectKey, Guid userId) => false;
    }

    private sealed class UploadLocalizer : StubStringLocalizer
    {
        protected override IReadOnlyDictionary<string, string> Catalog { get; } =
            new Dictionary<string, string>
            {
                ["ImageAlt"] = "Profile image",
                ["ImageDropHere"] = "Drop an image here or click to choose a file",
                ["ImageUploading"] = "Uploading the image...",
                ["ImageProgress"] = "Uploading: {0}%",
                ["ImageRemove"] = "Remove image",
                ["ImageFormatHint"] = "{0}. Up to {1}; larger photos are scaled down automatically.",
                ["ImageStorageUnavailable"] = "Image uploads are not available right now.",
                ["ImageUploadTooLargeToDownscale"] =
                    "This image is too large even after scaling it down. Try a smaller file.",
                ["ImageUploadDownscaleFailed"] =
                    "This image could not be processed. Try a different file.",
                ["ErrorNetworkUnavailable"] =
                    "The connection was lost. Check your network and try again.",
                ["Cancel"] = "Cancel",
            };
    }
}
