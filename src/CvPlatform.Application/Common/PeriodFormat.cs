namespace CvPlatform.Application.Common;

/// <summary>Formats an optional period range for display and export surfaces.</summary>
public static class PeriodFormat
{
    public static string Format(DateOnly? start, DateOnly? end) => (start, end) switch
    {
        ({ } s, { } e) => $"{s:yyyy/MM} – {e:yyyy/MM}",
        ({ } s, null) => $"{s:yyyy/MM} – …",
        (null, { } e) => $"… – {e:yyyy/MM}",
        _ => "—",
    };
}

