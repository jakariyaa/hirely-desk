using Microsoft.AspNetCore.Identity;

namespace CvPlatform.Core.Entities;

public class ApplicationUser : IdentityUser<Guid>
{
    public string PreferredLanguage { get; set; } = "en";
    public string PreferredTheme { get; set; } = "light";

    public Profile Profile { get; set; } = default!;
}
