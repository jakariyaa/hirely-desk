namespace CvPlatform.Infrastructure.Crm;

public sealed class SalesforceOptions
{
    public const string SectionName = "Salesforce";

    public string InstanceUrl { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string ApiVersion { get; set; } = "v61.0";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(InstanceUrl) &&
        !string.IsNullOrWhiteSpace(ClientId) &&
        !string.IsNullOrWhiteSpace(ClientSecret);
}
