using AwesomeAssertions;

namespace CvPlatform.Tests;

/// <summary>
/// Guards the friendly-message contract: components must funnel failures through
/// <see cref="CvPlatform.Web.ErrorHandling.IUiErrorReporter"/> instead of showing the raw domain or
/// exception message to the user.
/// </summary>
public class FriendlyErrorMessageGuardTests
{
    private static readonly string ComponentRoot = LocateComponentRoot();

    [Theory]
    [InlineData(".Error.Message")]
    [InlineData("ex.Message")]
    [InlineData("exception.Message")]
    public void No_component_shows_a_raw_developer_message(string forbidden)
    {
        var offenders = new List<string>();
        foreach (var (path, text) in Components())
        {
            if (IsBusinessLogicCheck(path))
                continue;

            var lines = text
                .Split('\n')
                .Select((line, index) => (line, index))
                .Where(part => part.line.Contains(forbidden, StringComparison.Ordinal))
                .Select(part => $"{Path.GetFileName(path)}:{part.index + 1} {part.line.Trim()}");

            offenders.AddRange(lines);
        }

        offenders.Should().BeEmpty(
            "components must report failures through IUiErrorReporter instead of rendering {0}", forbidden);
    }

    [Fact]
    public void Every_component_that_surfaces_a_failure_injects_the_reporter()
    {
        var offenders = Components()
            .Where(entry => entry.Text.Contains("catch (Exception")
                || entry.Text.Contains("Snackbar.Add(") && entry.Text.Contains("Severity.Error"))
            .Where(entry => !entry.Text.Contains("IUiErrorReporter"))
            .Select(entry => Path.GetFileName(entry.Path))
            .Distinct()
            .ToArray();

        offenders.Should().BeEmpty(
            "every component that handles a failure must inject IUiErrorReporter");
    }

    private static IEnumerable<(string Path, string Text)> Components()
    {
        if (!Directory.Exists(ComponentRoot))
            yield break;

        foreach (var path in Directory.GetFiles(ComponentRoot, "*.razor", SearchOption.AllDirectories))
            yield return (path, File.ReadAllText(path));
    }

    /// <summary>
    /// One rule still branches on the developer message instead of an error code. That line is
    /// business logic rather than user-facing output, so it is allow-listed instead of banned.
    /// </summary>
    private static bool IsBusinessLogicCheck(string path) =>
        Path.GetFileName(path).Equals("AttributeDefinitionDialog.razor", StringComparison.Ordinal);

    private static string LocateComponentRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "src", "CvPlatform.Web", "Components");
            if (Directory.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }

        return string.Empty;
    }
}