using ModRouter.Components;
using ModRouter.Core.Compilation;
using ModRouter.Core.Matching;
using Microsoft.Extensions.Logging;

namespace ModRouter.Navigation;

/// <summary>What a stage tells the coordinator. Create redirects with <see cref="Redirect"/>.</summary>
public sealed class StageResult
{
    StageResult(bool isCancel, string? target, bool? replaceHistory)
    {
        IsCancel = isCancel;
        RedirectTarget = target;
        ReplaceHistory = replaceHistory;
    }

    /// <summary>Carry on with the next stage.</summary>
    public static StageResult Continue { get; } = new(false, null, null);

    /// <summary>Stop: the platform keeps the old page and URL.</summary>
    public static StageResult Cancel { get; } = new(true, null, null);

    /// <summary>
    /// Stop and navigate to <paramref name="target"/> instead (a URL, base-relative or absolute). The history entry is
    /// pushed unless this is the initial load or <paramref name="replaceHistory"/> says otherwise.
    /// </summary>
    public static StageResult Redirect(string target, bool? replaceHistory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target);

        return new StageResult(false, target, replaceHistory);
    }

    /// <summary>True for <see cref="Cancel"/>: the navigation stops and the platform keeps the old page and URL.</summary>
    public bool IsCancel { get; }

    /// <summary>True for a result made by <see cref="Redirect"/>.</summary>
    public bool IsRedirect => RedirectTarget is not null;

    /// <summary>The URL a redirect result sends the navigation to; null unless <see cref="IsRedirect"/>.</summary>
    public string? RedirectTarget { get; }

    /// <summary>
    /// The history behavior a redirect asked for: true replaces the current entry, false pushes one, null uses the
    /// default (a push, except on the initial load).
    /// </summary>
    public bool? ReplaceHistory { get; }
}

/// <summary>
/// The data a navigation carries through its stages. Built-in stages fill <see cref="Location"/>,
/// <see cref="MatchChain"/>, <see cref="Failure"/> and <see cref="Error"/>.
/// </summary>
public sealed class RouteNavigationContext
{
    /// <summary>
    /// Identifies this navigation (the number later committed as <see cref="RouteState.NavigationId"/>); each redirect
    /// hop is a new navigation with its own.
    /// </summary>
    public required long NavigationId { get; init; }

    /// <summary>The absolute URI of the previously committed state; null before the first navigation commits.</summary>
    public string? From { get; init; }

    /// <summary>The absolute URI being navigated to, query and fragment included.</summary>
    public required string To { get; init; }

    /// <summary>
    /// How the navigation started. A browser back or forward traversal is <see cref="NavigationTrigger.Programmatic"/>
    /// here; only its committed state says <see cref="NavigationTrigger.HistoryRestore"/>.
    /// </summary>
    public required NavigationTrigger Trigger { get; init; }

    /// <summary>
    /// Canceled when a newer navigation supersedes this one or it is canceled. A stage must pass it to everything it
    /// awaits.
    /// </summary>
    public required CancellationToken CancellationToken { get; init; }

    /// <summary>The navigation scope's services, for stages that resolve guards, loaders or anything else per navigation.</summary>
    public required IServiceProvider Services { get; init; }

    /// <summary>
    /// The state string the target's history entry carries (set by <c>historyState</c> on the platform's
    /// <c>NavigateTo</c>, or restored on back and forward), or null.
    /// </summary>
    public string? HistoryEntryState { get; init; }

    /// <summary>The previously committed state (the one the user is looking at), or null before the first commit.</summary>
    public RouteState? FromState { get; init; }

    /// <summary>The absolute URIs that already redirected to this navigation, oldest first.</summary>
    public IReadOnlyList<string> RedirectChain { get; init; } = [];

    /// <summary>When set, the loader of this chain level runs again even if its reload policy would reuse the data.</summary>
    public int? ReloadLevel { get; init; }

    /// <summary>When true, every loader runs again.</summary>
    public bool ReloadAll { get; init; }

    /// <summary>The chain levels whose loaders run again (a background refresh of stale cached levels); null for none.</summary>
    internal IReadOnlyCollection<int>? ReloadLevels { get; init; }

    /// <summary>True when the loader of chain level <paramref name="depth"/> is to run again whatever its reload policy and cache say.</summary>
    internal bool Reloads(int depth) => ReloadAll || ReloadLevel == depth || ReloadLevels?.Contains(depth) == true;

