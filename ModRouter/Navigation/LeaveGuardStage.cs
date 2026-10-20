using ModRouter.Components;
using ModRouter.Core.Definitions;
using ModRouter.Core.Guards;
using ModRouter.Core.Matching;
using Microsoft.Extensions.DependencyInjection;

namespace ModRouter.Navigation;

/// <summary>
/// Asks the levels on screen whether the navigation may take them off screen: the component of each level that implements
/// <see cref="IRouteNavigationLock"/>, then the route's declared leave guards. Deepest level first, each level's declared
/// guards in declaration order, one at a time. It runs before the redirect rules and the enter guards, so a page can
/// protect its unsaved state whatever the target turns out to do. The first answer that does not allow ends the stage.
/// A level whose route stays but whose route parameters change is asked of its component only.
/// </summary>
internal sealed class LeaveGuardStage : INavigationStage
{
    /// <summary>Runs for a target that matched nothing too: every level on screen is left then.</summary>
    public bool RunsOnFailure => true;

    public async ValueTask<StageResult> RunAsync(RouteNavigationContext context)
    {
        // Nothing is on screen before the first commit, a reload leaves nothing, and a redirect hop is part of a
        // navigation whose first hop already asked.
        if (context.Trigger is NavigationTrigger.InitialLoad or NavigationTrigger.Reload or NavigationTrigger.Redirect
            || context.FromState is not { Chain: { } shown } from)
        {
            return StageResult.Continue;
        }

        var registry = context.Services.GetService<RouteComponentRegistry>();

        // A level at or below a failed one showed its error, not its page: there is nothing to protect there.
        for (var depth = (from.FailedLevel ?? shown.Matches.Count) - 1; depth >= 0; depth--)
        {
            var level = shown.Matches[depth];
            var target = TargetAt(context, depth, level.Route);
            var isLeft = target is null;
            var parametersChange = target is not null && target.InheritedParameters != level.InheritedParameters;

            if (!isLeft && !parametersChange)
            {
                continue;
            }

            // The component first: the code closest to the state at risk gets the first say.
            if (registry?.Get(depth) is IRouteNavigationLock locked
                && Verdict(await locked.CanLeaveAsync(Ask(context, level, depth, shown, parametersChange))) is { } blocked)
            {
                return blocked;
            }

            if (!isLeft)
            {
                continue;
            }

            foreach (var metadata in level.Route.Metadata.GetAll<RouteLeaveGuardMetadata>())
            {
                context.CancellationToken.ThrowIfCancellationRequested();

                var guard = (IRouteLeaveGuard)ActivatorUtilities.GetServiceOrCreateInstance(context.Services, metadata.GuardType);

                if (Verdict(await guard.CanDeactivateAsync(Ask(context, level, depth, shown, isParameterChange: false))) is { } stop)
                {
                    return stop;
                }
            }
        }

        return StageResult.Continue;
    }

    /// <summary>The level the target shows at <paramref name="depth"/> when it is the same route, else null: the level is left.</summary>
    static RouteContext? TargetAt(RouteNavigationContext context, int depth, RouteDefinition route)
        => context.MatchChain is { } target
            && depth < target.Matches.Count
            && ReferenceEquals(target.Matches[depth].Route, route)
                ? target.Matches[depth]
                : null;

    static RouteLeaveContext Ask(RouteNavigationContext context, RouteContext level, int depth, RouteMatchChain shown, bool isParameterChange)
    {
        context.CancellationToken.ThrowIfCancellationRequested();

        return new RouteLeaveContext(level, depth, shown, context.To, context.Location.Query, context.Services, context.CancellationToken, isParameterChange);
    }

    /// <summary>What the stage does with an answer: null to go on, else the result that ends it.</summary>
    static StageResult? Verdict(RouteLeaveResult result) => result.Outcome switch
    {
        RouteLeaveOutcome.Allow => null,
        RouteLeaveOutcome.Cancel => StageResult.Cancel,
        _ => StageResult.Redirect(result.Url!, result.ReplaceHistory),
    };
}
