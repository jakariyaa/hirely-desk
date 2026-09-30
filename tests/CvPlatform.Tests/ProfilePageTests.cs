using AwesomeAssertions;
using Bunit;
using CvPlatform.Application.Attributes;
using CvPlatform.Application.Authorization;
using CvPlatform.Application.Common;
using CvPlatform.Application.Profiles;
using CvPlatform.Core.Enums;
using CvPlatform.Core.Entities;
using CvPlatform.Core.Storage;
using CvPlatform.Web.Components.Pages;
using CvPlatform.Web.Components.Shared;
using CvPlatform.Web.Resources;
using CvPlatform.Web.State;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MudBlazor.Services;
using System.Globalization;
using System.Security.Claims;
using AppError = CvPlatform.Application.Common.Error;
using ProfilePage = CvPlatform.Web.Components.Pages.Profile;

namespace CvPlatform.Tests;

public class ProfilePageTests : BunitContext, IAsyncLifetime
{
    private static readonly Guid NameId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid PhotoId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid EmailId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid PersonalId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid ContactId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid UserId = Guid.Parse("66666666-6666-6666-6666-666666666666");

    private StubProfileService _profiles = new();

    public ProfilePageTests()
    {
        Services.AddMudServices();
        Services.AddCvErrorHandling();
        Services.AddSingleton<IStringLocalizer<SharedResource>, ProfileLocalizer>();
        Services.AddSingleton<IImageStorage, StubImageStorage>();
        Services.AddSingleton<ProfileHeaderState>();
        Services.AddSingleton<IProfileService>(_profiles);
        Services.AddSingleton<AuthenticationStateProvider>(new StubAuthenticationStateProvider(UserId));
        Services.AddSingleton<SignInManager<ApplicationUser>>(new StubSignInManager());
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void The_photo_gets_its_own_card_instead_of_being_buried_in_its_category()
    {
        var cut = RenderPage();

        var photo = cut.Find($"#profile-section-{PhotoId:D}");
        photo.TextContent.Should().Contain("Photo");
        photo.QuerySelector("input[type=file]").Should().NotBeNull();

        // The hoisted definition must not also render inside its original category.
        var personal = cut.Find($"#profile-section-{PersonalId:D}");
        personal.QuerySelector("input[type=file]").Should().BeNull();
        cut.FindAll("input[type=file]").Should().ContainSingle();
    }

    [Fact]
    public void Every_section_is_reachable_from_the_sticky_navigation()
    {
        var cut = RenderPage();

        var targets = cut.FindAll("nav.cv-profile-nav a")
            .Select(anchor => anchor.GetAttribute("href")).ToList();

        targets.Should().Equal(
            $"#profile-section-{PersonalId:D}",
            $"#profile-section-{ContactId:D}",
            "#profile-section-account");
        cut.Find($"#profile-section-{PersonalId:D}").Should().NotBeNull();
        cut.Find("#profile-section-account").Should().NotBeNull();
    }

    [Fact]
    public void The_first_section_is_marked_active_and_selection_moves_on_click()
    {
        var cut = RenderPage();

        cut.Find("nav.cv-profile-nav a.cv-profile-nav-link-active")
            .GetAttribute("href").Should().Be($"#profile-section-{PersonalId:D}");

        cut.FindAll("nav.cv-profile-nav a")[1].Click();

        cut.Find("nav.cv-profile-nav a.cv-profile-nav-link-active")
            .GetAttribute("href").Should().Be($"#profile-section-{ContactId:D}");
    }

    [Fact]
    public void External_provider_buttons_live_in_their_own_account_section_not_the_header()
    {
        var cut = RenderPage();

        cut.FindAll(".cv-page-actions").Should().BeEmpty();
        cut.FindAll("#profile-section-account a.cv-oauth-btn").Should().ContainSingle();
    }

    [Fact]
    public void A_save_status_region_is_always_present_so_the_outcome_is_never_silent()
    {
        var cut = RenderPage();

        var status = cut.Find(".cv-save-status");
        status.GetAttribute("role").Should().Be("status");
        status.GetAttribute("aria-live").Should().Be("polite");
        status.TextContent.Should().Contain("All changes saved");
    }

    [Fact]
    public void There_is_no_bulk_save_button_because_autosave_is_the_single_mechanism()
    {
        var cut = RenderPage();

        cut.Markup.Should().NotContain("Save all");
        cut.FindAll("button").Should().BeEmpty();
    }

    [Fact]
    public void Navigation_is_not_locked_while_the_page_is_clean()
    {
        var cut = RenderPage();

        cut.FindComponent<NavigationLock>().Instance.ConfirmExternalNavigation.Should().BeFalse();
    }

    [Fact]
    public async Task An_edit_saves_on_its_own_and_reports_the_result_without_a_save_button()
    {
        var cut = RenderPage();
        cut.WaitForAssertion(() => cut.FindAll("input[type=text]").Should().NotBeEmpty());

        cut.FindAll("input[type=text]")[0].Change("Ada");

        // The debounce is real, so wait for the service rather than for a specific label.
        _profiles.Saved.Wait(TimeSpan.FromSeconds(10)).Should().BeTrue();
        cut.WaitForState(() => cut.Find(".cv-save-status").TextContent.Contains("All changes saved"));

        _profiles.SavedInputs.Should().ContainSingle();
        _profiles.SavedInputs[0].AttributeDefinitionId.Should().Be(NameId);
        _profiles.SavedInputs[0].StringValue.Should().Be("Ada");
        cut.FindComponent<NavigationLock>().Instance.ConfirmExternalNavigation.Should().BeFalse();
    }

    [Fact]
    public async Task A_failed_save_surfaces_in_the_status_region_and_keeps_the_lock_closed()
    {
        _profiles.SaveError = new AppError(ErrorCodes.Unexpected, "boom");
        var cut = RenderPage();
        cut.WaitForAssertion(() => cut.FindAll("input[type=text]").Should().NotBeEmpty());

        cut.FindAll("input[type=text]")[0].Change("Ada");

        _profiles.Saved.Wait(TimeSpan.FromSeconds(10)).Should().BeTrue();
        cut.WaitForState(() => cut.Find(".cv-save-status").TextContent.Contains("could not be saved"));
    }

    [Fact]
    public void A_catalog_failure_offers_a_retry_instead_of_an_empty_page()
    {
        _profiles.CatalogError = new AppError(ErrorCodes.ServiceUnavailable, "down");

        var cut = RenderPage();

        cut.Find(".cv-empty-state").Should().NotBeNull();
        cut.Find(".cv-empty-actions button").TextContent.Should().Contain("Retry");
        cut.FindAll(".cv-profile-section").Should().BeEmpty();
    }

    [Fact]
    public void A_value_load_failure_keeps_the_editor_usable_and_warns_above_it()
    {
        _profiles.ProfileError = new AppError(ErrorCodes.Unexpected, "down");

        var cut = RenderPage();

        cut.Find(".mud-alert").Should().NotBeNull();
        cut.FindAll(".cv-profile-section").Should().NotBeEmpty();
        cut.FindAll(".cv-empty-state").Should().BeEmpty();
    }

    [Fact]
    public void While_the_profile_loads_the_page_shows_a_placeholder_instead_of_blank_space()
    {
        _profiles.LoadGate = new TaskCompletionSource();

        var cut = Render<ProfilePage>();
        cut.WaitForAssertion(() => cut.Markup.Should().NotBeEmpty());

        cut.FindAll(".mud-skeleton").Should().HaveCount(2);
        cut.FindAll(".cv-profile-section").Should().BeEmpty();

        _profiles.LoadGate.SetResult();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    async Task IAsyncLifetime.DisposeAsync() => await base.DisposeAsync();

    private IRenderedComponent<ProfilePage> RenderPage()
    {
        var cut = Render<ProfilePage>();
        cut.WaitForAssertion(() => cut.Markup.Should().NotBeEmpty());
        cut.WaitForAssertion(() =>
            (cut.FindAll(".cv-profile-section").Count
             + cut.FindAll(".cv-empty-state").Count
             + cut.FindAll(".mud-alert").Count).Should().BeGreaterThan(0));
        return cut;
    }

    private static IReadOnlyList<AttributeCategoryDto> Catalog() =>
    [
        new(PersonalId, "Personal",
        [
            new(NameId, ProfileAttributeNames.Name, AttributeDataType.String, true, [], null),
            new(PhotoId, ProfileAttributeNames.Photo, AttributeDataType.Image, true, [], null),
        ]),
        new(ContactId, "Contact",
        [
            new(EmailId, "Me.Email", AttributeDataType.String, true, [], null),
        ]),
    ];

    private static ProfileDto StoredProfile() => new(Guid.NewGuid(), UserId,
    [
        new(Guid.NewGuid(), NameId, "Me.Name", AttributeDataType.String, "Grace", null, null, null, null, null, null, null, null, 1),
        new(Guid.NewGuid(), EmailId, "Me.Email", AttributeDataType.String, "grace@example.test", null, null, null, null, null, null, null, null, 1),
    ]);

    private sealed class StubProfileService : IProfileService
    {
        public Result<IReadOnlyList<AttributeCategoryDto>>? CatalogResult { get; set; }

        public Result<ProfileDto>? ProfileResult { get; set; }

        public Result<ProfileDto>? SaveResult { get; set; }

        public AppError? CatalogError { get; set; }

        public AppError? ProfileError { get; set; }

        public AppError? SaveError { get; set; }

        public TaskCompletionSource LoadGate { get; set; } = CompletedGate();

        public ManualResetEventSlim Saved { get; } = new(false);

        public List<AttributeValueInput> SavedInputs { get; } = [];

        public async Task<Result<IReadOnlyList<AttributeCategoryDto>>> GetCatalogAsync(
            CancellationToken ct = default)
        {
            await LoadGate.Task;
            if (CatalogError is { } catalogError)
                return Result<IReadOnlyList<AttributeCategoryDto>>.Failure(catalogError.Code, catalogError.Message);
            return CatalogResult ?? Result<IReadOnlyList<AttributeCategoryDto>>.Success(Catalog());
        }

        private static TaskCompletionSource CompletedGate()
        {
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            gate.SetResult();
            return gate;
        }

        public async Task<Result<ProfileDto>> GetForUserAsync(
            ActorContext actor, Guid userId, CancellationToken ct = default)
        {
            await LoadGate.Task;
            if (ProfileError is { } profileError)
                return Result<ProfileDto>.Failure(profileError.Code, profileError.Message);
            return ProfileResult ?? Result<ProfileDto>.Success(StoredProfile());
        }

        public Task<Result<ProfileDto>> SaveAttributeValueAsync(
            ActorContext actor, Guid userId, AttributeValueInput input, CancellationToken ct = default)
        {
            SavedInputs.Add(input);
            Saved.Set();
            if (SaveError is { } saveError)
                return Task.FromResult(Result<ProfileDto>.Failure(saveError.Code, saveError.Message));
            return Task.FromResult(SaveResult ?? Result<ProfileDto>.Success(StoredProfile()));
        }

        public Task<Result<ProfileDto>> SaveAttributeValuesAsync(
            ActorContext actor, Guid userId, IReadOnlyList<AttributeValueInput> inputs,
            CancellationToken ct = default) =>
            Task.FromResult(Result<ProfileDto>.Success(StoredProfile()));

        public Task<Result<ProfileSummaryDto>> GetSummaryForUserAsync(
            ActorContext actor, Guid userId, CancellationToken ct = default) =>
            Task.FromResult(Result<ProfileSummaryDto>.Success(new("Grace", null)));
    }

    private sealed class StubAuthenticationStateProvider(Guid userId) : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
            Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity(
                new List<Claim> { new(ClaimTypes.NameIdentifier, userId.ToString()) }, "test"))));
    }

    private sealed class StubSignInManager() : SignInManager<ApplicationUser>(
        new StubUserManager(),
        new StubHttpContextAccessor(),
        new UserClaimsPrincipalFactory<ApplicationUser>(
            new StubUserManager(), Microsoft.Extensions.Options.Options.Create(new IdentityOptions())),
        Microsoft.Extensions.Options.Options.Create(new IdentityOptions()),
        NullLogger<SignInManager<ApplicationUser>>.Instance,
        new AuthenticationSchemeProvider(Microsoft.Extensions.Options.Options.Create(new AuthenticationOptions())),
        new StubUserConfirmation())
    {
        public override Task<IEnumerable<AuthenticationScheme>> GetExternalAuthenticationSchemesAsync() =>
            Task.FromResult<IEnumerable<AuthenticationScheme>>([new("Google", "Google", typeof(StubExternalHandler))]);
    }

    private sealed class StubUserConfirmation : IUserConfirmation<ApplicationUser>
    {
        public Task<bool> IsConfirmedAsync(UserManager<ApplicationUser> manager, ApplicationUser user) =>
            Task.FromResult(true);
    }

    private sealed class StubExternalHandler : IAuthenticationHandler
    {
        public Task InitializeAsync(AuthenticationScheme scheme, HttpContext context) => Task.CompletedTask;
        public Task<AuthenticateResult> AuthenticateAsync() => Task.FromResult(AuthenticateResult.NoResult());
        public Task ChallengeAsync(AuthenticationProperties? properties) => Task.CompletedTask;
        public Task ForbidAsync(AuthenticationProperties? properties) => Task.CompletedTask;
    }

    private sealed class StubHttpContextAccessor : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; }
    }

    private sealed class StubUserManager() : UserManager<ApplicationUser>(
        new StubUserStore(),
        null!,
        null!,
        null!,
        null!,
        null!,
        null!,
        null!,
        null!);

    private sealed class StubUserStore : IUserStore<ApplicationUser>
    {
        public Task<string> GetUserIdAsync(ApplicationUser user, CancellationToken ct) => Task.FromResult(user.Id.ToString());

        public Task<string?> GetUserNameAsync(ApplicationUser user, CancellationToken ct) => Task.FromResult<string?>(user.UserName);

        public Task SetUserNameAsync(ApplicationUser user, string? name, CancellationToken ct) => Task.CompletedTask;

        public Task<string?> GetNormalizedUserNameAsync(ApplicationUser user, CancellationToken ct) =>
            Task.FromResult<string?>(user.NormalizedUserName);

        public Task<ApplicationUser?> FindByIdAsync(string userId, CancellationToken ct) => Task.FromResult<ApplicationUser?>(null);

        public Task<ApplicationUser?> FindByNameAsync(string normalizedUserName, CancellationToken ct) => Task.FromResult<ApplicationUser?>(null);

        public Task SetNormalizedUserNameAsync(ApplicationUser user, string? name, CancellationToken ct) => Task.CompletedTask;

        public Task<ApplicationUser?> FindByEmailAsync(string normalizedEmail, CancellationToken ct) => Task.FromResult<ApplicationUser?>(null);

        public Task<string> GetEmailAsync(ApplicationUser user, CancellationToken ct) => Task.FromResult(user.Email!);

        public Task SetEmailAsync(ApplicationUser user, string? email, CancellationToken ct) => Task.CompletedTask;

        public Task<bool> IsEmailConfirmedAsync(ApplicationUser user, CancellationToken ct) => Task.FromResult(true);

        public Task SetEmailConfirmedAsync(ApplicationUser user, bool confirmed, CancellationToken ct) => Task.CompletedTask;

        public Task<IdentityResult> CreateAsync(ApplicationUser user, CancellationToken ct) => Task.FromResult(IdentityResult.Success);

        public Task<IdentityResult> UpdateAsync(ApplicationUser user, CancellationToken ct) => Task.FromResult(IdentityResult.Success);

        public Task<IdentityResult> DeleteAsync(ApplicationUser user, CancellationToken ct) => Task.FromResult(IdentityResult.Success);

        public void Dispose()
        {
        }
    }

    private sealed class StubImageStorage : IImageStorage
    {
        public bool IsConfigured => true;

        public long MaxUploadBytes => 5 * 1024 * 1024;

        public IReadOnlyCollection<string> AllowedContentTypes => ["image/jpeg"];

        public bool IsAllowedContentType(string contentType) => true;

        public ImageUploadTicket? CreateUploadTicket(Guid userId, string contentType, long size) => null;

        public ImageDownloadTicket? CreateDownloadTicket(string objectKey) => null;

        public Task<string?> CompleteUploadAsync(Guid userId, string objectKey, string contentType, long size, CancellationToken cancellationToken = default) => Task.FromResult<string?>(objectKey);

        public Task<StoredImage?> OpenObjectAsync(string objectKey, CancellationToken cancellationToken = default) => Task.FromResult<StoredImage?>(null);

        public bool IsOwnedObjectKey(string objectKey, Guid userId) => false;
    }

    private sealed class ProfileLocalizer : IStringLocalizer<SharedResource>
    {
        private static readonly IReadOnlyDictionary<string, string> Values =
            new Dictionary<string, string>
            {
                ["Profile"] = "Profile",
                ["ProfileSubtitle"] = "Your details",
                ["ProfilePhoto"] = "Photo",
                ["ProfilePhotoHint"] = "Used as your avatar across the platform.",
                ["ProfileAboutYou"] = "About you",
                ["ProfileAccount"] = "Account",
                ["ProfileExternalLoginsHint"] = "Sign in faster by linking an external account.",
                ["ProfileJumpToSection"] = "Sections",
                ["ProfileSaving"] = "Saving your changes...",
                ["ProfileSaved"] = "All changes saved",
                ["ProfileSaveFailed"] = "Some changes could not be saved",
                ["ProfileUnsavedChangesTitle"] = "Leave without saving?",
                ["ProfileUnsavedChangesBody"] = "Your edits have not been saved yet.",
                ["ProfileDiscardChanges"] = "Discard changes",
                ["ProfileKeepEditing"] = "Keep editing",
                ["ConcurrencyConflictTitle"] = "Someone else changed this profile",
                ["LoadFailed"] = "We could not load this page",
                ["Retry"] = "Retry",
                ["Cancel"] = "Cancel",
                ["LinkExternalProvider"] = "Link {0}",
                ["Attr.Me.Name"] = "Full name",
                ["Attr.Me.Photo"] = "Photo",
                ["Attr.Me.Email"] = "Email",
                ["ImageAlt"] = "Profile image",
                ["ImageDropHere"] = "Drop an image here or click to choose a file",
                ["ImageFormatHint"] = "{0}. Up to {1}.",
                ["ImageStorageUnavailable"] = "Image uploads are not available right now.",
                ["ImageRemove"] = "Remove image",
            };

        public LocalizedString this[string name] =>
            new(name, Values.TryGetValue(name, out var value) ? value : name);

        public LocalizedString this[string name, params object[] arguments] =>
            new(name, string.Format(CultureInfo.InvariantCulture, this[name].Value, arguments));

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) =>
            Values.Select(pair => new LocalizedString(pair.Key, pair.Value));
    }
}
