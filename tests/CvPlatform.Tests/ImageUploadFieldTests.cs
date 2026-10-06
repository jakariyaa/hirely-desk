using AwesomeAssertions;
using Bunit;
using CvPlatform.Application.Attributes;
using CvPlatform.Core.Enums;
using CvPlatform.Core.Storage;
using CvPlatform.Web.Components.Shared;
using CvPlatform.Web.ErrorHandling;
using CvPlatform.Web.Resources;
using CvPlatform.Web.Storage;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using MudBlazor.Services;
using System.Security.Claims;

namespace CvPlatform.Tests;

public class ImageUploadFieldTests : BunitContext, IAsyncLifetime
{
    private static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public ImageUploadFieldTests()
    {
        Services.AddMudServices();
        Services.AddCvErrorHandling();
        Services.AddSingleton<IStringLocalizer<SharedResource>, UploadLocalizer>();
        Services.AddSingleton<IImageStorage>(new StubImageStorage());
        Services.AddSingleton<IImageUploadClient>(new StubUploadClient());
        Services.AddSingleton<AuthenticationStateProvider>(new StubAuthStateProvider(UserId));
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void The_file_input_advertises_the_limits_the_server_enforces()
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
            .Add(p => p.Value, new AttributeValueDraft { ImageValueId = valueId, ImageObjectKey = "users/00000000-0000-0000-0000-000000000000/profile/abc.jpg" }));

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

        await cut.InvokeAsync(() => cut.Instance.OnFileSelected(Selected(new byte[] { 1, 2, 3 })));

