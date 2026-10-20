using System.Diagnostics.CodeAnalysis;
using ModRouter.Core.Definitions;
using ModRouter.Core.Generation;

namespace ModRouter.Core.Compilation;

public sealed partial class CompiledRouteTable : INamedRouteRegistry
{
    /// <inheritdoc />
    public string GenerateUrl(
        string routeName,
        IReadOnlyDictionary<string, object?>? parameters = null,
        IReadOnlyDictionary<string, object?>? query = null,
        string? fragment = null,
        IReadOnlyDictionary<string, object?>? ambientValues = null)
    {
        if (!TryGenerateUrl(routeName, parameters, query, fragment, ambientValues, out var url, out var error))
        {
            throw new RouteUrlGenerationException(error);
        }

        return url;
    }

    /// <summary>
    /// Generates from the route's canonical template. When the supplied values cannot build it, the route's own aliases
    /// (under the canonical ancestor path, in declaration order) are tried and the first they satisfy is used: a
    /// component with <c>@page "/org-chart"</c> and <c>@page "/org-chart/{id:int}"</c> generates both URLs. A route whose
    /// aliases redirect to the canonical URL (<c>CanonicalAlias()</c>) never falls back, and when no template is satisfied
    /// the canonical template's error is reported.
    /// </summary>
    public bool TryGenerateUrl(
        string routeName,
        IReadOnlyDictionary<string, object?>? parameters,
        IReadOnlyDictionary<string, object?>? query,
        string? fragment,
        IReadOnlyDictionary<string, object?>? ambientValues,
        [NotNullWhen(true)] out string? url,
        [NotNullWhen(false)] out RouteUrlGenerationError? error)
    {
        ArgumentNullException.ThrowIfNull(routeName);

        if (!TryGetNamed(routeName, out var entry))
        {
            url = null;
            error = new RouteUrlGenerationError(UrlGenerationErrorCode.UnknownRoute, UnknownRouteMessage(routeName), routeName);

            return false;
        }

        if (UrlGenerator.TryGenerate(entry, parameters, query, fragment, ambientValues, out url, out error))
        {
            return true;
        }

        if (RedirectsAliases(entry))
        {
            return false;
        }

        foreach (var alias in OwnAliases(entry))
        {
            if (UrlGenerator.TryGenerate(alias, parameters, query, fragment, ambientValues, out url, out _))
            {
                error = null;

                return true;
            }
        }

        url = null;

        return false;
    }

    /// <summary>True when an alias URL of this route redirects to the canonical one (the last level that declares a behavior decides).</summary>
    static bool RedirectsAliases(CompiledRoute entry)
        => entry.Chain.Select(level => level.Metadata.Get<RouteAliasMetadata>()).LastOrDefault(m => m is not null)?.Behavior == AliasBehavior.RedirectToCanonical;

    /// <summary>The alias entries of <paramref name="canonical"/>'s definition whose ancestor levels take the canonical path, in declaration order.</summary>
    IEnumerable<CompiledRoute> OwnAliases(CompiledRoute canonical)
    {
        var ancestors = canonical.LevelTemplates.Length - 1;

        return Routes.Where(entry => entry.IsAlias
            && ReferenceEquals(entry.Definition, canonical.Definition)
            && entry.LevelTemplates.Take(ancestors).SequenceEqual(canonical.LevelTemplates.Take(ancestors), StringComparer.Ordinal));
    }
}
