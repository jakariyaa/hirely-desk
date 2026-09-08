using MudBlazor;

namespace CvPlatform.Web.Theme;

/// <summary>
/// Central design tokens for the whole application: corporate blue brand palette,
/// light and dark variants, typography and layout properties.
/// Every surface (app bar, drawer, pages, dialogs) derives from this single theme.
/// </summary>
public static class CvPlatformTheme
{
    // Brand palette — corporate blue.
    public const string PrimaryHex = "#1565C0";   // Blue 800
    public const string PrimaryDarkHex = "#0D47A1"; // Blue 900
    public const string SecondaryHex = "#00838F"; // Cyan 800
    public const string AccentHex = "#F9A825";    // Yellow 800

    public static MudTheme Instance { get; } = Create();

    private static MudTheme Create() => new()
    {
        PaletteLight = new PaletteLight
        {
            Primary = PrimaryHex,
            PrimaryDarken = PrimaryDarkHex,
            Secondary = SecondaryHex,
            AppbarBackground = "#FFFFFF",
            AppbarText = "#37474F",
            Background = "#F4F6F9",
            Surface = "#FFFFFF",
            DrawerBackground = "#FFFFFF",
            TextPrimary = "#263238",
            TextSecondary = "#546E7A",
            Divider = "#E0E5EA",
            Success = "#2E7D32",
            Info = "#0288D1",
            Warning = "#F9A825",
            Error = "#C62828",
        },
        PaletteDark = new PaletteDark
        {
            Primary = "#64B5F6",          // Blue 300 — readable on dark surfaces
            Secondary = "#4DD0E1",
            AppbarBackground = "#102A43",
            AppbarText = "#E3F2FD",
            Background = "#0F1923",
            Surface = "#16283A",
            DrawerBackground = "#102A43",
            TextPrimary = "#ECEFF1",
            TextSecondary = "#90A4AE",
            Divider = "#263B52",
            Success = "#66BB6A",
            Info = "#4FC3F7",
            Warning = "#FFD54F",
            Error = "#EF5350",
        },
        LayoutProperties = new LayoutProperties
        {
            DefaultBorderRadius = "8px",
            DrawerWidthLeft = "260px",
            DrawerWidthRight = "300px",
            AppbarHeight = "64px",
        },
        Typography = new MudBlazor.Typography
        {
            Default = new DefaultTypography { FontSize = "0.9375rem", LineHeight = "1.55" },
            H1 = new H1Typography { FontSize = "2.25rem", FontWeight = "600" },
            H2 = new H2Typography { FontSize = "1.875rem", FontWeight = "600" },
            H3 = new H3Typography { FontSize = "1.5rem", FontWeight = "600" },
            H4 = new H4Typography { FontSize = "1.25rem", FontWeight = "600" },
            H5 = new H5Typography { FontSize = "1.1rem", FontWeight = "600" },
            H6 = new H6Typography { FontSize = "1rem", FontWeight = "600" },
            Button = new ButtonTypography { FontWeight = "600", TextTransform = "none" },
            Caption = new CaptionTypography { FontSize = "0.8125rem" },
        },
    };
}
