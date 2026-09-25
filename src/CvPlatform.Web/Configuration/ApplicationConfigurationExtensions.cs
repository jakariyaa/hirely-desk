using CvPlatform.Infrastructure.Email;
using CvPlatform.Infrastructure.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CvPlatform.Web.Configuration;

public static class ApplicationConfigurationExtensions
{
    public static ApplicationConfiguration AddCvPlatformConfiguration(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var applicationConfiguration = configuration.Get<ApplicationConfiguration>() ?? new();
        var failures = ApplicationConfigurationValidator.Validate(applicationConfiguration);
        if (failures.Count > 0)
            throw new InvalidOperationException(
                $"Invalid application configuration:{Environment.NewLine}- {string.Join(Environment.NewLine + "- ", failures)}");

        services.AddValidatedOptions<ConnectionStringsOptions>(
            configuration,
            ConnectionStringsOptions.SectionName,
            new ConnectionStringsOptionsValidator());
        services.AddValidatedOptions<DatabaseOptions>(
            configuration,
            DatabaseOptions.SectionName,
            new DatabaseOptionsValidator());
        services.AddValidatedOptions<GoogleAuthenticationOptions>(
            configuration,
            GoogleAuthenticationOptions.SectionName,
            new GoogleAuthenticationOptionsValidator());
        services.AddValidatedOptions<FacebookAuthenticationOptions>(
            configuration,
            FacebookAuthenticationOptions.SectionName,
            new FacebookAuthenticationOptionsValidator());
        services.AddValidatedOptions<SeedOptions>(
            configuration,
            SeedOptions.SectionName,
            new SeedOptionsValidator());
        services.AddValidatedOptions<B2Options>(
            configuration,
            B2Options.SectionName,
            new B2OptionsValidator());
        services.AddValidatedOptions<GmailOptions>(
            configuration,
            GmailOptions.SectionName,
            new GmailOptionsValidator());

        return applicationConfiguration;
    }

    private static void AddValidatedOptions<TOptions>(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionName,
        IValidateOptions<TOptions> validator)
        where TOptions : class
    {
        services.AddOptions<TOptions>()
            .Bind(configuration.GetSection(sectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<TOptions>>(validator);
    }
}
