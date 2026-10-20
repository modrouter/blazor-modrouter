using System.Runtime.CompilerServices;
using ModRouter.Core.Loading;

namespace ModRouter.Test;

[TestFixture]
public sealed class DeferredAndStreamTests
{
    static async IAsyncEnumerable<int> Numbers(TaskCompletionSource gate, int count, bool fail = false, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        for (var i = 1; i <= count; i++)
        {
            yield return i;
        }

        await gate.Task.WaitAsync(cancellationToken);

        if (fail)
        {
            throw new InvalidOperationException("source failed");
        }
    }

    static async Task<bool> WaitAsync(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(10);
        }

        return condition();
    }

    [Test]
    public async Task Deferred_value_succeeds_and_raises_changed_once()
    {
        var source = new TaskCompletionSource<string>();
        var deferred = new DeferredValue<string>(source.Task);
        var changes = 0;
        deferred.Changed += () => Interlocked.Increment(ref changes);

        Assert.That(deferred.Status, Is.EqualTo(DeferredStatus.Pending));

        source.SetResult("done");

        Assert.That(await WaitAsync(() => deferred.Status == DeferredStatus.Succeeded), Is.True);
        Assert.That(deferred.Value, Is.EqualTo("done"));
        Assert.That(await WaitAsync(() => Volatile.Read(ref changes) == 1), Is.True);
    }

    [Test]
    public async Task Deferred_fault_is_contained_and_observed()
    {
        var source = new TaskCompletionSource<int>();
        var deferred = new DeferredValue<int>(source.Task);

        source.SetException(new InvalidOperationException("boom"));

        Assert.That(await WaitAsync(() => deferred.Status == DeferredStatus.Faulted), Is.True);
        Assert.That(deferred.Error, Is.TypeOf<InvalidOperationException>());
    }

    [Test]
    public async Task Deferred_cancellation_is_its_own_status()
    {
        var source = new TaskCompletionSource<int>();
        var deferred = new DeferredValue<int>(source.Task);

        source.SetCanceled();

        Assert.That(await WaitAsync(() => deferred.Status == DeferredStatus.Canceled), Is.True);
        Assert.That(deferred.Error, Is.Null);
    }

    [Test]
    public async Task Stream_does_not_enumerate_until_started()
    {
        var gate = new TaskCompletionSource();
        var stream = new RouteStream<int>(Numbers(gate, 3));

        await Task.Delay(50);

        Assert.That(stream.Items, Is.Empty);
        Assert.That(stream.IsComplete, Is.False);

        ((IRouteStream)stream).Start(CancellationToken.None);

        Assert.That(await WaitAsync(() => stream.Items.Count == 3), Is.True);
        Assert.That(stream.IsComplete, Is.False);

        gate.SetResult();

        Assert.That(await WaitAsync(() => stream.IsComplete), Is.True);
        Assert.That(stream.Items, Is.EqualTo(new[] { 1, 2, 3 }));
    }

    [Test]
    public async Task Stream_records_the_exception_that_ended_it_and_keeps_the_items()
    {
        var gate = new TaskCompletionSource();
        var stream = new RouteStream<int>(Numbers(gate, 2, fail: true));
        ((IRouteStream)stream).Start(CancellationToken.None);
        gate.SetResult();

        Assert.That(await WaitAsync(() => stream.Error is not null), Is.True);
        Assert.That(stream.Items, Is.EqualTo(new[] { 1, 2 }));
        Assert.That(stream.IsComplete, Is.False);
    }

    [Test]
    public async Task Stream_stops_quietly_when_canceled()
    {
        var gate = new TaskCompletionSource();
        using var cts = new CancellationTokenSource();
        var stream = new RouteStream<int>(Numbers(gate, 1));
        var changes = 0;
        stream.Changed += () => Interlocked.Increment(ref changes);
        ((IRouteStream)stream).Start(cts.Token);

        Assert.That(await WaitAsync(() => stream.Items.Count == 1), Is.True);

        cts.Cancel();
        await Task.Delay(50);

        Assert.That(stream.Error, Is.Null);
        Assert.That(stream.IsComplete, Is.False);
        Assert.That(changes, Is.EqualTo(1));
    }

    static async IAsyncEnumerable<int> Synchronous(Action onPull)
    {
        await Task.CompletedTask;

        for (var i = 1; i <= 3; i++)
        {
            onPull();

            yield return i;
        }
    }

    [Test]
    public async Task Stream_start_returns_before_a_synchronous_source_is_enumerated()
    {
        // Start runs inside the navigation commit, on the renderer: a source that has its items ready must not be
        // enumerated to the end right there.
        var pulled = 0;
        var stream = new RouteStream<int>(Synchronous(() => Interlocked.Increment(ref pulled)));

        ((IRouteStream)stream).Start(CancellationToken.None);
        var pulledInsideStart = Volatile.Read(ref pulled);

        Assert.That(await WaitAsync(() => stream.IsComplete), Is.True);
        Assert.That(pulledInsideStart, Is.Zero, "the pump must not run inside Start");
        Assert.That(stream.Items, Is.EqualTo(new[] { 1, 2, 3 }));
    }

    [Test]
    public async Task Stream_start_is_idempotent()
    {
        var gate = new TaskCompletionSource();
        var stream = new RouteStream<int>(Numbers(gate, 2));
        ((IRouteStream)stream).Start(CancellationToken.None);
        ((IRouteStream)stream).Start(CancellationToken.None);
        gate.SetResult();

        Assert.That(await WaitAsync(() => stream.IsComplete), Is.True);
        Assert.That(stream.Items, Is.EqualTo(new[] { 1, 2 }));
    }

    [Test]
    public async Task A_throwing_deferred_subscriber_does_not_stop_the_others_or_corrupt_the_value()
    {
        var source = new TaskCompletionSource<int>();
        var deferred = new DeferredValue<int>(source.Task);
        var reached = 0;
        deferred.Changed += () => throw new InvalidOperationException("bad subscriber");
        deferred.Changed += () => Interlocked.Increment(ref reached);

        source.SetResult(1);

        Assert.That(await WaitAsync(() => Volatile.Read(ref reached) == 1), Is.True, "the subscriber after the throwing one still runs");
        Assert.That(deferred.Status, Is.EqualTo(DeferredStatus.Succeeded));
        Assert.That(deferred.Value, Is.EqualTo(1));
    }

    [Test]
    public async Task A_throwing_stream_subscriber_does_not_end_the_stream_or_stop_the_others()
    {
        var gate = new TaskCompletionSource();
        var stream = new RouteStream<int>(Numbers(gate, 3));
        var reached = 0;
        stream.Changed += () => throw new InvalidOperationException("bad subscriber");
        stream.Changed += () => Interlocked.Increment(ref reached);
        ((IRouteStream)stream).Start(CancellationToken.None);
        gate.SetResult();

        Assert.That(await WaitAsync(() => stream.IsComplete), Is.True, "a subscriber bug must not stop the enumeration");
        Assert.That(stream.Items, Is.EqualTo(new[] { 1, 2, 3 }));
        Assert.That(stream.Error, Is.Null);
        Assert.That(Volatile.Read(ref reached), Is.EqualTo(4), "three items and the completion");
    }

    [Test]
    public void Load_result_collects_deferred_values_and_streams_by_name()
    {
        var result = RouteLoadResult.Create("critical")
            .Defer("slow", Task.FromResult(5))
            .Stream("feed", Numbers(new TaskCompletionSource(), 1));

        Assert.That(result.Data, Is.EqualTo("critical"));
        Assert.That(result.Deferred["slow"], Is.TypeOf<DeferredValue<int>>());
        Assert.That(result.Streams["feed"], Is.TypeOf<RouteStream<int>>());
    }
}
