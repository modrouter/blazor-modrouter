using ModRouter.Navigation;
using Microsoft.Extensions.Logging;

namespace ModRouter.Diagnostics;

/// <summary>
/// Every log event of the router, source-generated: a stable <see cref="EventId"/> and a fixed level per event, named
/// placeholders, and no allocation when the level is disabled. Each method takes the caller's logger, so the category
/// stays the class that logs. Ids by area: 1000 lifecycle, 1100 stages, 1200 guards and loaders, 1300 redirects, 1400
/// configuration and markup, 1500 re-check and authentication, 1600 <c>OnNavigateAsync</c> and compatibility, 1700
/// effects and JavaScript, 1800 subscribers and diagnostics. The observability page's "Log events" table (docs/observability.md) lists them; an id, once
/// published, is never reused for another event.
/// </summary>
internal static partial class RouterLog
{
    // ---- 1000 lifecycle -------------------------------------------------------------------------------------------

    [LoggerMessage(EventId = 1000, EventName = nameof(NavigationRequested), Level = LogLevel.Debug, Message = "Navigation {NavigationId} requested to {Target} ({Trigger}).")]
    public static partial void NavigationRequested(ILogger logger, long navigationId, string target, NavigationTrigger trigger);

    [LoggerMessage(EventId = 1001, EventName = nameof(NavigationCommitted), Level = LogLevel.Debug, Message = "Navigation {NavigationId} committed.")]
    public static partial void NavigationCommitted(ILogger logger, long navigationId);

    [LoggerMessage(EventId = 1002, EventName = nameof(NavigationSuperseded), Level = LogLevel.Debug, Message = "Navigation {NavigationId} was superseded.")]
    public static partial void NavigationSuperseded(ILogger logger, long navigationId);

    [LoggerMessage(EventId = 1003, EventName = nameof(NavigationCanceled), Level = LogLevel.Debug, Message = "Navigation {NavigationId} was canceled.")]
    public static partial void NavigationCanceled(ILogger logger, long navigationId);

    [LoggerMessage(EventId = 1004, EventName = nameof(NavigationFailed), Level = LogLevel.Error, Message = "Navigation {NavigationId} to {Target} failed.")]
    public static partial void NavigationFailed(ILogger logger, Exception exception, long navigationId, string target);

    [LoggerMessage(EventId = 1005, EventName = nameof(FallbackNavigationFailed), Level = LogLevel.Error, Message = "Navigation to {Location} failed after the location had already changed.")]
    public static partial void FallbackNavigationFailed(ILogger logger, Exception exception, string location);

    [LoggerMessage(EventId = 1006, EventName = nameof(LocationChangedWithoutPreparedNavigation), Level = LogLevel.Warning, Message = "Location changed to {Location} without a prepared navigation; running the pipeline after the fact.")]
    public static partial void LocationChangedWithoutPreparedNavigation(ILogger logger, string location);

    [LoggerMessage(EventId = 1007, EventName = nameof(UnparseableLocation), Level = LogLevel.Warning, Message = "The platform reported a location the router cannot parse: {Location}; it is ignored.")]
    public static partial void UnparseableLocation(ILogger logger, string location);

    // ---- 1100 stages ----------------------------------------------------------------------------------------------

    [LoggerMessage(EventId = 1100, EventName = nameof(NavigationCanceledByStage), Level = LogLevel.Debug, Message = "Navigation {NavigationId} was canceled by {Stage}.")]
    public static partial void NavigationCanceledByStage(ILogger logger, long navigationId, string stage);

    [LoggerMessage(EventId = 1101, EventName = nameof(ParameterBindingFailed), Level = LogLevel.Warning, Message = "Navigation {NavigationId} to {Path} failed parameter binding: {Error}")]
    public static partial void ParameterBindingFailed(ILogger logger, long navigationId, string path, string? error);

    [LoggerMessage(EventId = 1102, EventName = nameof(AliasRenderedInPlace), Level = LogLevel.Debug, Message = "The alias {Path} cannot build the canonical URL {Template}; it renders in place.")]
    public static partial void AliasRenderedInPlace(ILogger logger, string path, string template);

    // ---- 1200 guards and loaders ----------------------------------------------------------------------------------

    [LoggerMessage(EventId = 1200, EventName = nameof(GuardFailed), Level = LogLevel.Error, Message = "Navigation {NavigationId}: {Message} (level {Level}).")]
    public static partial void GuardFailed(ILogger logger, Exception? exception, long navigationId, string message, int level);

    [LoggerMessage(EventId = 1201, EventName = nameof(LoaderFailed), Level = LogLevel.Error, Message = "Navigation {NavigationId}: the loader of level {Level} failed.")]
    public static partial void LoaderFailed(ILogger logger, Exception exception, long navigationId, int level);

    // ---- 1300 redirects -------------------------------------------------------------------------------------------

