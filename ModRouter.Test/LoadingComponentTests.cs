using System.Threading.Channels;
using Bunit;
using static ModRouter.Test.ComponentTestKit;
using ModRouter.Components;
using ModRouter.Core.Compilation;
using ModRouter.Core.Loading;
using ModRouter.Navigation;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace ModRouter.Test;

[TestFixture]
public sealed class LoadingComponentTests
{
    BunitContext _ctx = default!;

    [SetUp]
    public void SetUp() => _ctx = new BunitContext();

    [TearDown]
    public async Task TearDown() => await _ctx.DisposeAsync();

    /// <summary>Polls instead of using WaitForAssertion, which only re-checks when the component under test renders.</summary>
    // ---- ModDeferred --------------------------------------------------------------------------------------------

    IRenderedComponent<ModDeferred<int>> RenderDeferred(DeferredValue<int> value, bool withError = false)
        => _ctx.Render<ModDeferred<int>>(p =>
        {
            p.Add(c => c.Value, value);
            p.Add(c => c.Fallback, Markup("<i id=\"fallback\">wait</i>"));
            p.Add(c => c.ChildContent, n => Markup($"<b id=\"value\">{n}</b>"));

            if (withError)
            {
                p.Add(c => c.Error, error => Markup($"<u id=\"error\">{error.Message}</u>"));
            }
        });

    [Test]
    public async Task Deferred_shows_the_fallback_then_the_value()
    {
        var source = new TaskCompletionSource<int>();
        var cut = RenderDeferred(new DeferredValue<int>(source.Task));

        Assert.That(One(cut, "#fallback").TextContent, Is.EqualTo("wait"));

        source.SetResult(9);
        await EventuallyAsync(() => One(cut, "#value").TextContent, Is.EqualTo("9"));
        Assert.That(All(cut, "#fallback"), Is.Empty);
    }

    [Test]
    public void Deferred_renders_a_value_that_already_arrived_without_the_fallback()
    {
        var value = new DeferredValue<int>(Task.FromResult(4));
        SpinWait.SpinUntil(() => value.Status == DeferredStatus.Succeeded, TimeSpan.FromSeconds(2));

        var cut = RenderDeferred(value);

        Assert.That(One(cut, "#value").TextContent, Is.EqualTo("4"));
        Assert.That(All(cut, "#fallback"), Is.Empty);
    }

    [Test]
    public async Task Deferred_shows_the_error_content_when_the_task_fails()
    {
        var source = new TaskCompletionSource<int>();
        var cut = RenderDeferred(new DeferredValue<int>(source.Task), withError: true);

        source.SetException(new InvalidOperationException("slow service down"));

        await EventuallyAsync(() => One(cut, "#error").TextContent, Is.EqualTo("slow service down"));
    }

    [Test]
    public async Task Deferred_without_error_content_shows_a_plain_alert()
    {
        var source = new TaskCompletionSource<int>();
        var cut = RenderDeferred(new DeferredValue<int>(source.Task));

        source.SetException(new InvalidOperationException("slow service down"));

        await EventuallyAsync(() => One(cut, "[role=alert]").TextContent, Is.EqualTo("slow service down"));
    }

    [Test]
    public async Task Deferred_swaps_to_a_new_value_and_stops_listening_to_the_old_one()
    {
        var first = new TaskCompletionSource<int>();
        var second = new TaskCompletionSource<int>();
        var cut = RenderDeferred(new DeferredValue<int>(first.Task));

        cut.Render(p => p.Add(c => c.Value, new DeferredValue<int>(second.Task)));
        first.SetResult(1);
        second.SetResult(2);

        await EventuallyAsync(() => One(cut, "#value").TextContent, Is.EqualTo("2"));
    }

    [Test]
    public async Task Deferred_looks_a_name_up_in_the_cascaded_route_data()
    {
        var source = new TaskCompletionSource<int>();
        var level = new RouteLevelData(
            new Core.Definitions.RouteDefinition { Template = "/x", Component = typeof(HomePage) },
            Core.Matching.RouteParameters.Empty,
            string.Empty,
            RouteLoadResult.Create().Defer("slow", source.Task),
            new CancellationTokenSource());

        var cut = _ctx.Render<ModDeferred<int>>(p =>
        {
            p.AddCascadingValue(level.Accessor);
            p.Add(c => c.Name, "slow");
            p.Add(c => c.ChildContent, n => Markup($"<b id=\"value\">{n}</b>"));
        });
        source.SetResult(3);

        await EventuallyAsync(() => One(cut, "#value").TextContent, Is.EqualTo("3"));
    }