        draft.ImageObjectKey.Should().Be(StubImageStorage.ObjectKey);
        // The stored id still points at the previous image until the save round-trips.
        draft.ImageValueId.Should().BeNull();
        changed.Should().Be(1);
    }

    [Fact]
    public async Task A_successful_upload_shows_the_preview_before_the_save_round_trips()
    {
        var cut = Render<ImageUploadField>(parameters => parameters
            .Add(p => p.Value, new AttributeValueDraft()));

        await cut.InvokeAsync(() => cut.Instance.OnFileSelected(Selected(new byte[] { 1, 2, 3 })));

        cut.Find(".cv-image-preview").Should().NotBeNull();
        cut.Find("img").GetAttribute("src").Should().Be("data:image/jpeg;base64,AQID");
    }

    [Fact]
    public async Task Replacing_the_draft_releases_the_pending_preview_for_the_stored_image()
    {
        var draft = new AttributeValueDraft();
        var cut = Render<ImageUploadField>(parameters => parameters.Add(p => p.Value, draft));

        await cut.InvokeAsync(() => cut.Instance.OnFileSelected(Selected(new byte[] { 1, 2, 3 })));

        // The parent swaps in the draft it got back from the server, which carries the stored id.
        var saved = Guid.NewGuid();
        cut.Render(parameters => parameters.Add(p => p.Value, new AttributeValueDraft
        {
            ImageValueId = saved,
            ImageObjectKey = StubImageStorage.ObjectKey,
        }));

        cut.Find("img").GetAttribute("src").Should().Be($"/api/profile-images/{saved:D}/content");
    }

    [Fact]
    public async Task Cancelling_an_upload_drops_the_preview_and_the_draft_untouched()
    {
        var client = new BlockingUploadClient();
        Services.AddSingleton<IImageUploadClient>(client);
        var draft = new AttributeValueDraft();
        var cut = Render<ImageUploadField>(parameters => parameters.Add(p => p.Value, draft));

        var upload = cut.InvokeAsync(() => cut.Instance.OnFileSelected(Selected(new byte[] { 1, 2, 3 })));
        await client.Started.Task;
        cut.FindAll("button").Single(b => b.TextContent.Contains("Cancel")).Click();
        await upload;

        draft.ImageObjectKey.Should().BeNull();
        cut.FindAll(".cv-image-preview").Should().BeEmpty();
        cut.FindAll("[role=alert]").Should().BeEmpty();
    }

    [Fact]
    public async Task A_transport_timeout_is_reported_instead_of_looking_like_a_cancellation()
    {
        Services.AddSingleton<IImageUploadClient>(new ThrowingUploadClient(
            new TaskCanceledException("The request timed out.")));
        var cut = Render<ImageUploadField>(parameters => parameters
            .Add(p => p.Value, new AttributeValueDraft()));

        await cut.InvokeAsync(() => cut.Instance.OnFileSelected(Selected(new byte[] { 1, 2, 3 })));

        cut.Find("[role=alert]").Should().NotBeNull();
        cut.FindAll(".cv-image-preview").Should().BeEmpty();
    }

    [Fact]
    public async Task A_failed_re_upload_leaves_the_previously_stored_image_on_screen()
    {
        var client = new ThrowingUploadClient(new HttpRequestException("connection reset"), afterFirst: true);
        Services.AddSingleton<IImageUploadClient>(client);
        var cut = Render<ImageUploadField>(parameters => parameters
            .Add(p => p.Value, new AttributeValueDraft()));

        await cut.InvokeAsync(() => cut.Instance.OnFileSelected(Selected(new byte[] { 1, 2, 3 })));

        // The first upload saved, so the parent swaps in the draft the server returned.
        var saved = Guid.NewGuid();
        cut.Render(parameters => parameters.Add(p => p.Value, new AttributeValueDraft
        {
            ImageValueId = saved,
            ImageObjectKey = StubImageStorage.ObjectKey,
        }));

        await cut.InvokeAsync(() => cut.Instance.OnFileSelected(Selected(new byte[] { 4, 5, 6 })));

        client.Calls.Should().Be(2);
        cut.Find("[role=alert]").Should().NotBeNull();
        cut.Find("img").GetAttribute("src").Should().Be($"/api/profile-images/{saved:D}/content");
    }

    [Fact]
    public async Task Every_attempt_rerenders_the_input_so_the_same_file_can_be_chosen_again()
    {
        Services.AddSingleton<IImageUploadClient>(new ThrowingUploadClient(
            new HttpRequestException("connection reset")));
        var cut = Render<ImageUploadField>(parameters => parameters
            .Add(p => p.Value, new AttributeValueDraft()));

        await cut.InvokeAsync(() => cut.Instance.OnFileSelected(Selected(new byte[] { 1 })));
        var first = ElementReferenceOf(cut);
        await cut.InvokeAsync(() => cut.Instance.OnFileSelected(Selected(new byte[] { 1 })));

        // A native file input keeps its value, so the element has to be replaced for the second
        // selection of the same file to raise change at all.
        ElementReferenceOf(cut).Should().NotBe(first);

        static string ElementReferenceOf(IRenderedComponent<ImageUploadField> cut) =>
            cut.Find("input[type=file]").GetAttribute("blazor:elementReference") ?? "(none)";
    }

    [Fact]
    public async Task The_uploaded_bytes_reach_storage_and_the_progress_reports_are_focused_to_the_ui()
    {
        var client = (StubUploadClient)Services.GetRequiredService<IImageUploadClient>();
        var cut = Render<ImageUploadField>(parameters => parameters
            .Add(p => p.Value, new AttributeValueDraft()));

        await cut.InvokeAsync(() => cut.Instance.OnFileSelected(Selected(new byte[] { 7, 7, 7, 7 })));

        client.Uploaded.Should().BeEquivalentTo(new byte[] { 7, 7, 7, 7 });
        client.ContentType.Should().Be("image/jpeg");
        client.Progressed.Should().BeTrue();
    }

    [Fact]
    public async Task An_unsupported_type_is_rejected_without_touching_storage()
    {
        var client = (StubUploadClient)Services.GetRequiredService<IImageUploadClient>();
        var cut = Render<ImageUploadField>(parameters => parameters
            .Add(p => p.Value, new AttributeValueDraft()));

        await cut.InvokeAsync(() => cut.Instance.OnFileSelected(
            Selected([9], contentType: "application/pdf")));

        cut.Find("[role=alert]").TextContent.Should()
            .Contain("Only JPEG, PNG, and WebP images up to 5 MB are supported.");
        client.Uploaded.Should().BeNull();
    }

    [Fact]
    public async Task An_oversized_file_is_rejected_with_an_actionable_message()
    {
        var storage = (StubImageStorage)Services.GetRequiredService<IImageStorage>();
        var cut = Render<ImageUploadField>(parameters => parameters
            .Add(p => p.Value, new AttributeValueDraft()));

        await cut.InvokeAsync(() => cut.Instance.OnFileSelected(
            Selected([9], size: storage.MaxUploadBytes + 1)));

        cut.Find("[role=alert]").TextContent.Should().Contain("This image is too large.");
    }

    [Fact]
    public async Task A_rejected_put_drops_the_preview_and_reports_the_storage_failure()
    {
        Services.AddSingleton<IImageUploadClient>(new StubUploadClient { Succeeds = false });
        var draft = new AttributeValueDraft();
        var cut = Render<ImageUploadField>(parameters => parameters.Add(p => p.Value, draft));

        await cut.InvokeAsync(() => cut.Instance.OnFileSelected(Selected(new byte[] { 1 })));

        draft.ImageObjectKey.Should().BeNull();
        cut.FindAll(".cv-image-preview").Should().BeEmpty();
        cut.Markup.Should().Contain("The storage service rejected the upload.");
    }

    [Fact]
    public async Task An_unverified_upload_reports_the_verification_failure()
    {
        Services.AddSingleton<IImageStorage>(new StubImageStorage { Verifies = false });
        var draft = new AttributeValueDraft();
        var cut = Render<ImageUploadField>(parameters => parameters.Add(p => p.Value, draft));

        await cut.InvokeAsync(() => cut.Instance.OnFileSelected(Selected(new byte[] { 1 })));

        draft.ImageObjectKey.Should().BeNull();
        cut.Markup.Should().Contain("The upload could not be verified.");
    }

    [Fact]
    public async Task A_ticket_the_storage_will_not_issue_stops_before_any_bytes_move()
    {
        Services.AddSingleton<IImageStorage>(new StubImageStorage { IssuesTickets = false });
        var cut = Render<ImageUploadField>(parameters => parameters
            .Add(p => p.Value, new AttributeValueDraft()));

        await cut.InvokeAsync(() => cut.Instance.OnFileSelected(Selected(new byte[] { 1 })));

        cut.Markup.Should().Contain("The upload could not be started.");
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

    // MudBlazor registers IAsyncDisposable-only services, so the container must be disposed async.
    async Task IAsyncLifetime.DisposeAsync() => await base.DisposeAsync();

    private static InputFileChangeEventArgs Selected(byte[] content, long? size = null, string contentType = "image/jpeg") =>
        new([new StubBrowserFile(content, size ?? content.Length, contentType)]);

    private sealed class StubAuthStateProvider(Guid userId) : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
            Task.FromResult(new AuthenticationState(
                new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "TestAuth"))));
    }

    private sealed class StubBrowserFile(byte[] content, long size, string contentType) : IBrowserFile
    {
        public long Size { get; } = size;

        public string ContentType { get; } = contentType;

        public string Name => "photo.jpg";

        public DateTimeOffset LastModified => DateTimeOffset.UnixEpoch;

        public Stream OpenReadStream(long maxAllowedSize = 512_000, CancellationToken cancellationToken = default) =>
            new MemoryStream(content);
    }

    private sealed class StubImageStorage : IImageStorage
    {
        public const string ObjectKey = "users/x/profile/a.jpg";

        public bool IsConfigured { get; init; } = true;

        public bool IssuesTickets { get; init; } = true;

        public bool Verifies { get; init; } = true;

        public long MaxUploadBytes => 5 * 1024 * 1024;

        public IReadOnlyCollection<string> AllowedContentTypes =>
            ["image/jpeg", "image/png", "image/webp"];

        public bool IsAllowedContentType(string contentType) =>
            AllowedContentTypes.Contains(contentType, StringComparer.OrdinalIgnoreCase);

        public ImageUploadTicket? CreateUploadTicket(Guid userId, string contentType, long size) =>
            IssuesTickets
                ? new ImageUploadTicket(
                    "https://storage.example/put", ObjectKey, contentType, MaxUploadBytes,
                    DateTimeOffset.UtcNow.AddMinutes(5))
                : null;

        public ImageDownloadTicket? CreateDownloadTicket(string objectKey) => null;

        public Task<string?> CompleteUploadAsync(
            Guid userId, string objectKey, string contentType, long size,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Verifies ? objectKey : null);

        public Task<StoredImage?> OpenObjectAsync(
            string objectKey, CancellationToken cancellationToken = default) =>
            Task.FromResult<StoredImage?>(null);

        public bool IsOwnedObjectKey(string objectKey, Guid userId) => false;
    }

    private sealed class StubUploadClient : IImageUploadClient
    {
        public bool Succeeds { get; init; } = true;

        public byte[]? Uploaded { get; private set; }

        public string? ContentType { get; private set; }

        public bool Progressed { get; private set; }

        public async Task<bool> PutAsync(
            ImageUploadTicket ticket, Stream content, long size, IProgress<int>? progress,
            CancellationToken cancellationToken)
        {
            using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, cancellationToken);
            Uploaded = buffer.ToArray();
            ContentType = ticket.ContentType;
            progress?.Report(50);
            progress?.Report(100);
            Progressed = progress is not null;
            return Succeeds;
        }
    }

    /// <summary>Hangs until cancelled, so the Cancel button can be exercised.</summary>
    private sealed class BlockingUploadClient : IImageUploadClient
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<bool> PutAsync(
            ImageUploadTicket ticket, Stream content, long size, IProgress<int>? progress,
            CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return true;
        }
    }

    /// <summary>Fails the way the network does, optionally only after the first attempt succeeded.</summary>
    private sealed class ThrowingUploadClient(Exception failure, bool afterFirst = false) : IImageUploadClient
    {
        public int Calls { get; private set; }

        public async Task<bool> PutAsync(
            ImageUploadTicket ticket, Stream content, long size, IProgress<int>? progress,
            CancellationToken cancellationToken)
        {
            Calls++;
            using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, cancellationToken);
            if (afterFirst && Calls == 1)
                return true;
            throw failure;
        }
    }

    private sealed class UploadLocalizer : StubStringLocalizer
    {
        protected override IReadOnlyDictionary<string, string> Catalog { get; } =
            new Dictionary<string, string>
            {
                ["ImageAlt"] = "Profile image",
                ["ImageChooseHere"] = "Click to choose an image file",
                ["ImageUploading"] = "Uploading the image...",
                ["ImageProgress"] = "Uploading: {0}%",
                ["ImageRemove"] = "Remove image",
                ["ImageFormatHint"] = "{0}. Up to {1}.",
                ["ImageStorageUnavailable"] = "Image uploads are not available right now.",
                ["ImageUploadTypeInvalid"] =
                    "Only JPEG, PNG, and WebP images up to 5 MB are supported.",
                ["ImageUploadTooLarge"] = "This image is too large. Try a smaller file.",
                ["ImageUploadTicketFailed"] = "The upload could not be started.",
                ["ImageUploadRejected"] = "The storage service rejected the upload.",
                ["ImageUploadVerifyFailed"] = "The upload could not be verified.",
                ["ErrorUnauthorized"] = "You are not signed in.",
                ["Cancel"] = "Cancel",
            };
    }
}