    [LoggerMessage(EventId = 1300, EventName = nameof(RedirectLoop), Level = LogLevel.Error, Message = "Navigation {NavigationId} was redirected in a loop: {Path}.")]
    public static partial void RedirectLoop(ILogger logger, long navigationId, string path);

    // ---- 1400 configuration and markup (1410: the outlet warning) -------------------------------------------------

    [LoggerMessage(EventId = 1400, EventName = nameof(MarkupRoutesCompileFailed), Level = LogLevel.Error, Message = "The markup <Routes> entries could not be compiled; the router shows the error instead of a page.")]
    public static partial void MarkupRoutesCompileFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1401, EventName = nameof(MarkupEntryParametersChanged), Level = LogLevel.Debug, Message = "The parameters of a markup route entry changed after it was registered and are ignored: {Registered} is now {Current}.")]
    public static partial void MarkupEntryParametersChanged(ILogger logger, string registered, string current);

    [LoggerMessage(EventId = 1410, EventName = nameof(OutletNotRendered), Level = LogLevel.Warning, Message = "The route {RouteName} ({Template}) has no outlet: its component {Component} rendered no ModRouterOutlet while it was shown, so the child it matched at depth {Depth} was never displayed.")]
    public static partial void OutletNotRendered(ILogger logger, string? routeName, string template, string component, int depth);

    // ---- 1500 re-check and authentication -------------------------------------------------------------------------

    [LoggerMessage(EventId = 1500, EventName = nameof(AuthorizationRechecked), Level = LogLevel.Debug, Message = "The authentication state changed: the authorization guards of {Path} say {After}; the committed state shows {Before}.")]
    public static partial void AuthorizationRechecked(ILogger logger, string path, string after, string before);

    // ---- 1600 OnNavigateAsync and compatibility -------------------------------------------------------------------

    [LoggerMessage(EventId = 1600, EventName = nameof(OnNavigateCanceled), Level = LogLevel.Debug, Message = "Navigation {NavigationId}: OnNavigateAsync was canceled by a newer navigation.")]
    public static partial void OnNavigateCanceled(ILogger logger, long navigationId);

    [LoggerMessage(EventId = 1601, EventName = nameof(OnNavigateThrew), Level = LogLevel.Error, Message = "Navigation {NavigationId}: OnNavigateAsync threw; the navigation commits anyway.")]
    public static partial void OnNavigateThrew(ILogger logger, Exception exception, long navigationId);

    [LoggerMessage(EventId = 1602, EventName = nameof(CompatAssembliesChanged), Level = LogLevel.Warning, Message = "The Router's AppAssembly/AdditionalAssemblies changed after the route table was compiled ({Before} -> {After}); the change is ignored. Add every assembly in OnNavigateAsync before the first navigation.")]
    public static partial void CompatAssembliesChanged(ILogger logger, string before, string after);

    // ---- 1700 effects and JavaScript ------------------------------------------------------------------------------

    [LoggerMessage(EventId = 1700, EventName = nameof(EffectsFailed), Level = LogLevel.Warning, Message = "The router could not run its after-navigation effects (title, focus, scroll).")]
    public static partial void EffectsFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1701, EventName = nameof(JsModuleLoadFailed), Level = LogLevel.Warning, Message = "The router's JavaScript module could not be loaded; title, focus and scroll effects are skipped.")]
    public static partial void JsModuleLoadFailed(ILogger logger, Exception exception);

    // ---- 1800 subscribers and diagnostics -------------------------------------------------------------------------

    [LoggerMessage(EventId = 1800, EventName = nameof(StateSubscriberThrew), Level = LogLevel.Error, Message = "A router state subscriber threw and was skipped.")]
    public static partial void StateSubscriberThrew(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1801, EventName = nameof(DiagnosticsSubscriberThrew), Level = LogLevel.Error, Message = "A router diagnostics subscriber threw and was skipped.")]
    public static partial void DiagnosticsSubscriberThrew(ILogger logger, Exception exception);

    // ---- a logger that never throws -------------------------------------------------------------------------------

    /// <summary>
    /// <paramref name="logger"/> behind a guard that swallows what a logging provider throws (the framework's aggregate
    /// logger rethrows its providers' failures): logging must never change what a navigation does.
    /// </summary>
    public static ILogger Guard(ILogger logger) => logger as GuardedLogger ?? new GuardedLogger(logger);

    sealed class GuardedLogger(ILogger inner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            try
            {
                return inner.BeginScope(state);
            }
            catch (Exception)
            {
                return null;
            }
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            try
            {
                return inner.IsEnabled(logLevel);
            }
            catch (Exception)
            {
                return false;
            }
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            try
            {
                inner.Log(logLevel, eventId, state, exception, formatter);
            }
            catch (Exception)
            {
                // A failing provider has nowhere to report to; the navigation goes on.
            }
        }
    }
}
