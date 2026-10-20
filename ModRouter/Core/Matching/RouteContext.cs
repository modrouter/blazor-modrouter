using System.Text;
using ModRouter.Core.Definitions;

namespace ModRouter.Core.Matching;

/// <summary>One level of a matched route chain. Paths and the title are computed on first use.</summary>
public sealed class RouteContext
{
    readonly string[] _segments;
    readonly int _matchedCount;
    string? _matchedPath;
    string? _remainingPath;
    string? _title;

    internal RouteContext(
        RouteDefinition route,
        string template,
        RouteParameters parameters,
        RouteParameters inheritedParameters,
        string[] segments,
        int matchedCount,
        RouteContext? parent)
    {
        Route = route;
        Template = template;
        Parameters = parameters;
        InheritedParameters = inheritedParameters;
        Parent = parent;
        _segments = segments;
        _matchedCount = matchedCount;
    }

    /// <summary>The definition this level matched.</summary>
    public RouteDefinition Route { get; }

    /// <summary>The matched definition's name; null when it is unnamed.</summary>
    public string? RouteName => Route.Name;

    /// <summary>The full template of the URL prefix owned by this level and its ancestors, as matched (alias form for an alias).</summary>
    public string Template { get; }

    /// <summary>Parameters declared at this level only.</summary>
    public RouteParameters Parameters { get; }

    /// <summary>Parameters of every ancestor and this level; this level wins on a name clash.</summary>
    public RouteParameters InheritedParameters { get; }

    /// <summary>The normalized, re-encoded path up to and including this level; <c>/</c> when empty.</summary>
    public string MatchedPath => _matchedPath ??= Join(0, _matchedCount, emptyAsRoot: true);

    /// <summary>The normalized, re-encoded path below this level; empty when nothing remains.</summary>
    public string RemainingPath => _remainingPath ??= Join(_matchedCount, _segments.Length, emptyAsRoot: false);

    /// <summary>The title factory result (given <see cref="InheritedParameters"/>), else the static title, else empty.</summary>
    public string Title => _title ??= Route.Title?.Invoke(InheritedParameters) ?? Route.StaticTitle ?? string.Empty;

    /// <summary>The matched definition's metadata, the same instance as <c>Route.Metadata</c>.</summary>
    public RouteMetadataCollection Metadata => Route.Metadata;

    /// <summary>The level above this one in the chain; null for the root level.</summary>
    public RouteContext? Parent { get; }

    /// <summary>
    /// The level below this one in the chain; null for the leaf. Set by the matcher while it builds the chain.
    /// </summary>
    public RouteContext? Child { get; internal set; }

    string Join(int from, int to, bool emptyAsRoot)
    {
        if (to <= from)
        {
            return emptyAsRoot ? "/" : string.Empty;
        }

        var builder = new StringBuilder();

        for (var i = from; i < to; i++)
        {
            builder.Append('/').Append(Uri.EscapeDataString(_segments[i]));
        }

        return builder.ToString();
    }
}
