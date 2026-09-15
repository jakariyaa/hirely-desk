namespace CvPlatform.Infrastructure.Email;

public sealed class GmailOptions
{
    public const string SectionName = "Gmail";

    public string Address { get; set; } = "";
    public string AppPassword { get; set; } = "";
    public string FromName { get; set; } = "Hirely Desk";
    public bool RequireConfirmedAccount { get; set; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Address) && !string.IsNullOrWhiteSpace(AppPassword);
}
