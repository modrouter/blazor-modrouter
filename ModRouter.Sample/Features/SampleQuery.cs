using Microsoft.AspNetCore.Components;

namespace ModRouter.Sample.Features;

/// <summary>
/// Reads the sample's query-string switches (<c>?scroller=1</c>, <c>?slownav=1</c>) from the current address, so a component
/// needs no JavaScript for them. (<c>?mode=</c> is read in <c>Program.cs</c> before any component exists, see <see cref="SampleJs"/>.)
/// </summary>
static class SampleQuery
{
    /// <summary>True when the current address has the query key <paramref name="key"/>, with or without a value.</summary>
    public static bool Has(NavigationManager navigation, string key)
    {
        var query = new Uri(navigation.Uri).Query.TrimStart('?');

        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var name = pair.Split('=', 2)[0];

            if (string.Equals(Uri.UnescapeDataString(name.Replace('+', ' ')), key, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
