using CvPlatform.Web.Resources;
using Microsoft.Extensions.Localization;
using System.Globalization;

namespace CvPlatform.Tests;

/// <summary>
/// Minimal <see cref="IStringLocalizer{T}"/> for component tests. Derived stubs only supply the
/// keys the component under test actually renders.
/// </summary>
internal abstract class StubStringLocalizer : IStringLocalizer<SharedResource>
{
    protected abstract IReadOnlyDictionary<string, string> Catalog { get; }

    public LocalizedString this[string name] =>
        new(name, Catalog.TryGetValue(name, out var value) ? value : name);

    public LocalizedString this[string name, params object[] arguments] =>
        new(name, string.Format(CultureInfo.InvariantCulture, this[name].Value, arguments));

    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) =>
        Catalog.Select(pair => new LocalizedString(pair.Key, pair.Value));
}