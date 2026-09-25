using AwesomeAssertions;
using CvPlatform.Web.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CvPlatform.Tests;

public class ConfigurationTests
{
    [Fact]
    public void Valid_configuration_binds_nested_sections()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = "Host=localhost;Database=cvplatform",
            ["Database:SkipMigrate"] = "true",
            ["Seed:AdminPassword"] = "admin-password",
            ["Seed:DemoPassword"] = "demo-password",
            ["B2:Region"] = "us-west-004",
            ["B2:BucketName"] = "images",
            ["B2:ApplicationKeyId"] = "application-key-id",
            ["B2:ApplicationKey"] = "application-key",
            ["Gmail:Address"] = "user@example.com",
            ["Gmail:AppPassword"] = "app-password",
            ["Authentication:Google:ClientId"] = "google-client-id",
            ["Authentication:Google:ClientSecret"] = "google-client-secret",
        });

        var services = new ServiceCollection();
        var appConfiguration = services.AddCvPlatformConfiguration(configuration);

        appConfiguration.ConnectionStrings.Default.Should().Be("Host=localhost;Database=cvplatform");
        appConfiguration.Database.SkipMigrate.Should().BeTrue();
        appConfiguration.B2.IsConfigured.Should().BeTrue();
        appConfiguration.Gmail.IsConfigured.Should().BeTrue();
        appConfiguration.Authentication.Google.IsConfigured.Should().BeTrue();
    }

    [Fact]
    public void Optional_integrations_may_be_unconfigured()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = "Host=localhost;Database=cvplatform",
            ["Seed:AdminPassword"] = "admin-password",
            ["Seed:DemoPassword"] = "demo-password",
        });

        var services = new ServiceCollection();

        var appConfiguration = services.AddCvPlatformConfiguration(configuration);

        appConfiguration.B2.IsConfigured.Should().BeFalse();
        appConfiguration.Gmail.IsConfigured.Should().BeFalse();
        appConfiguration.Authentication.Google.IsConfigured.Should().BeFalse();
        appConfiguration.Authentication.Facebook.IsConfigured.Should().BeFalse();
    }

    [Fact]
    public void Partial_b2_configuration_is_rejected()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = "Host=localhost;Database=cvplatform",
            ["Seed:AdminPassword"] = "admin-password",
            ["Seed:DemoPassword"] = "demo-password",
            ["B2:Region"] = "us-west-004",
            ["B2:BucketName"] = "images",
        });

        var services = new ServiceCollection();

        var exception = Record.Exception(() => services.AddCvPlatformConfiguration(configuration));

        exception.Should().NotBeNull();
        exception!.Message.Should().Contain("B2:ApplicationKeyId");
    }

    [Theory]
    [InlineData("B2:ApplicationKeyId", "B2:ApplicationKey")]
    [InlineData("Authentication:Google:ClientId", "Authentication:Google:ClientSecret")]
    [InlineData("Authentication:Facebook:AppId", "Authentication:Facebook:AppSecret")]
    [InlineData("Gmail:Address", "Gmail:AppPassword")]
    public void Partial_optional_credentials_are_rejected(string configuredKey, string missingKey)
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = "Host=localhost;Database=cvplatform",
            ["Seed:AdminPassword"] = "admin-password",
            ["Seed:DemoPassword"] = "demo-password",
            [configuredKey] = configuredKey.Contains("Address", StringComparison.Ordinal)
                ? "user@example.com"
                : "configured-value",
        });

        var services = new ServiceCollection();

        var exception = Record.Exception(() => services.AddCvPlatformConfiguration(configuration));

        exception.Should().NotBeNull();
        exception!.Message.Should().Contain(missingKey);
        exception.Message.Should().NotContain("admin-password");
        exception.Message.Should().NotContain("configured-value");
    }

    [Fact]
    public void Invalid_gmail_address_is_rejected()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = "Host=localhost;Database=cvplatform",
            ["Seed:AdminPassword"] = "admin-password",
            ["Seed:DemoPassword"] = "demo-password",
            ["Gmail:Address"] = "not-an-email",
            ["Gmail:AppPassword"] = "app-password",
        });

        var services = new ServiceCollection();

        var exception = Record.Exception(() => services.AddCvPlatformConfiguration(configuration));

        exception.Should().NotBeNull();
        exception!.Message.Should().Contain("Gmail:Address");
    }

    private static IConfiguration BuildConfiguration(IReadOnlyDictionary<string, string?> values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
}
