using AwesomeAssertions;
using Bunit;
using CvPlatform.Core.Enums;
using CvPlatform.Web.Components.Layout;
using CvPlatform.Web.Components.Shared;
using CvPlatform.Web.Resources;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using MudBlazor;
using MudBlazor.Services;
using System.Globalization;

namespace CvPlatform.Tests;

public class ComponentTests : BunitContext
{
    public ComponentTests()
    {
        Services.AddMudServices();
        Services.AddSingleton<IStringLocalizer<SharedResource>, TestLocalizer>();
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

    private sealed class TestLocalizer : IStringLocalizer<SharedResource>
    {
        private static readonly IReadOnlyDictionary<string, string> Values = new Dictionary<string, string>
        {
            ["AddChoice"] = "Add choice",
            ["Choice"] = "Choice",
            ["DropdownOptionsHint"] = "Add the choices users can select.",
            ["Maximum"] = "Maximum",
            ["Minimum"] = "Minimum",
            ["NumericOptionsHint"] = "Optionally limit the allowed numeric range.",
            ["Remove"] = "Remove",
        };

        public LocalizedString this[string name] => new(name, Values.TryGetValue(name, out var value) ? value : name);

        public LocalizedString this[string name, params object[] arguments] =>
            new(name, string.Format(CultureInfo.InvariantCulture, this[name].Value, arguments));

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) =>
            Values.Select(pair => new LocalizedString(pair.Key, pair.Value));

        public IStringLocalizer WithCulture(CultureInfo culture) => this;
    }
}
