using CvPlatform.Infrastructure.Email;
using CvPlatform.Infrastructure.Storage;

namespace CvPlatform.Web.Configuration;

public sealed class ApplicationConfiguration
{
    public ConnectionStringsOptions ConnectionStrings { get; set; } = new();
    public DatabaseOptions Database { get; set; } = new();
    public ExternalAuthenticationOptions Authentication { get; set; } = new();
    public SeedOptions Seed { get; set; } = new();
    public B2Options B2 { get; set; } = new();
    public GmailOptions Gmail { get; set; } = new();
}

public sealed class ConnectionStringsOptions
{
    public const string SectionName = "ConnectionStrings";

    public string Default { get; set; } = "";
}

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    public bool SkipMigrate { get; set; }
}

public sealed class ExternalAuthenticationOptions
{
    public GoogleAuthenticationOptions Google { get; set; } = new();
    public FacebookAuthenticationOptions Facebook { get; set; } = new();
}

public sealed class GoogleAuthenticationOptions
{
    public const string SectionName = "Authentication:Google";

    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ClientId) &&
        !string.IsNullOrWhiteSpace(ClientSecret);
}

public sealed class FacebookAuthenticationOptions
{
    public const string SectionName = "Authentication:Facebook";

    public string AppId { get; set; } = "";
    public string AppSecret { get; set; } = "";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(AppId) &&
        !string.IsNullOrWhiteSpace(AppSecret);
}

public sealed class SeedOptions
{
    public const string SectionName = "Seed";

    public string AdminPassword { get; set; } = "";
    public string DemoPassword { get; set; } = "";
}
