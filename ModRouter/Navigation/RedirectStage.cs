using ModRouter.Core.Compilation;
using ModRouter.Core.Definitions;
using ModRouter.Core.Generation;
using ModRouter.Core.Syntax;
using ModRouter.Diagnostics;
using Microsoft.Extensions.Logging;

namespace ModRouter.Navigation;

/// <summary>
/// Turns a matched redirect rule (<c>RouteMap.Redirect</c>) or an alias hit with canonical redirect enabled into a
/// redirect. Runs before guards: a URL that only forwards must never be guarded or loaded.
/// </summary>
internal sealed class RedirectStage(RouteTableHolder holder, AliasBehavior defaultAliasBehavior, ILogger logger) : INavigationStage
{
    public ValueTask<StageResult> RunAsync(RouteNavigationContext context)
    {
        if (context.MatchChain is not { } chain)
        {
            return ValueTask.FromResult(StageResult.Continue);
        }

        var leaf = chain.Leaf;
        var table = holder.Table;

        if (leaf.Route.Metadata.Get<RouteRedirectMetadata>() is { } rule)
        {
            return ValueTask.FromResult(Rule(context, leaf.InheritedParameters.Values, rule, table));
        }

        if (chain.MatchedViaAlias && WantsCanonical(chain.Matches.Select(m => m.Route), out var fromDefault))
        {
            return ValueTask.FromResult(Canonical(context, leaf.Route, leaf.InheritedParameters.Values, table, fromDefault));
        }

        return ValueTask.FromResult(StageResult.Continue);
    }

    static StageResult Rule(RouteNavigationContext context, IReadOnlyDictionary<string, object?> values, RouteRedirectMetadata rule, CompiledRouteTable table)
    {
        table.TryGetNamed(rule.ToRoute, out var target);
        var declared = target!.Segments.Where(s => s.IsParameter).Select(s => s.Text).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var forwarded = values.Where(pair => declared.Contains(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);

        return Forward(context, table.GenerateUrl(rule.ToRoute, forwarded), rule.PreserveQuery, rule.PreserveFragment);
    }

    StageResult Canonical(RouteNavigationContext context, RouteDefinition leaf, IReadOnlyDictionary<string, object?> values, CompiledRouteTable table, bool fromDefault)
    {
        var entry = table.FindCanonical(leaf);

        if (!UrlGenerator.TryGenerate(entry, values, null, null, null, out var url, out var error))
        {
            if (fromDefault)
            {
                // The router-wide default only redirects an alias whose values can build the canonical URL (the alias
                // has parameters the canonical template lacks or omits, or a constraint rejects a value): render it where it matched.
                RouterLog.AliasRenderedInPlace(logger, context.Location.Path, entry.CanonicalTemplate);

                return StageResult.Continue;
            }

            // A declared CanonicalAlias() keeps the failure. RT0015 checks that the alias names the canonical template's
            // parameters, so only a value-level mismatch (a constraint the canonical template rejects) can reach it.
            throw new InvalidOperationException($"The canonical redirect of '{entry.CanonicalTemplate}' could not build its URL: {error.Message}");
        }

        return Forward(context, url, true, true);
    }

    /// <summary>True when the alias should redirect; <paramref name="fromDefault"/> says the behavior is the router-wide default, not declared on a route.</summary>
    bool WantsCanonical(IEnumerable<RouteDefinition> levels, out bool fromDefault)
    {
        var declared = levels.Select(l => l.Metadata.Get<RouteAliasMetadata>()).LastOrDefault(m => m is not null);
        fromDefault = declared is null;

        return (declared?.Behavior ?? defaultAliasBehavior) == AliasBehavior.RedirectToCanonical;
    }

    static StageResult Forward(RouteNavigationContext context, string url, bool preserveQuery, bool preserveFragment)
    {
        // NavigationManager resolves a relative URI against the base, so drop the leading slash.
        var target = url.TrimStart('/') + (preserveQuery ? context.Location.Query : string.Empty);

        if (preserveFragment && context.Location.Fragment is { Length: > 0 } fragment)
        {
            target += "#" + fragment;
        }

        return StageResult.Redirect(target.Length == 0 ? "./" : target);
    }
}
