using ModRouter.Core.Compilation;
using ModRouter.Core.Definitions;
using ModRouter.Core.Guards;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ModRouter.Navigation;

/// <summary>
/// Runs the route guards parent-first, each level's guards in declaration order, one at a time. The first guard that
/// does not allow ends the stage: cancel and redirect stop the navigation, not-authorized and a failing guard are
/// recorded as the error of that level so the navigation still commits.
/// </summary>
internal sealed class GuardStage(RouteTableHolder holder, ILogger logger) : INavigationStage
{
    public async ValueTask<StageResult> RunAsync(RouteNavigationContext context)
    {
        if (context.MatchChain is not { } chain)
        {
            return StageResult.Continue;
        }

        for (var depth = 0; depth < chain.Matches.Count; depth++)
        {
            var level = chain.Matches[depth];

            foreach (var metadata in level.Route.Metadata.GetAll<RouteGuardMetadata>())
            {
                context.CancellationToken.ThrowIfCancellationRequested();

                RouteGuardResult result;

                try
                {
                    var guard = (IRouteGuard)ActivatorUtilities.GetServiceOrCreateInstance(context.Services, metadata.GuardType);
                    var guardContext = new RouteGuardContext(
                        level,
                        depth,
                        chain,
                        context.To,
                        context.FromState?.AbsoluteUri,
                        context.Location.Query,
                        context.Trigger == NavigationTrigger.InitialLoad,
                        context.Services,
                        context.CancellationToken);

                    result = await guard.CanActivateAsync(guardContext);
                }
                catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    return Fail(context, depth, metadata, $"The guard {metadata.GuardType.Name} threw: {exception.Message}", exception);
                }

                switch (result.Outcome)
                {
                    case RouteGuardOutcome.Allow:
                        continue;
                    case RouteGuardOutcome.Cancel:
                        return StageResult.Cancel;
                    case RouteGuardOutcome.NotAuthorized:
                        context.Error = new RouteError(RouteErrorKind.NotAuthorized, result.Message ?? "You do not have access to this page.", null, depth, metadata.GuardType);

                        return StageResult.Continue;
                    default:
                        return Redirect(context, depth, metadata, result);
                }
            }
        }

        return StageResult.Continue;
    }

    StageResult Redirect(RouteNavigationContext context, int depth, RouteGuardMetadata metadata, RouteGuardResult result)
    {
        if (result.Url is { } url)
        {
            return StageResult.Redirect(url, result.ReplaceHistory);
        }

        if (!holder.Table.TryGenerateUrl(result.RouteName!, result.Parameters, result.Query, result.Fragment, null, out var generated, out var error))
        {
            return Fail(context, depth, metadata, $"The guard {metadata.GuardType.Name} redirected to a route that cannot be built: {error.Message}", null);
        }

        // NavigationManager resolves a relative URI against the base, so drop the leading slash.
        var target = generated.TrimStart('/');

        return StageResult.Redirect(target.Length == 0 ? "./" : target, result.ReplaceHistory);
    }

    StageResult Fail(RouteNavigationContext context, int depth, RouteGuardMetadata metadata, string message, Exception? exception)
    {
        Diagnostics.RouterLog.GuardFailed(logger, exception, context.NavigationId, message, depth);
        context.Error = new RouteError(RouteErrorKind.GuardFailed, message, exception, depth, metadata.GuardType);

        return StageResult.Continue;
    }
}
