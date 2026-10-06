namespace CvPlatform.Web.Components.Shared;

/// <summary>Derives the short display initials shown in avatar chips.</summary>
public static class Initials
{
    public static string From(string? name, string? email)
    {
        var source = string.IsNullOrWhiteSpace(name) ? email : name;
        if (string.IsNullOrWhiteSpace(source))
            return "?";

        var parts = source.Split(new[] { ' ', '.', '_', '-', '@' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2)
            return string.Concat(parts[0][0].ToString().ToUpperInvariant(), parts[1][0].ToString().ToUpperInvariant());
        return source.Substring(0, 1).ToUpperInvariant();
    }
}
