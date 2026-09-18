using CvPlatform.Web.Resources;
using Microsoft.Extensions.Localization;

namespace CvPlatform.Web.Resources;

public static class AttributeLabels
{
    public static string Localize(IStringLocalizer<SharedResource> localizer, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return name;
        var localized = localizer[$"Attr.{name}"];
        return localized.ResourceNotFound ? name : localized.Value;
    }
}
