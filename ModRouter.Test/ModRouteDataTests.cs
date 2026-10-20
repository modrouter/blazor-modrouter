using ModRouter.Components;
using ModRouter.Core.Definitions;
using ModRouter.Core.Loading;
using ModRouter.Core.Matching;
using ModRouter.Navigation;

namespace ModRouter.Test;

[TestFixture]
public sealed class ModRouteDataTests
{
    static readonly RouteDefinition Route = new() { Template = "/x", Name = "x", Component = typeof(HomePage) };

    static RouteLevelData Level(object? data = null, Action<RouteLoadResult>? configure = null, CancellationTokenSource? lifetime = null)
    {
        var result = RouteLoadResult.Create(data);
        configure?.Invoke(result);

        return new RouteLevelData(Route, RouteParameters.Empty, string.Empty, result, lifetime ?? new CancellationTokenSource());
    }

    static async IAsyncEnumerable<int> Numbers(Action onPull)
    {
        onPull();
        await Task.Yield();

        yield return 1;
    }

    [Test]
    public void Get_returns_the_data_of_the_requested_type_or_default()
    {
        var data = Level("critical").Accessor;

        Assert.That(data.Get<string>(), Is.EqualTo("critical"));
        Assert.That(data.Get<int?>(), Is.Null);
        Assert.That(ModRouteData.Empty.Get<string>(), Is.Null);
    }

    [Test]
    public void GetRequired_throws_a_helpful_error_when_the_level_has_no_such_data()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => Level("critical").Accessor.GetRequired<int>())!;

        Assert.That(exception.Message, Does.Contain("Int32").And.Contain("Loader"));
        Assert.That(Level(5).Accessor.GetRequired<int>(), Is.EqualTo(5));
    }

    [Test]
    public void GetFromAncestor_walks_up_to_the_nearest_ancestor_with_that_type()
    {
        var grandparent = Level("grand");
        var parent = Level(42);
        var child = Level("child");
        parent.Accessor.Parent = grandparent.Accessor;
        child.Accessor.Parent = parent.Accessor;

        Assert.That(child.Accessor.Get<string>(), Is.EqualTo("child"));
        Assert.That(child.Accessor.GetFromAncestor<int>(), Is.EqualTo(42));
        Assert.That(child.Accessor.GetFromAncestor<string>(), Is.EqualTo("grand"), "the level's own data is not an ancestor's");
        Assert.That(grandparent.Accessor.GetFromAncestor<string>(), Is.Null);
    }

    [Test]
    public void Deferred_and_stream_slots_are_found_by_name_and_type()
    {
        var level = Level("c", result => result.Defer("slow", Task.FromResult(3)).Stream("feed", Numbers(() => { })));

        Assert.That(level.Accessor.Deferred<int>("slow"), Is.SameAs(level.Deferred<int>("slow")));
        Assert.That(level.Accessor.Stream<int>("feed"), Is.SameAs(level.Stream<int>("feed")));
        Assert.Throws<KeyNotFoundException>(() => level.Accessor.Deferred<int>("nope"));
        Assert.Throws<InvalidCastException>(() => level.Accessor.Stream<string>("feed"));
    }

    [Test]
    public void The_not_found_message_lists_what_the_loader_did_return()
    {
        var level = Level("c", result => result.Defer("slow", Task.FromResult(3)).Defer("alpha", Task.FromResult(1)));

        var exception = Assert.Throws<KeyNotFoundException>(() => level.Deferred<int>("missing"))!;

        Assert.That(exception.Message, Does.Contain("missing").And.Contain("alpha, slow").And.Contain("'x'"));
    }

    [Test]
    public void The_empty_data_has_no_slots_and_says_so()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => ModRouteData.Empty.Deferred<int>("slow"))!;

        Assert.That(exception.Message, Does.Contain("no loader"));
        Assert.Throws<InvalidOperationException>(() => ModRouteData.Empty.Stream<int>("feed"));
    }

    [Test]
    public void Component_lookups_tolerate_missing_slots_and_wrong_types_instead_of_throwing()
    {
        var level = Level("c", result => result.Defer("slow", Task.FromResult(3)));

        Assert.That(level.Accessor.FindDeferred<int>("slow"), Is.Not.Null);
        Assert.That(level.Accessor.FindDeferred<int>("missing"), Is.Null);
        Assert.That(level.Accessor.FindDeferred<string>("slow"), Is.Null);
        Assert.That(level.Accessor.FindStream<int>("slow"), Is.Null);
        Assert.That(ModRouteData.Empty.FindDeferred<int>("slow"), Is.Null);
        Assert.That(ModRouteData.Empty.FindStream<int>("feed"), Is.Null);
    }

    [Test]
    public async Task Streams_start_once_on_activation_and_never_before()
    {
        var pulls = 0;
        var level = Level("c", result => result.Stream("feed", Numbers(() => Interlocked.Increment(ref pulls))));

        await Task.Delay(50);
        Assert.That(pulls, Is.Zero);

        level.Activate();
        level.Activate();
        await Task.Delay(100);

        Assert.That(pulls, Is.EqualTo(1));
    }

    [Test]
    public void Discard_cancels_the_lifetime_token_the_loader_was_given()
    {
        using var lifetime = new CancellationTokenSource();
        var level = Level("c", lifetime: lifetime);

        Assert.That(lifetime.IsCancellationRequested, Is.False);

        level.Discard();

        Assert.That(lifetime.IsCancellationRequested, Is.True);
    }

    [Test]
    public async Task Discard_stops_a_running_stream()
    {
        var pulls = 0;
        var level = Level("c", result => result.Stream("feed", Endless(() => Interlocked.Increment(ref pulls))));
        level.Activate();
        await Task.Delay(50);

        level.Discard();
        await Task.Delay(50);
        var after = pulls;
        await Task.Delay(100);

        Assert.That(pulls, Is.EqualTo(after), "no more items are pulled once the level is discarded");
        Assert.That(level.Stream<int>("feed").Error, Is.Null, "being discarded is not an error");
    }

    static async IAsyncEnumerable<int> Endless(Action onPull, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        while (true)
        {
            onPull();
            await Task.Delay(5, cancellationToken);

            yield return 1;
        }
    }
}
