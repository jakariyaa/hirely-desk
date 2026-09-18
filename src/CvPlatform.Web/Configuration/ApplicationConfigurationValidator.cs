using System.Net.Mail;
using CvPlatform.Infrastructure.Email;
using CvPlatform.Infrastructure.Storage;
using Microsoft.Extensions.Options;

namespace CvPlatform.Web.Configuration;

internal static class ApplicationConfigurationValidator
{
    public static IReadOnlyList<string> Validate(ApplicationConfiguration configuration)
    {
        return
        [
            .. ValidateConnectionStrings(configuration.ConnectionStrings),
            .. ValidateDatabase(configuration.Database),
            .. ValidateGoogle(configuration.Authentication.Google),
            .. ValidateFacebook(configuration.Authentication.Facebook),
            .. ValidateSeed(configuration.Seed),
            .. ValidateCloudinary(configuration.Cloudinary),
            .. ValidateGmail(configuration.Gmail),
        ];
    }

    public static IReadOnlyList<string> ValidateConnectionStrings(ConnectionStringsOptions options)
    {
        return string.IsNullOrWhiteSpace(options.Default)
            ? ["ConnectionStrings:Default is required."]
            : [];
    }

    public static IReadOnlyList<string> ValidateDatabase(DatabaseOptions options)
    {
        return [];
    }

    public static IReadOnlyList<string> ValidateGoogle(GoogleAuthenticationOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ClientId) &&
            string.IsNullOrWhiteSpace(options.ClientSecret))
            return [];

        if (string.IsNullOrWhiteSpace(options.ClientId))
            return ["Authentication:Google:ClientId is required when ClientSecret is configured."];

        if (string.IsNullOrWhiteSpace(options.ClientSecret))
            return ["Authentication:Google:ClientSecret is required when ClientId is configured."];

        return [];
    }

    public static IReadOnlyList<string> ValidateFacebook(FacebookAuthenticationOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.AppId) &&
            string.IsNullOrWhiteSpace(options.AppSecret))
            return [];

        if (string.IsNullOrWhiteSpace(options.AppId))
            return ["Authentication:Facebook:AppId is required when AppSecret is configured."];

        if (string.IsNullOrWhiteSpace(options.AppSecret))
            return ["Authentication:Facebook:AppSecret is required when AppId is configured."];

        return [];
    }

    public static IReadOnlyList<string> ValidateSeed(SeedOptions options)
    {
        var failures = new List<string>();
        if (string.IsNullOrWhiteSpace(options.AdminPassword))
            failures.Add("Seed:AdminPassword is required.");
        else if (options.AdminPassword.Length < 8)
            failures.Add("Seed:AdminPassword must be at least 8 characters long.");
        if (string.IsNullOrWhiteSpace(options.DemoPassword))
            failures.Add("Seed:DemoPassword is required.");
        else if (options.DemoPassword.Length < 8)
            failures.Add("Seed:DemoPassword must be at least 8 characters long.");
        return failures;
    }

    public static IReadOnlyList<string> ValidateCloudinary(CloudinaryOptions options)
    {
        var hasCloudName = !string.IsNullOrWhiteSpace(options.CloudName);
        var hasUploadPreset = !string.IsNullOrWhiteSpace(options.UploadPreset);
        var hasApiKey = !string.IsNullOrWhiteSpace(options.ApiKey);
        var hasApiSecret = !string.IsNullOrWhiteSpace(options.ApiSecret);
        var hasAnyValue = hasCloudName || hasUploadPreset || hasApiKey || hasApiSecret;

        if (!hasAnyValue)
            return [];

        var failures = new List<string>();
        if (!hasCloudName)
            failures.Add("Cloudinary:CloudName is required when Cloudinary is configured.");
        if (hasApiKey != hasApiSecret)
            failures.Add("Cloudinary:ApiKey and Cloudinary:ApiSecret must be configured together.");
        if (!hasUploadPreset && !(hasApiKey && hasApiSecret))
            failures.Add("Cloudinary:UploadPreset or the Cloudinary API key pair is required.");
        return failures;
    }

    public static IReadOnlyList<string> ValidateGmail(GmailOptions options)
    {
        var hasAddress = !string.IsNullOrWhiteSpace(options.Address);
        var hasPassword = !string.IsNullOrWhiteSpace(options.AppPassword);

        if (!hasAddress && !hasPassword)
            return [];

        var failures = new List<string>();
        if (!hasAddress)
            failures.Add("Gmail:Address is required when Gmail is configured.");
        if (!hasPassword)
            failures.Add("Gmail:AppPassword is required when Gmail is configured.");
        if (hasAddress)
        {
            try
            {
                _ = new MailAddress(options.Address);
            }
            catch (FormatException)
            {
                failures.Add("Gmail:Address must be a valid email address.");
            }
        }
        return failures;
    }
}

internal sealed class ConnectionStringsOptionsValidator : IValidateOptions<ConnectionStringsOptions>
{
    public ValidateOptionsResult Validate(string? name, ConnectionStringsOptions options) =>
        ValidateOptionsResultFactory.Create(ApplicationConfigurationValidator.ValidateConnectionStrings(options));
}

internal sealed class DatabaseOptionsValidator : IValidateOptions<DatabaseOptions>
{
    public ValidateOptionsResult Validate(string? name, DatabaseOptions options) =>
        ValidateOptionsResultFactory.Create(ApplicationConfigurationValidator.ValidateDatabase(options));
}

internal sealed class GoogleAuthenticationOptionsValidator : IValidateOptions<GoogleAuthenticationOptions>
{
    public ValidateOptionsResult Validate(string? name, GoogleAuthenticationOptions options) =>
        ValidateOptionsResultFactory.Create(ApplicationConfigurationValidator.ValidateGoogle(options));
}

internal sealed class FacebookAuthenticationOptionsValidator : IValidateOptions<FacebookAuthenticationOptions>
{
    public ValidateOptionsResult Validate(string? name, FacebookAuthenticationOptions options) =>
        ValidateOptionsResultFactory.Create(ApplicationConfigurationValidator.ValidateFacebook(options));
}

internal sealed class SeedOptionsValidator : IValidateOptions<SeedOptions>
{
    public ValidateOptionsResult Validate(string? name, SeedOptions options) =>
        ValidateOptionsResultFactory.Create(ApplicationConfigurationValidator.ValidateSeed(options));
}

internal sealed class CloudinaryOptionsValidator : IValidateOptions<CloudinaryOptions>
{
    public ValidateOptionsResult Validate(string? name, CloudinaryOptions options) =>
        ValidateOptionsResultFactory.Create(ApplicationConfigurationValidator.ValidateCloudinary(options));
}

internal sealed class GmailOptionsValidator : IValidateOptions<GmailOptions>
{
    public ValidateOptionsResult Validate(string? name, GmailOptions options) =>
        ValidateOptionsResultFactory.Create(ApplicationConfigurationValidator.ValidateGmail(options));
}

internal static class ValidateOptionsResultFactory
{
    public static ValidateOptionsResult Create(IReadOnlyList<string> failures) =>
        failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
}