    /// <summary>
    /// The target as a path, query and fragment relative to the application base. Set by the built-in normalize stage;
    /// a target outside the base leaves <c>/</c> here and sets <see cref="Failure"/> to
    /// <see cref="RouteFailure.NotFound"/>.
    /// </summary>
    public BaseRelativeLocation Location { get; internal set; }

    /// <summary>
    /// The matched levels. Set by the built-in recognize stage; null while nothing has matched, and reset to null when
    /// parameter binding fails.
    /// </summary>
    public RouteMatchChain? MatchChain { get; internal set; }

    /// <summary>
    /// Why the target has no chain, set by the built-in stages; <see cref="RouteFailure.None"/> until one fails.
    /// </summary>
    public RouteFailure Failure { get; internal set; }

    /// <summary>The error the navigation will commit with, once a guard or loader recorded one.</summary>
    public RouteError? Error { get; internal set; }

    /// <summary>What each chain level's loader produced (null entries: no loader, or not loaded). Set by the loader stage.</summary>
    internal RouteLevelData?[] LevelData { get; set; } = [];

    /// <summary>True for a preload: only cacheable levels load, nothing is restored, and a stale entry counts as a miss.</summary>
    internal bool IsPreload { get; init; }

    /// <summary>The depths whose data came from a stale cache entry and need a background refresh once this navigation commits.</summary>
    internal System.Collections.Concurrent.ConcurrentBag<int> StaleLevels { get; } = [];
}

/// <summary>
/// One step of a navigation. Stages run in order after the built-in normalize, recognize, bind, redirect, guard and
/// loader stages, in DI registration order. A stage must honor <see cref="RouteNavigationContext.CancellationToken"/>.
/// </summary>
public interface INavigationStage
{
    /// <summary>
    /// Runs the stage for <paramref name="context"/> and decides how the navigation goes on:
    /// <see cref="StageResult.Continue"/>, <see cref="StageResult.Cancel"/> or <see cref="StageResult.Redirect"/>. An
    /// exception other than the navigation's own cancellation fails the navigation (it is logged and recorded with
    /// outcome Failed).
    /// </summary>
    ValueTask<StageResult> RunAsync(RouteNavigationContext context);

    /// <summary>
    /// False (the default) skips the stage when the navigation already failed (not found, binding failure) or recorded
    /// an error. Return true for a stage that must see those too.
    /// </summary>
    bool RunsOnFailure => false;
}

internal sealed class NormalizeStage(INavigationPlatformAdapter adapter) : INavigationStage
{
    public bool RunsOnFailure => true;

    public ValueTask<StageResult> RunAsync(RouteNavigationContext context)
    {
        if (BaseUriHelper.TryGetBaseRelative(adapter.BaseUri, context.To, out var location))
        {
            context.Location = location;
        }
        else
        {
            context.Location = new BaseRelativeLocation("/", string.Empty, null);
            context.Failure = RouteFailure.NotFound;
        }

        return ValueTask.FromResult(StageResult.Continue);
    }
}

internal sealed class RecognizeStage(RouteTableHolder holder) : INavigationStage
{
    public bool RunsOnFailure => true;

    public ValueTask<StageResult> RunAsync(RouteNavigationContext context)
    {
        if (context.Failure == RouteFailure.None)
        {
            var result = holder.Table.Match(context.Location.Path);

            if (result.IsMatch)
            {
                context.MatchChain = result.Chain;
            }
            else
            {
                context.Failure = result.Failure == RouteMatchFailure.MalformedPath ? RouteFailure.MalformedPath : RouteFailure.NotFound;
            }
        }

        return ValueTask.FromResult(StageResult.Continue);
    }
}

internal sealed class BindStage(RouteComponentBinder binder, ILogger logger) : INavigationStage
{
    public bool RunsOnFailure => true;

    public ValueTask<StageResult> RunAsync(RouteNavigationContext context)
    {
        if (context.MatchChain is { } chain)
        {
            foreach (var level in chain.Matches)
            {
                if (!binder.TryBind(level, out _, out var error))
                {
                    Diagnostics.RouterLog.ParameterBindingFailed(logger, context.NavigationId, context.Location.Path, error);
                    context.MatchChain = null;
                    context.Failure = RouteFailure.ParameterBindingFailed;

                    break;
                }
            }
        }

        return ValueTask.FromResult(StageResult.Continue);
    }
}
