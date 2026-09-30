using AwesomeAssertions;
using Bunit;
using CvPlatform.Application.Attributes;
using CvPlatform.Application.Authorization;
using CvPlatform.Application.Common;
using CvPlatform.Core.Enums;
using CvPlatform.Web.Auth;
using CvPlatform.Web.Components.Layout;
using CvPlatform.Web.Components.Shared;
using CvPlatform.Web.Resources;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Facebook;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using MudBlazor;
using MudBlazor.Services;
using System.Globalization;

namespace CvPlatform.Tests;

public class ComponentTests : BunitContext, IAsyncLifetime
{
    public ComponentTests()
    {
        Services.AddMudServices();
        Services.AddCvErrorHandling();
        Services.AddSingleton<IStringLocalizer<SharedResource>, TestLocalizer>();
        Services.AddSingleton<IAttributeDefinitionService, StubAttributeDefinitionService>();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void EmptyState_renders_title_description_and_actions()
    {
        var cut = Render<EmptyState>(parameters => parameters
            .Add(p => p.Title, "Nothing here")
            .Add(p => p.Description, "Try again later")
            .AddChildContent("<button>Retry</button>"));

        cut.Markup.Should().Contain("Nothing here");
        cut.Markup.Should().Contain("Try again later");
        cut.Find(".cv-empty-actions").TextContent.Should().Contain("Retry");
    }

    [Fact]
    public void EmptyState_hides_optional_sections_when_missing()
    {
        var cut = Render<EmptyState>(parameters => parameters
            .Add(p => p.Title, "Nothing here"));

        cut.FindAll(".cv-empty-description").Should().BeEmpty();
        cut.FindAll(".cv-empty-actions").Should().BeEmpty();
    }

    [Fact]
    public void PageHeader_renders_title_subtitle_and_actions()
    {
        var cut = Render<PageHeader>(parameters => parameters
            .Add(p => p.Title, "My CVs")
            .Add(p => p.Subtitle, "Your applications")
            .AddChildContent("<button>New</button>"));

        cut.Find(".cv-page-title").TextContent.Should().Contain("My CVs");
        cut.Find(".cv-page-subtitle").TextContent.Should().Contain("Your applications");
        cut.Find(".cv-page-actions").TextContent.Should().Contain("New");
    }

    [Fact]
    public void PageHeader_hides_subtitle_and_actions_when_missing()
    {
        var cut = Render<PageHeader>(parameters => parameters
            .Add(p => p.Title, "My CVs"));

        cut.FindAll(".cv-page-subtitle").Should().BeEmpty();
        cut.FindAll(".cv-page-actions").Should().BeEmpty();
    }

    [Fact]
    public void PageHeader_renders_icon_when_provided()
    {
        var cut = Render<PageHeader>(parameters => parameters
            .Add(p => p.Title, "My CVs")
            .Add(p => p.Icon, Icons.Material.Filled.Work));

        cut.Find(".cv-page-header-icon").Should().NotBeNull();
    }

    [Fact]
    public void PageHeader_hides_icon_when_missing()
    {
        var cut = Render<PageHeader>(parameters => parameters
            .Add(p => p.Title, "My CVs"));

        cut.FindAll(".cv-page-header-icon").Should().BeEmpty();
    }

    [Fact]
    public void AttributeOptionsEditor_renders_dropdown_choices()
    {
        var cut = Render<AttributeOptionsEditor>(parameters => parameters
            .Add(p => p.DataType, AttributeDataType.Dropdown)
            .Add(p => p.OptionsJson, "{\"choices\":[\"Senior\"]}"));

        cut.Markup.Should().Contain("Senior");
        cut.Markup.Should().Contain("Add choice");
    }

    [Fact]
    public void AttributeOptionsEditor_renders_date_bounds_for_date_attributes()
    {
        var cut = Render<AttributeOptionsEditor>(parameters => parameters
            .Add(p => p.DataType, AttributeDataType.Date)
            .Add(p => p.OptionsJson, "{\"minDate\":\"1990-01-01\",\"maxAgeDays\":43800}"));

        cut.FindComponents<MudDatePicker>().Should().HaveCount(2);
        cut.FindComponents<MudNumericField<int?>>().Should().HaveCount(2);
        cut.Markup.Should().Contain("Optionally limit which calendar days are accepted");
    }

    [Fact]
    public void AttributeValueEditor_warns_when_a_stored_date_is_outside_the_allowed_range()
    {
        var definition = new AttributeDefinitionDto(
            Guid.NewGuid(), "Me.BirthDate", AttributeDataType.Date, true, [],
            new DateRangeDto(new DateOnly(1906, 1, 1), AttributeDateRules.TodayFrom(null)));
        var draft = new AttributeValueDraft { Date = new DateOnly(1899, 12, 31) };

        var cut = Render<AttributeValueEditor>(parameters => parameters
            .Add(p => p.Definition, definition)
            .Add(p => p.Value, draft));

        cut.Markup.Should().Contain("This date is outside the allowed range");
    }

    [Fact]
    public void AttributeValueEditor_stays_silent_for_a_valid_date()
    {
        var today = AttributeDateRules.TodayFrom(null);
        var definition = new AttributeDefinitionDto(
            Guid.NewGuid(), "Me.BirthDate", AttributeDataType.Date, true, [],
            new DateRangeDto(today.AddYears(-120), today));
        var draft = new AttributeValueDraft { Date = new DateOnly(1990, 4, 1) };

        var cut = Render<AttributeValueEditor>(parameters => parameters
            .Add(p => p.Definition, definition)
            .Add(p => p.Value, draft));

        cut.Markup.Should().NotContain("This date is outside the allowed range");
        var picker = cut.FindComponent<MudDatePicker>();
        picker.Instance.MaxDate.Should().Be(today.ToDateTime(TimeOnly.MinValue));
    }

    [Fact]
    public void AttributeDefinitionDialog_renders_a_category_select_populated_from_the_catalog()
    {
        var categories = new[]
        {
            new AttributeCategoryDto(Guid.NewGuid(), "Personal", []),
            new AttributeCategoryDto(Guid.NewGuid(), "Skills", []),
        };

        var cut = OpenAttributeDefinitionDialog(categories);

        var select = cut.FindComponent<MudSelect<Guid>>();
        select.Instance.Label.Should().Be("Category");
        select.Instance.Required.Should().BeTrue();
        cut.FindComponents<MudSelectItem<Guid>>().Select(item => item.Instance.Value)
            .Should().Equal(categories.Select(category => category.Id));
        cut.FindComponents<MudSelectItem<Guid>>().Should().HaveCount(categories.Length);
    }

    [Fact]
    public void AttributeDefinitionDialog_preselects_the_first_category_when_creating()
    {
        var categories = new[]
        {
            new AttributeCategoryDto(Guid.NewGuid(), "Personal", []),
            new AttributeCategoryDto(Guid.NewGuid(), "Skills", []),
        };

        var cut = OpenAttributeDefinitionDialog(categories);

        var select = cut.FindComponent<MudSelect<Guid>>();
        select.Find("div.mud-input").TextContent.Should().Contain("Personal");
        select.Instance.HelperText.Should().NotBeNull();
    }

    [Fact]
    public void AttributeDefinitionDialog_disables_the_category_select_when_no_categories_exist()
    {
        var cut = OpenAttributeDefinitionDialog([]);

        var select = cut.FindComponent<MudSelect<Guid>>();
        select.Instance.Disabled.Should().BeTrue();
        cut.FindComponents<MudSelectItem<Guid>>().Should().BeEmpty();
    }

    private IRenderedComponent<MudDialogProvider> OpenAttributeDefinitionDialog(
        IReadOnlyList<AttributeCategoryDto> categories)
    {
        var provider = Render<MudDialogProvider>();
        var parameters = new DialogParameters
        {
            [nameof(AttributeDefinitionDialog.Categories)] = categories,
        };
        var dialog = Services.GetRequiredService<IDialogService>()
            .ShowAsync<AttributeDefinitionDialog>("New attribute", parameters);
        provider.WaitForAssertion(() => provider.FindComponent<MudSelect<Guid>>().Should().NotBeNull());
        _ = dialog.Result;
        return provider;
    }

    [Fact]
    public void Navigation_marks_position_templates_without_marking_positions()
    {
        var authorization = AddAuthorization();
        authorization.SetAuthorized("Recruiter");
        authorization.SetRoles("Recruiter");
        Services.GetRequiredService<NavigationManager>().NavigateTo("/positions/templates");

        var cut = Render<NavMenu>();
        var positions = cut.Find("a[href=\"/positions\"]");
        var templates = cut.Find("a[href=\"/positions/templates\"]");

        positions.GetAttribute("class").Should().NotContain("active");
        templates.GetAttribute("class").Should().Contain("active");
    }

    [Fact]
    public void ExternalProviderButtons_render_brand_icons_and_login_urls()
    {
        var providers = new List<AuthenticationScheme>
        {
            new("Google", "Google", typeof(OAuthHandler<OAuthOptions>)),
            new("Facebook", "Facebook", typeof(FacebookHandler)),
        };

        var cut = Render<ExternalProviderButtons>(parameters => parameters
            .Add(p => p.Providers, providers)
            .Add(p => p.ReturnUrl, "/mycvs"));

        var buttons = cut.FindAll("a.cv-oauth-btn");
        buttons.Should().HaveCount(2);
        buttons[0].GetAttribute("href").Should().Be("/Account/ExternalLogin?provider=Google&returnUrl=%2Fmycvs");
        buttons[1].GetAttribute("href").Should().Be("/Account/ExternalLogin?provider=Facebook&returnUrl=%2Fmycvs");
        buttons[0].QuerySelector("svg").Should().NotBeNull();
        buttons[0].TextContent.Should().Contain("Google");
        buttons[1].TextContent.Should().Contain("Facebook");
        cut.FindAll("svg path").Should().HaveCount(2);
    }

    [Fact]
    public void ExternalProviderButtons_fall_back_to_neutral_icon_and_local_return_url()
    {
        var providers = new List<AuthenticationScheme>
        {
            new("GitHub", "GitHub", typeof(OAuthHandler<OAuthOptions>)),
        };

        var cut = Render<ExternalProviderButtons>(parameters => parameters
            .Add(p => p.Providers, providers)
            .Add(p => p.ReturnUrl, "https://evil.example/steal")
            .Add(p => p.LinkMode, true));

        var button = cut.Find("a.cv-oauth-btn");
        button.GetAttribute("href").Should().Be("/Account/LinkExternalLogin?provider=GitHub&returnUrl=%2F");
        cut.FindAll("svg path").Should().ContainSingle();
    }

    [Fact]
    public void ExternalProviderButtons_render_nothing_without_providers()
    {
        var cut = Render<ExternalProviderButtons>();

        cut.Markup.Should().BeEmpty();
    }

    [Fact]
    public void ExternalProviderBranding_maps_known_schemes_case_insensitively()
    {
        ExternalProviderBranding.IconFor("google").Should().Be(Icons.Custom.Brands.Google);
        ExternalProviderBranding.IconFor("FACEBOOK").Should().Be(Icons.Custom.Brands.Facebook);
        ExternalProviderBranding.IconFor("unknown").Should().Be(ExternalProviderBranding.FallbackIcon);
        ExternalProviderBranding.IconFor(null).Should().Be(ExternalProviderBranding.FallbackIcon);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    async Task IAsyncLifetime.DisposeAsync() => await base.DisposeAsync();

    private sealed class StubAttributeDefinitionService : IAttributeDefinitionService
    {
        public Task<Result<PagedResult<AttributeDefinitionAdminDto>>> ListAsync(
            ActorContext actor, AttributeCatalogQuery query, CancellationToken ct = default) =>
            Task.FromResult(Result<PagedResult<AttributeDefinitionAdminDto>>.Success(new([], 0, 1, 20)));

        public Task<Result<AttributeDefinitionAdminDto>> CreateAsync(
            ActorContext actor, AttributeDefinitionInput input, CancellationToken ct = default) =>
            Task.FromResult(Result<AttributeDefinitionAdminDto>.Success(Empty()));

        public Task<Result<AttributeDefinitionAdminDto>> UpdateAsync(
            ActorContext actor, Guid id, AttributeDefinitionInput input, CancellationToken ct = default) =>
            Task.FromResult(Result<AttributeDefinitionAdminDto>.Success(Empty()));

        public Task<Result<AttributeDeleteImpactDto>> GetDeleteImpactAsync(
            ActorContext actor, Guid id, CancellationToken ct = default) =>
            Task.FromResult(Result<AttributeDeleteImpactDto>.Success(AttributeDeleteImpactDto.Zero));

        public Task<Result<AttributeDeleteImpactDto>> GetDeleteImpactAsync(
            ActorContext actor, IReadOnlyList<Guid> ids, CancellationToken ct = default) =>
            Task.FromResult(Result<AttributeDeleteImpactDto>.Success(AttributeDeleteImpactDto.Zero));

        public Task<Result<AttributeOptionImpactDto>> GetOptionChangeImpactAsync(
            ActorContext actor, Guid id, AttributeDefinitionInput input, CancellationToken ct = default) =>
            Task.FromResult(Result<AttributeOptionImpactDto>.Success(new([], 0, 0)));

        public Task<Result> DeleteAsync(
            ActorContext actor, Guid id, long expectedVersion, CancellationToken ct = default) =>
            Task.FromResult(Result.Success());

        public Task<Result> DeleteManyAsync(
            ActorContext actor, IReadOnlyList<AttributeDefinitionDeleteInput> items, CancellationToken ct = default) =>
            Task.FromResult(Result.Success());

        private static AttributeDefinitionAdminDto Empty() =>
            new(Guid.Empty, Guid.Empty, "", "", null, AttributeDataType.String, false, null, 0);
    }

    private sealed class TestLocalizer : IStringLocalizer<SharedResource>
    {
        private static readonly IReadOnlyDictionary<string, string> Values = new Dictionary<string, string>
        {
            ["AddChoice"] = "Add choice",
            ["Choice"] = "Choice",
            ["DropdownOptionsHint"] = "Add the choices users can select.",
            ["DateOptionsHint"] = "Optionally limit which calendar days are accepted. Age limits are relative to today.",
            ["DateOutOfRange"] = "This date is outside the allowed range. Please pick another one.",
            ["MinimumDate"] = "Minimum date",
            ["MaximumDate"] = "Maximum date",
            ["MinimumAgeDays"] = "Minimum age (days)",
            ["MaximumAgeDays"] = "Maximum age (days)",
            ["Maximum"] = "Maximum",
            ["Minimum"] = "Minimum",
            ["NumericOptionsHint"] = "Optionally limit the allowed numeric range.",
            ["Remove"] = "Remove",
            ["Category"] = "Category",
            ["CategoryHelper"] = "Shared resource attributes belong to a category.",
            ["Cancel"] = "Cancel",
            ["Save"] = "Save",
        };

        public LocalizedString this[string name] => new(name, Values.TryGetValue(name, out var value) ? value : name);

        public LocalizedString this[string name, params object[] arguments] =>
            new(name, string.Format(CultureInfo.InvariantCulture, this[name].Value, arguments));

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) =>
            Values.Select(pair => new LocalizedString(pair.Key, pair.Value));

        public IStringLocalizer WithCulture(CultureInfo culture) => this;
    }
}
