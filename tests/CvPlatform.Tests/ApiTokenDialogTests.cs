using AwesomeAssertions;
using Bunit;
using CvPlatform.Application.Authorization;
using CvPlatform.Application.Common;
using CvPlatform.Application.Integration;
using CvPlatform.Web.Components.Shared;
using CvPlatform.Web.Resources;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using MudBlazor;
using MudBlazor.Services;

namespace CvPlatform.Tests;

public class ApiTokenDialogTests : BunitContext, IAsyncLifetime
{
    private sealed class StubTokenService : IPositionApiTokenService
    {
        public List<PositionApiTokenDto> Tokens { get; } = [];
        public PositionApiTokenCreatedDto? GeneratedResult { get; set; }
        public int GeneratedCount { get; private set; }

        public Task<Result<PositionApiTokenCreatedDto>> GenerateAsync(
            ActorContext actor, Guid positionId, string? name, CancellationToken ct = default)
        {
            GeneratedCount++;
            return Task.FromResult(GeneratedResult is null
                ? Result<PositionApiTokenCreatedDto>.Failure(ErrorCodes.Unexpected, "boom")
                : Result<PositionApiTokenCreatedDto>.Success(GeneratedResult));
        }

        public Task<Result<IReadOnlyList<PositionApiTokenDto>>> ListAsync(
            ActorContext actor, Guid positionId, CancellationToken ct = default) =>
            Task.FromResult(Result<IReadOnlyList<PositionApiTokenDto>>.Success(Tokens));

        public Task<Result> RevokeAsync(
            ActorContext actor, Guid positionId, Guid tokenId, CancellationToken ct = default)
        {
            Tokens.RemoveAll(t => t.Id == tokenId);
            return Task.FromResult(Result.Success());
        }
    }

    private sealed class TestLocalizer : StubStringLocalizer
    {
        protected override IReadOnlyDictionary<string, string> Catalog { get; } =
            new Dictionary<string, string>
            {
                ["ApiTokenDialogTitle"] = "Position API tokens",
                ["ApiTokenHint"] = "External systems read aggregated results with a token.",
                ["ApiTokenEmpty"] = "No API tokens for this position yet.",
                ["ApiTokenActive"] = "Active",
                ["ApiTokenStatusRevoked"] = "Revoked",
                ["ApiTokenCreated"] = "Created",
                ["ApiTokenLastUsed"] = "Last used",
                ["ApiTokenNever"] = "never",
                ["ApiTokenNameLabel"] = "Token name",
                ["ApiTokenNew"] = "Generate token",
                ["ApiTokenGenerated"] = "Copy this token now.",
                ["ApiTokenGeneratedTitle"] = "Generated API token",
                ["ApiTokenCopy"] = "Copy to clipboard",
                ["ApiTokenCopied"] = "Token copied to clipboard.",
                ["ApiTokenRevoke"] = "Revoke",
                ["ApiTokenRevoked"] = "API token revoked.",
                ["ApiTokenRevokeConfirm"] = "Revoke this API token?",
                ["ApiTokenDone"] = "Done",
                ["Close"] = "Close",
                ["Cancel"] = "Cancel",
            };
    }

    private readonly StubTokenService _tokens = new();

    public ApiTokenDialogTests()
    {
        Services.AddMudServices();
        Services.AddCvErrorHandling();
        Services.AddSingleton<IStringLocalizer<SharedResource>, TestLocalizer>();
        Services.AddSingleton<IPositionApiTokenService>(_tokens);
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private IRenderedComponent<MudDialogProvider> OpenDialog()
    {
        var provider = Render<MudDialogProvider>();
        var parameters = new DialogParameters
        {
            [nameof(ApiTokenDialog.Actor)] = new ActorContext(Guid.NewGuid(), false, true),
            [nameof(ApiTokenDialog.PositionId)] = Guid.NewGuid(),
        };
        var dialog = Services.GetRequiredService<IDialogService>()
            .ShowAsync<ApiTokenDialog>("Position API tokens", parameters);
        provider.WaitForAssertion(() => provider.FindComponent<ApiTokenDialog>().Should().NotBeNull());
        _ = dialog.Result;
        return provider;
    }

    [Fact]
    public void Dialog_lists_existing_tokens_and_marks_revoked_ones()
    {
        _tokens.Tokens.Add(new PositionApiTokenDto(
            Guid.NewGuid(), "Odoo production", new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 10, 4, 8, 0, 0, DateTimeKind.Utc), null));
        _tokens.Tokens.Add(new PositionApiTokenDto(
            Guid.NewGuid(), "Old integration", new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc),
            null, new DateTime(2026, 9, 2, 8, 0, 0, DateTimeKind.Utc)));

        var cut = OpenDialog();

        cut.Markup.Should().Contain("Odoo production");
        cut.Markup.Should().Contain("Old integration");
        cut.Markup.Should().Contain("Active");
        cut.Markup.Should().Contain("Revoked");
        cut.Markup.Should().Contain("Generate token");
    }

    [Fact]
    public void Dialog_shows_an_empty_state_without_tokens()
    {
        var cut = OpenDialog();

        cut.Markup.Should().Contain("No API tokens for this position yet.");
    }

    [Fact]
    public void Generating_a_token_shows_the_secret_exactly_once_with_a_copy_action()
    {
        _tokens.GeneratedResult = new PositionApiTokenCreatedDto(
            Guid.NewGuid(), "Odoo production", "cvp_secret-value",
            new DateTime(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc));

        var cut = OpenDialog();

        cut.Find("button").Click();
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("cvp_secret-value"));

        cut.Markup.Should().Contain("Copy this token now.");
        cut.Markup.Should().Contain("Done");
        cut.Markup.Should().NotContain("Generate token");
        _tokens.GeneratedCount.Should().Be(1);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    async Task IAsyncLifetime.DisposeAsync() => await base.DisposeAsync();
}
