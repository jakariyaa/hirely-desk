namespace CvPlatform.Core.Security;

public static class RedirectUrlHelper
{
    public static bool IsLocalUrl(string? url) =>
        !string.IsNullOrEmpty(url)
        && url[0] == '/'
        && (url.Length == 1 || (url[1] != '/' && url[1] != '\\'));

    public static string SafeReturnUrl(string? url) => IsLocalUrl(url) ? url! : "/";
}
