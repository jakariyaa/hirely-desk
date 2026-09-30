using AwesomeAssertions;
using Bunit;
using CvPlatform.Application.Common;
using CvPlatform.Web.Components.Shared;
using CvPlatform.Web.ErrorHandling;
using CvPlatform.Web.Resources;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using MudBlazor.Services;

namespace CvPlatform.Tests;

public class AppErrorBoundaryTests : BunitContext
{
    private const string Title = "friendly boundary title";
    private const string Body = "friendly boundary body";
    private const string Secret = "connection string is secret";

    public AppErrorBoundaryTests()
    {
        Services.AddCvErrorHandling();
        Services.AddSingleton<IStringLocalizer<SharedResource>, BoundaryLocalizer>();
    }

    [Fact]
    public void The_child_renders_normally_while_nothing_throws()
    {
        var cut = RenderBoundary(Static("child content"));

        cut.Markup.Should().Contain("child content");
    }

    [Fact]
    public void An_escaping_exception_replaces_the_child_with_localized_friendly_text()
    {
        var cut = RenderBoundary(Exploding());

        cut.Markup.Should().Contain(Title);
        cut.Markup.Should().Contain(Body);
        cut.Markup.Should().NotContain(Secret);
        cut.Markup.Should().NotContain("child content");
    }

    [Fact]
    public void The_reference_id_is_hidden_in_development()
    {
        using var development = new DevelopmentContext();

        var cut = development.RenderBoundary(Exploding());

        cut.FindAll("code").Should().BeEmpty();
    }

    [Fact]
    public void The_reference_id_is_shown_outside_development()
    {
        var cut = RenderBoundary(Exploding());

        cut.FindAll("code").Should().ContainSingle();
    }

    [Fact]
    public void Recover_puts_the_child_back()
    {
        var attempts = 0;
        var cut = RenderBoundary(FlakyChild(() => attempts++ == 0));
        cut.Markup.Should().Contain(Title);

        cut.InvokeAsync(() => cut.Instance.Recover());

        cut.Markup.Should().Contain("child content");
    }

    private IRenderedComponent<AppErrorBoundary> RenderBoundary(RenderFragment child) =>
        Render<AppErrorBoundary>(parameters => parameters.AddChildContent(child));

    private static RenderFragment Static(string text) =>
        builder => builder.AddContent(0, text);

    private static RenderFragment Exploding() =>
        _ => throw new InvalidOperationException(Secret);

    private static RenderFragment FlakyChild(Func<bool> shouldThrow) => builder =>
    {
        if (shouldThrow())
            throw new InvalidOperationException(Secret);
        builder.AddContent(0, "child content");
    };

    private sealed class DevelopmentContext : BunitContext
    {
        public DevelopmentContext()
        {
            Services.AddCvErrorHandling(ErrorHandlingTestServices.Development);
            Services.AddSingleton<IStringLocalizer<SharedResource>, BoundaryLocalizer>();
        }

        public IRenderedComponent<AppErrorBoundary> RenderBoundary(RenderFragment child) =>
            Render<AppErrorBoundary>(parameters => parameters.AddChildContent(child));
    }

    private sealed class BoundaryLocalizer : StubStringLocalizer
    {
        private static readonly IReadOnlyDictionary<string, string> Values =
            new Dictionary<string, string>
            {
                ["ErrorBoundaryTitle"] = Title,
                ["ErrorBoundaryBody"] = Body,
                ["ErrorReferenceId"] = "Reference",
                ["Retry"] = "Retry"
            };

        protected override IReadOnlyDictionary<string, string> Catalog => Values;
    }
}

public class UiErrorReporterTests : BunitContext
{
    public UiErrorReporterTests()
    {
        Services.AddMudServices();
        Services.AddCvErrorHandling();
    }

    [Fact]
    public void Reporting_a_domain_error_returns_friendly_text_without_developer_detail()
    {
        var reporter = Services.GetRequiredService<IUiErrorReporter>();

        var text = reporter.Report(new Error(ErrorCodes.Forbidden, "internal recruiter note"));

        text.Should().NotBeNullOrWhiteSpace();
        text.Should().NotContain("recruiter");
    }

    [Fact]
    public void Reporting_a_validation_error_keeps_the_validator_message()
    {
        var reporter = Services.GetRequiredService<IUiErrorReporter>();

        var text = reporter.Report(new Error(ErrorCodes.ValidationFailed, "Title must not be empty."));

        text.Should().Contain("Title must not be empty.");
    }

    [Fact]
    public void Reporting_an_exception_hides_its_message_in_production()
    {
        var reporter = Services.GetRequiredService<IUiErrorReporter>();

        var text = reporter.Report(new InvalidOperationException("connection string is secret"));

        text.Should().NotContain("secret");
    }

    [Fact]
    public void Reporting_an_exception_surfaces_its_message_in_development()
    {
        using var development = new BunitContext();
        development.Services.AddMudServices();
        development.Services.AddCvErrorHandling(ErrorHandlingTestServices.Development);
        var reporter = development.Services.GetRequiredService<IUiErrorReporter>();

        var text = reporter.Report(new InvalidOperationException("NullReference at Foo.Bar"));

        text.Should().Contain("NullReference at Foo.Bar");
    }
}