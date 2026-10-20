using Bunit;
using ModRouter.Components;
using ModRouter.Core.Definitions;
using ModRouter.Core.Loading;
using ModRouter.Navigation;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace ModRouter.Test;

/// <summary>
/// Event 1410: a parent level that rendered its page but never rendered an outlet for the child the chain matched. A state
/// is judged when it leaves the screen (replaced or the router disposed), never when it was shown for less than the settle
/// window, never past a failed level, and each route warns once. The same in the default mode and in <c>Found</c> mode.
/// </summary>
[TestFixture]
public sealed class OutletWarningTests
{
    public sealed class ParentNoOutlet : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder) => builder.AddMarkupContent(0, "<p id=\"parent\">parent</p>");
    }

    public sealed class ParentWithOutlet : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.AddMarkupContent(0, "<p id=\"parent\">parent</p>");
            builder.OpenComponent<ModRouterOutlet>(1);
            builder.CloseComponent();
        }
    }

    public sealed class ParentConditionalOutlet : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            // The condition never becomes true: the outlet is written but never rendered.
            if (DateTime.MinValue.Year > 1)
            {
                builder.OpenComponent<ModRouterOutlet>(1);
                builder.CloseComponent();
            }
        }
    }

    public sealed class ParentLoadingOutlet : ComponentBase
    {
        public static TaskCompletionSource Gate { get; set; } = new();

        bool _loaded;

        protected override async Task OnInitializedAsync()
        {
            await Gate.Task;
            _loaded = true;
        }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            if (_loaded)
            {
                builder.OpenComponent<ModRouterOutlet>(1);
                builder.CloseComponent();
            }
            else
            {
                builder.AddMarkupContent(0, "<p id=\"parent-loading\">loading</p>");
            }
        }
    }

    public sealed class ChildPage : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder) => builder.AddMarkupContent(0, "<p id=\"child\">child</p>");
    }

    sealed class FailingLoader : IRouteLoader
    {
        public ValueTask<RouteLoadResult> LoadAsync(RouteLoadContext context) => throw new InvalidOperationException("load");
    }

    static void Routes(RouteMap map) => map
        .Route("/", typeof(HomeProbe), name: "home")
        .Route("/none", typeof(ParentNoOutlet), name: "none", children: c => c.Index(typeof(ChildPage), name: "none-index").Route("x", typeof(ChildPage), name: "none-x"))
        .Route("/with", typeof(ParentWithOutlet), name: "with", children: c => c.Route("x", typeof(ChildPage), name: "with-x"))
        .Route("/cond", typeof(ParentConditionalOutlet), name: "cond", children: c => c.Route("x", typeof(ChildPage), name: "cond-x"))
        .Route("/loading", typeof(ParentLoadingOutlet), name: "loading", children: c => c.Route("x", typeof(ChildPage), name: "loading-x"))
        .Route("/failing", typeof(ParentNoOutlet), name: "failing", children: c => c.Route("x", typeof(ChildPage), name: "failing-x")).Loader<FailingLoader>();

    sealed class Harness : IAsyncDisposable
    {
        public required BunitContext Context { get; init; }

        public required FakeNavigationAdapter Adapter { get; init; }

        public required FakeTimeProvider Time { get; init; }

        public required LogCapture Log { get; init; }

        public required IRenderedComponent<Components.ModRouter> Router { get; init; }

        /// <summary>The 1410 warnings so far, as (route name, depth).</summary>
        public IReadOnlyList<(string? Route, int Depth)> Warnings
            => [.. Log.WithId(1410).Select(e => ((string?)e["RouteName"], (int)e["Depth"]!))];

        /// <summary>Navigates, waits for the commit to render, and stays <paramref name="stay"/> (2 s by default).</summary>
        public async Task GoAsync(string uri, TimeSpan? stay = null)
        {
            await Adapter.NavigateAsync(uri, intercepted: true);
            await Adapter.SettledAsync();
            Router.WaitForState(() => true);
            Time.Advance(stay ?? TimeSpan.FromSeconds(2));
        }

        public ValueTask DisposeAsync() => Context.DisposeAsync();
    }

    static Harness Mount(bool found)
    {
        var ctx = new BunitContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        var time = new FakeTimeProvider();
        var log = new LogCapture();
        ctx.Services.AddSingleton<TimeProvider>(time);
        ctx.Services.AddSingleton<ILoggerFactory>(log);
        ctx.Services.AddModRouter(Routes);
        var adapter = new FakeNavigationAdapter("http://localhost/") { ExecuteNavigateTo = true };
        ctx.Services.AddSingleton<INavigationPlatformAdapter>(adapter);
        var router = ctx.Render<Components.ModRouter>(p =>
        {
            p.Add(r => r.DefaultLayout, typeof(AltLayoutProbe));
            p.Add(r => r.Error, error => b => b.AddMarkupContent(0, $"<p id=\"route-error\">{error.Kind}</p>"));

            if (found)
            {
                p.Add(r => r.Found, routeData => b =>
                {
                    b.OpenComponent<Compatibility.RouteView>(0);
                    b.AddComponentParameter(1, nameof(Compatibility.RouteView.RouteData), routeData);
                    b.CloseComponent();
                });
            }
        });
        time.Advance(TimeSpan.FromSeconds(2));

        return new Harness { Context = ctx, Adapter = adapter, Time = time, Log = log, Router = router };
    }

    [SetUp]
    public void SetUp() => ParentLoadingOutlet.Gate = new TaskCompletionSource();

    [TearDown]
    public void TearDown() => ParentLoadingOutlet.Gate.TrySetResult();

    [TestCase(false)]
    [TestCase(true)]
    public async Task The_probe_table_warns_only_for_a_parent_that_rendered_its_page_without_an_outlet(bool found)
    {
        await using var h = Mount(found);
        var log = new List<string>();

        async Task StepAsync(string uri, string label, TimeSpan? stay = null, Func<Task>? whileShown = null)
        {
            var before = h.Warnings.Count;
            await h.GoAsync(uri, stay);

            if (whileShown is not null)
            {
                await whileShown();
            }

            await h.GoAsync("/");
            log.Add($"{label}: {(h.Warnings.Count > before ? string.Join(";", h.Warnings.Skip(before)) : "no warning")}");
        }

        await StepAsync("/with/x", "parent with outlet");
        await StepAsync("/none/x", "parent without outlet");
        await StepAsync("/none/x", "same route again");
        await StepAsync("/cond/x", "outlet in a conditional that stays false");
        await StepAsync("/nope", "not found");
        await StepAsync("/failing/x", "failed level");
        await StepAsync("/loading/x", "parent loading, replaced after 100 ms", TimeSpan.FromMilliseconds(100));
        await StepAsync("/loading/x", "parent loading, then the outlet renders", whileShown: async () =>
        {
            ParentLoadingOutlet.Gate.SetResult();
            await ComponentTestKit.EventuallyAsync(() => ComponentTestKit.All(h.Router, "#child").Count, Is.EqualTo(1));
        });

        Assert.That(log, Is.EqualTo(new[]
        {
            "parent with outlet: no warning",
            "parent without outlet: (none, 1)",
            "same route again: no warning",
            "outlet in a conditional that stays false: (cond, 1)",
            "not found: no warning",
            "failed level: no warning",
            "parent loading, replaced after 100 ms: no warning",
            "parent loading, then the outlet renders: no warning",
        }));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task An_index_child_under_a_parent_without_an_outlet_warns(bool found)
    {
        await using var h = Mount(found);

        await h.GoAsync("/none");
        await h.GoAsync("/");

        Assert.That(h.Warnings, Is.EqualTo(new[] { ((string?)"none", 1) }));
    }

    [Test]
    public async Task The_warning_names_the_route_its_template_and_the_component_without_an_outlet_as_a_Warning()
    {
        await using var h = Mount(found: false);

        await h.GoAsync("/none/x");
        await h.GoAsync("/");

        var entry = h.Log.WithId(1410).Single();
        Assert.Multiple(() =>
        {
            Assert.That(entry.EventId.Name, Is.EqualTo("OutletNotRendered"));
            Assert.That(entry.Level, Is.EqualTo(LogLevel.Warning));
            Assert.That(entry["RouteName"], Is.EqualTo("none"));
            Assert.That(entry["Template"], Is.EqualTo("/none"));
            Assert.That(entry["Component"], Is.EqualTo(typeof(ParentNoOutlet).FullName));
            Assert.That(entry["Depth"], Is.EqualTo(1));
            Assert.That(entry.Category, Is.EqualTo(typeof(Components.ModRouter).FullName));
        });
    }

    [Test]
    public async Task A_state_still_on_screen_when_the_router_is_disposed_is_judged_too()
    {
        var h = Mount(found: false);

        await h.GoAsync("/none/x");
        await h.DisposeAsync();

        Assert.That(h.Warnings, Is.EqualTo(new[] { ((string?)"none", 1) }));
    }

    [Test]
    public async Task A_state_disposed_within_the_settle_window_is_not_judged()
    {
        var h = Mount(found: false);

        await h.GoAsync("/none/x", TimeSpan.FromMilliseconds(500));
        await h.DisposeAsync();

        Assert.That(h.Warnings, Is.Empty);
    }
}
