using ModRouter.Components;
using ModRouter.Core.Definitions;
using ModRouter.Core.Loading;
using ModRouter.Core.Matching;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ModRouter.Navigation;

/// <summary>
/// Runs the route loaders. Each level either reuses the previous committed level's data (by its reload policy) or
/// loads; every load starts at once so an unchanged level never waits for a changed one, and the results are awaited
/// in level order. The first failure at level L is recorded as that level's error and cancels the loaders of deeper
/// levels. Levels at or below a guard error are not loaded.
/// </summary>
internal sealed class LoaderStage(ILogger logger) : INavigationStage
{
    public bool RunsOnFailure => true;

    public async ValueTask<StageResult> RunAsync(RouteNavigationContext context)
    {
        if (context.Failure != RouteFailure.None || context.MatchChain is not { } chain)
        {
            return StageResult.Continue;
        }

        var limit = context.Error?.Level ?? chain.Matches.Count;
        var data = new RouteLevelData?[chain.Matches.Count];
        var reused = new bool[chain.Matches.Count];
        var loads = new Task<RouteLevelData?>?[limit];

        // Not disposed: loaders may keep the token for work that runs after this stage, and the registration on the
        // navigation's own token is released when that one is disposed.
        var failure = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
        var completed = false;

        try
        {
            for (var depth = 0; depth < limit; depth++)
            {
                loads[depth] = LoadLevelAsync(context, chain, depth, loads, reused, failure.Token);
            }

            for (var depth = 0; depth < limit; depth++)
            {
                try
                {
                    data[depth] = await loads[depth]!;
                }
                catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    Diagnostics.RouterLog.LoaderFailed(logger, exception, context.NavigationId, depth);
                    context.Error = new RouteError(RouteErrorKind.LoadFailed, $"Loading failed: {exception.Message}", exception, depth);
                    failure.Cancel();
                    DiscardFrom(loads, data, reused, depth + 1);

                    break;
                }
            }

            completed = true;
            context.LevelData = data;

            return StageResult.Continue;
        }
        finally
        {
            if (!completed)
            {
                // Superseded, canceled or unexpectedly failed: nothing this navigation loaded may outlive it.
                failure.Cancel();
                DiscardFrom(loads, data, reused, 0);
            }
        }
    }

    async Task<RouteLevelData?> LoadLevelAsync(
        RouteNavigationContext context,
        RouteMatchChain chain,
        int depth,
        Task<RouteLevelData?>?[] loads,
        bool[] reused,
        CancellationToken token)
    {
        var level = chain.Matches[depth];

        if (level.Route.Metadata.Get<RouteLoaderMetadata>() is not { } metadata)
        {
            return null;
        }

        var rules = level.Route.Metadata.Get<RouteLoaderCacheMetadata>();

        // A preload only warms the cache: a level that does not cache has nothing to gain from it.
        if (context.IsPreload && rules is null)
        {
            return null;
        }

        var previous = Previous(context, depth, level);

        if (previous is not null && CanReuse(context, depth, level, metadata.Policy, previous))
        {
            reused[depth] = true;

            return previous;
        }

        var lifetime = new CancellationTokenSource();
        var cache = rules is null ? null : context.Services.GetService<LoaderCache>();
        var key = new LoaderCache.Key(level.Route, level.InheritedParameters, context.Location.Query);

        // An explicit reload (or a revalidation) asks for fresh data; every other navigation may use what is cached.
        if (cache is not null && !context.Reloads(depth))
        {
            switch (cache.Find(key, rules!, out var cached))
            {
                case LoaderCacheState.Fresh:
                    return new RouteLevelData(level.Route, level.InheritedParameters, context.Location.Query, RouteLoadResult.Create(cached), lifetime);

                case LoaderCacheState.Stale when !context.IsPreload:
                    context.StaleLevels.Add(depth);

                    return new RouteLevelData(level.Route, level.InheritedParameters, context.Location.Query, RouteLoadResult.Create(cached), lifetime);
            }
        }

        // The server already ran this loader while prerendering and persisted its data: use it instead of loading twice.
        if (!context.IsPreload
            && LoaderStatePersistence.TryRestore(context.Services, level.Route, depth, context.Location.Path, context.Location.Query) is { } restored)
        {
            cache?.Set(key, restored.Data);

            return new RouteLevelData(level.Route, level.InheritedParameters, context.Location.Query, restored, lifetime);
        }

        try
        {
            var loader = (IRouteLoader)ActivatorUtilities.GetServiceOrCreateInstance(context.Services, metadata.LoaderType);
            var loadContext = new RouteLoadContext(
                level,
                context.Location.Query,
                previous is not null || context.Trigger == NavigationTrigger.Reload,
                context.Services,
                type => ParentDataAsync(loads, depth, type),
                token,
                lifetime.Token);

            var result = await loader.LoadAsync(loadContext);

            if (result.IsPlain)
            {
                cache?.Set(key, result.Data);
            }

            return new RouteLevelData(level.Route, level.InheritedParameters, context.Location.Query, result, lifetime);
        }
        catch
        {
            lifetime.Cancel();

            throw;
        }
    }

    static RouteLevelData? Previous(RouteNavigationContext context, int depth, RouteContext level)
        => context.FromState is { } from
            && depth < from.LevelData.Count
            && from.LevelData[depth] is { } existing
            && ReferenceEquals(existing.Route, level.Route)
                ? existing
                : null;

    static bool CanReuse(RouteNavigationContext context, int depth, RouteContext level, RouteReloadPolicy policy, RouteLevelData previous)
    {
        if (context.Reloads(depth))
        {
            return false;
        }

        return policy switch
        {
            RouteReloadPolicy.Never => true,
            RouteReloadPolicy.EveryNavigation => false,
            RouteReloadPolicy.WhenParametersOrQueryChange => previous.Parameters == level.InheritedParameters && previous.Query == context.Location.Query,
            _ => previous.Parameters == level.InheritedParameters,
        };
    }

    static async Task<object?> ParentDataAsync(Task<RouteLevelData?>?[] loads, int depth, Type type)
    {
        for (var ancestor = depth - 1; ancestor >= 0; ancestor--)
        {
            if (loads[ancestor] is not { } load)
            {
                continue;
            }

            var data = (await load)?.Data;

            if (data is not null && type.IsInstanceOfType(data))
            {
                return data;
            }
        }

        return null;
    }

    /// <summary>
    /// Forgets the loads from <paramref name="from"/> down: observes their outcome and discards whatever they produce
    /// (without waiting for them, so a loader that ignores cancellation cannot delay the navigation).
    /// </summary>
    static void DiscardFrom(Task<RouteLevelData?>?[] loads, RouteLevelData?[] data, bool[] reused, int from)
    {
        for (var depth = from; depth < loads.Length; depth++)
        {
            data[depth] = null;

            if (loads[depth] is not { } load)
            {
                continue;
            }

            var isReused = reused[depth];

            _ = load.ContinueWith(
                finished =>
                {
                    if (finished.IsCompletedSuccessfully && finished.Result is { } produced && !isReused)
                    {
                        produced.Discard();
                    }

                    _ = finished.Exception;
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
    }
}
