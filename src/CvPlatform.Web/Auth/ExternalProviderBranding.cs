using CvPlatform.Core.Security;
using Microsoft.AspNetCore.Authentication;
using MudBlazor;

namespace CvPlatform.Web.Auth;

/// <summary>Brand presentation for the configured external authentication providers.</summary>
public static class ExternalProviderBranding
{
    private static readonly IReadOnlyDictionary<string, string> BrandIcons =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Google"] = Icons.Custom.Brands.Google,
            ["Facebook"] = Icons.Custom.Brands.Facebook,
        };

    public static string FallbackIcon => Icons.Material.Filled.AccountCircle;

    /// <summary>The brand glyph for a scheme, or a neutral fallback for providers without one.</summary>
    public static string IconFor(string? scheme) =>
        !string.IsNullOrWhiteSpace(scheme) && BrandIcons.TryGetValue(scheme, out var icon)
            ? icon
            : FallbackIcon;

    /// <summary>The sign-in endpoint for a provider, with the return path escaped for the query string.</summary>
    public static string LoginUrl(AuthenticationScheme scheme, string? returnUrl) =>
        $"/Account/ExternalLogin?provider={Uri.EscapeDataString(scheme.Name)}" +
        $"&returnUrl={Uri.EscapeDataString(NormalizeReturnUrl(returnUrl))}";

    /// <summary>The link endpoint for a provider, used from an already signed-in profile.</summary>
    public static string LinkUrl(AuthenticationScheme scheme, string? returnUrl) =>
        $"/Account/LinkExternalLogin?provider={Uri.EscapeDataString(scheme.Name)}" +
        $"&returnUrl={Uri.EscapeDataString(NormalizeReturnUrl(returnUrl))}";

    public static string DisplayName(AuthenticationScheme scheme) =>
        string.IsNullOrWhiteSpace(scheme.DisplayName) ? scheme.Name : scheme.DisplayName;

    private static string NormalizeReturnUrl(string? returnUrl) =>
        RedirectUrlHelper.SafeReturnUrl(returnUrl);
}