    [Test]
    public void Deferred_with_a_name_but_no_route_data_explains_itself()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => _ctx.Render<ModDeferred<int>>(p => p.Add(c => c.Name, "slow")))!;

        Assert.That(exception.Message, Does.Contain("route data"));
    }

    // ---- ModStream ----------------------------------------------------------------------------------------------

    sealed class StreamHarness
    {
        public Channel<int> Channel { get; } = System.Threading.Channels.Channel.CreateUnbounded<int>();

        public RouteStream<int> Stream { get; }

        public StreamHarness()
        {
            Stream = new RouteStream<int>(Channel.Reader.ReadAllAsync());
            ((IRouteStream)Stream).Start(CancellationToken.None);
        }
    }

    IRenderedComponent<ModStream<int>> RenderStream(StreamHarness harness, bool withOptional = true)
        => _ctx.Render<ModStream<int>>(p =>
        {
            p.Add(c => c.Value, harness.Stream);
            p.Add(c => c.Fallback, Markup("<i id=\"fallback\">none yet</i>"));
            p.Add(c => c.ItemTemplate, item => Markup($"<span class=\"item\">{item}</span>"));

            if (withOptional)
            {
                p.Add(c => c.Empty, Markup("<i id=\"empty\">nothing</i>"));
                p.Add(c => c.InProgress, Markup("<i id=\"more\">loading more</i>"));
                p.Add(c => c.Error, error => Markup($"<u id=\"error\">{error.Message}</u>"));
            }
        });

    [Test]
    public async Task Stream_shows_the_fallback_until_the_first_item_then_the_items_and_the_in_progress_view()
    {
        var harness = new StreamHarness();
        var cut = RenderStream(harness);

        Assert.That(One(cut, "#fallback").TextContent, Is.EqualTo("none yet"));

        harness.Channel.Writer.TryWrite(1);
        harness.Channel.Writer.TryWrite(2);

        await EventuallyAsync(() => All(cut, ".item").Select(i => i.TextContent), Is.EqualTo(new[] { "1", "2" }));
        Assert.That(All(cut, "#fallback"), Is.Empty);
        Assert.That(One(cut, "#more"), Is.Not.Null);

        harness.Channel.Writer.Complete();

        await EventuallyAsync(() => All(cut, "#more"), Is.Empty);
        Assert.That(All(cut, ".item"), Has.Count.EqualTo(2));
    }

    [Test]
    public async Task Stream_that_completes_empty_shows_the_empty_view()
    {
        var harness = new StreamHarness();
        var cut = RenderStream(harness);

        harness.Channel.Writer.Complete();

        await EventuallyAsync(() => One(cut, "#empty").TextContent, Is.EqualTo("nothing"));
        Assert.That(All(cut, "#fallback"), Is.Empty);
    }

    [Test]
    public async Task Stream_error_shows_after_the_items_that_arrived()
    {
        var harness = new StreamHarness();
        var cut = RenderStream(harness);
        harness.Channel.Writer.TryWrite(1);
        await EventuallyAsync(() => All(cut, ".item"), Has.Count.EqualTo(1));

        harness.Channel.Writer.Complete(new InvalidOperationException("feed broke"));

        await EventuallyAsync(() => One(cut, "#error").TextContent, Is.EqualTo("feed broke"));
        Assert.That(All(cut, ".item"), Has.Count.EqualTo(1));
        Assert.That(All(cut, "#more"), Is.Empty);
    }

    [Test]
    public async Task Stream_without_an_error_view_shows_a_plain_alert()
    {
        var harness = new StreamHarness();
        var cut = RenderStream(harness, withOptional: false);

        harness.Channel.Writer.Complete(new InvalidOperationException("feed broke"));

        await EventuallyAsync(() => One(cut, "[role=alert]").TextContent, Is.EqualTo("feed broke"));
    }

    static IEnumerable<int> Range(int count) => Enumerable.Range(0, count);

    static async IAsyncEnumerable<int> Immediate(int count)
    {
        foreach (var item in Range(count))
        {
            yield return item;
        }

        await Task.CompletedTask;
    }

    [Test]
    public async Task A_burst_of_items_that_is_ready_at_once_renders_once_not_once_per_item()
    {
        var stream = new RouteStream<int>(Immediate(500));
        var cut = _ctx.Render<ModStream<int>>(p =>
        {
            p.Add(c => c.Value, stream);
            p.Add(c => c.ItemTemplate, item => Markup($"<span class=\"item\">{item}</span>"));
        });
        var before = cut.RenderCount;

        ((IRouteStream)stream).Start(CancellationToken.None);
        while (!stream.IsComplete)
        {
            await Task.Delay(10);
        }

        await Task.Delay(200);

        Assert.That(All(cut, ".item"), Has.Count.EqualTo(500));
        Assert.That(cut.RenderCount - before, Is.LessThan(10), "items that are already available must not cause one render each");
    }

    // ---- ModNavigationIndicator ---------------------------------------------------------------------------------

    (IRenderedComponent<ModNavigationIndicator> Cut, RouteStateStore Store, FakeTimeProvider Time) MountIndicator(TimeSpan? delay = null)
    {
        var time = new FakeTimeProvider();
        _ctx.Services.AddSingleton<TimeProvider>(time);
        _ctx.Services.AddModRouter(map => map.Route("/", typeof(HomeProbe), name: "home"));
        _ctx.Services.AddSingleton<INavigationPlatformAdapter>(new FakeNavigationAdapter());

        var cut = _ctx.Render<ModNavigationIndicator>(p =>
        {
            p.Add(c => c.ChildContent, Markup("<progress id=\"busy\"></progress>"));

            if (delay is { } value)
            {
                p.Add(c => c.Delay, value);
            }
        });

        return (cut, _ctx.Services.GetRequiredService<RouteStateStore>(), time);
    }

    [Test]
    public async Task The_indicator_appears_only_after_the_delay()
    {
        var (cut, store, time) = MountIndicator();

        store.SetNavigating(true);
        time.Advance(TimeSpan.FromMilliseconds(149));
        Assert.That(All(cut, "#busy"), Is.Empty);

        time.Advance(TimeSpan.FromMilliseconds(1));

        await EventuallyAsync(() => One(cut, "#busy"), Is.Not.Null);

        store.SetNavigating(false);

        await EventuallyAsync(() => All(cut, "#busy"), Is.Empty);
    }

    [Test]
    public void A_navigation_that_finishes_inside_the_delay_never_shows_the_indicator()
    {
        var (cut, store, time) = MountIndicator();
        var renders = cut.RenderCount;

        store.SetNavigating(true);
        time.Advance(TimeSpan.FromMilliseconds(100));
        store.SetNavigating(false);
        time.Advance(TimeSpan.FromSeconds(5));

        Assert.That(All(cut, "#busy"), Is.Empty);
        Assert.That(cut.RenderCount, Is.EqualTo(renders));
    }

    [Test]
    public async Task The_delay_restarts_for_the_newest_navigation()
    {
        var (cut, store, time) = MountIndicator();

        store.SetNavigating(true);
        time.Advance(TimeSpan.FromMilliseconds(100));
        store.SetNavigating(false);
        store.SetNavigating(true);
        time.Advance(TimeSpan.FromMilliseconds(100));

        Assert.That(All(cut, "#busy"), Is.Empty);

        time.Advance(TimeSpan.FromMilliseconds(50));

        await EventuallyAsync(() => One(cut, "#busy"), Is.Not.Null);
    }

    [Test]
    public async Task A_zero_delay_shows_the_indicator_immediately()
    {
        var (cut, store, _) = MountIndicator(TimeSpan.Zero);

        store.SetNavigating(true);

        await EventuallyAsync(() => One(cut, "#busy"), Is.Not.Null);
    }

    [Test]
    public async Task The_indicator_stops_listening_when_disposed()
    {
        var (_, store, time) = MountIndicator();

        await _ctx.DisposeComponentsAsync();
        store.SetNavigating(true);
        time.Advance(TimeSpan.FromSeconds(1));

        Assert.That(_ctx.Renderer.UnhandledException.IsCompleted, Is.False);
        Assert.That(store.IsNavigating, Is.True);
    }
}
