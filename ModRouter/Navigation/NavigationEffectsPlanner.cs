using ModRouter.Core.Definitions;

namespace ModRouter.Navigation;

/// <summary>
/// Decides, from a committed state, what the router does in the browser afterwards. A pure function of the state's
/// trigger and title, the options, the route's metadata and the navigation's own options, so the whole table is
/// tested without a browser:
///
/// <list type="table">
/// <listheader><term>Trigger</term><description>Title / announce / focus / scroll</description></listheader>
/// <item><term>InitialLoad</term><description>title; fragment scroll only</description></item>
/// <item><term>Link, Programmatic, Redirect</term><description>title, announce, focus; scroll to the fragment or the top</description></item>
/// <item><term>HistoryRestore, BrowserBack, BrowserForward</term><description>title; restore the saved scroll position</description></item>
/// <item><term>Reload</term><description>nothing</description></item>
/// </list>
///
/// A redirect chain that started at the initial load or at a back/forward traversal takes that origin's row (the user
/// did not navigate; the redirect target is simply the page that load or traversal ends on). A redirect that started
/// anywhere else (a link, <c>NavigateTo</c>, a retry whose guard now sends the user elsewhere) is a new page.
/// </summary>
internal static class NavigationEffectsPlanner
{
    public static NavigationEffects Plan(RouteState state, ModRouterOptions options)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(options);

        // Clamped by hand: a double beyond the int range converts to int.MinValue before .NET 9 (it saturates from .NET 9 on).
        var timeout = (int)Math.Clamp(options.Focus.Timeout.TotalMilliseconds, 0, int.MaxValue);
        var requested = state.Options;

        switch (EffectiveTrigger(state))
        {
            case NavigationTrigger.Reload:
                return NavigationEffects.None;

            case NavigationTrigger.InitialLoad:
                return new NavigationEffects(state.Title, null, null, FragmentScroll(state, options, timeout));

            case NavigationTrigger.HistoryRestore:
            case NavigationTrigger.BrowserBack:
            case NavigationTrigger.BrowserForward:
                var restore = options.Scroll is { Enabled: true, Restore: true }
                    ? new ScrollEffect(ScrollMode.Restore, null, options.Scroll.Container, timeout)
                    : null;

                return new NavigationEffects(state.Title, null, null, restore);

            default:
                return new NavigationEffects(
                    state.Title,
                    Announcement(state, options, requested),
                    Focus(state, options, requested, timeout),
                    options.Scroll.Enabled && (requested?.Scroll ?? true)
                        ? FragmentScroll(state, options, timeout) ?? new ScrollEffect(ScrollMode.Top, null, options.Scroll.Container, timeout)
                        : null);
        }
    }

    /// <summary>The trigger whose row applies: a redirect's origin when that origin was not a navigation the user made.</summary>
    static NavigationTrigger EffectiveTrigger(RouteState state)
        => state.Trigger == NavigationTrigger.Redirect
            && state.RedirectOrigin is NavigationTrigger.InitialLoad or NavigationTrigger.HistoryRestore or NavigationTrigger.BrowserBack or NavigationTrigger.BrowserForward
            ? state.RedirectOrigin.Value
            : state.Trigger;

    static string? Announcement(RouteState state, ModRouterOptions options, ModNavigationOptions? requested)
    {
        if (!options.Announce.Enabled || !(requested?.Announce ?? true) || state.Title is null)
        {
            return null;
        }

        return Deepest<RouteAnnounceMetadata>(state) is { Enabled: false } ? null : state.Title;
    }

    static FocusEffect? Focus(RouteState state, ModRouterOptions options, ModNavigationOptions? requested, int timeout)
    {
        if (!options.Focus.Enabled || !(requested?.Focus ?? true))
        {
            return null;
        }

        var selector = Deepest<RouteFocusMetadata>(state)?.Selector ?? options.Focus.Selector;

        return new FocusEffect(selector, timeout);
    }

    static ScrollEffect? FragmentScroll(RouteState state, ModRouterOptions options, int timeout)
        => options.Scroll.Enabled && !string.IsNullOrEmpty(state.Fragment)
            ? new ScrollEffect(ScrollMode.Fragment, state.Fragment, options.Scroll.Container, timeout)
            : null;

    /// <summary>The metadata of the deepest chain level that declares one: a child's choice beats its parent's.</summary>
    static T? Deepest<T>(RouteState state)
        where T : class
    {
        if (state.Chain is not { } chain)
        {
            return null;
        }

        for (var depth = chain.Matches.Count - 1; depth >= 0; depth--)
        {
            if (chain.Matches[depth].Metadata.Get<T>() is { } found)
            {
                return found;
            }
        }

        return null;
    }
}
