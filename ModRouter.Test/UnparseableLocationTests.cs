using ModRouter.Components;
using ModRouter.Diagnostics;
using ModRouter.Navigation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ModRouter.Test;

/// <summary>
/// A custom platform adapter can hand the coordinator a string no URI parser accepts (Blazor's own NavigationManager
/// validates what it reports). Such a location is ignored as if it were outside the base, and logged (1007): nothing the
/// router owns is involved, so nothing fails, and no pending or arrived state is left waiting for it.
/// </summary>
[TestFixture]
public sealed class UnparseableLocationTests
{
    const string Base = "http://localhost/";
    const string Unparseable = "http://[bad";

    /// <summary>An adapter that reports whatever string the test raises, and keeps the handler the coordinator registered.</summary>
    sealed class RawAdapter : INavigationPlatformAdapter
    {
        public Func<LocationChangingRequest, ValueTask<LocationChangingDecision>>? Handler { get; private set; }

        public string Uri { get; set; } = Base;

        public string BaseUri => Base;

        public string? HistoryEntryState => null;

        public event Action<LocationChangedNotification>? LocationChanged;

        public void NavigateTo(string uri, bool replaceHistory = false, bool forceLoad = false, string? historyState = null)
        {
        }

        public IDisposable RegisterLocationChanging(Func<LocationChangingRequest, ValueTask<LocationChangingDecision>> handler)
        {
            Handler = handler;

            return new NoOp();
        }

        public void RaiseChanged(string location) => LocationChanged?.Invoke(new LocationChangedNotification(location, false, null));

        sealed class NoOp : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }

    sealed class Harness : IDisposable
    {
        readonly ServiceProvider _services = new ServiceCollection().BuildServiceProvider();

        public Harness()
        {
            var options = new ModRouterOptions();
            options.Diagnostics.HistorySize = 20;
            Diagnostics = new RouterDiagnostics(options);
            Coordinator = new NavigationCoordinator(Adapter, Store, RouteTestKit.Frozen(Pipeline.Table()), new RouteComponentBinder(), _services, options, [], Log, Diagnostics);
            Coordinator.Attach();
        }

        public RawAdapter Adapter { get; } = new();

        public RouteStateStore Store { get; } = new();

        public LogCapture Log { get; } = new();

        public RouterDiagnostics Diagnostics { get; }

        public NavigationCoordinator Coordinator { get; }

        public void Dispose() => _services.Dispose();
    }

    [Test]
    public async Task An_unparseable_location_changed_is_ignored_and_logged()
    {
        using var h = new Harness();
        await h.Coordinator.NavigateInitialAsync();
        var committed = h.Store.Current;

        Assert.DoesNotThrow(() => h.Adapter.RaiseChanged(Unparseable));

        var entry = h.Log.WithId(1007).Single();
        Assert.Multiple(() =>
        {
            Assert.That(h.Store.Current, Is.SameAs(committed), "the committed state is unchanged");
            Assert.That(entry.Level, Is.EqualTo(LogLevel.Warning));
            Assert.That(entry["Location"], Is.EqualTo(Unparseable));
            Assert.That(entry.Message, Does.Contain(Unparseable));
            Assert.That(h.Log.WithId(1006), Is.Empty, "it is not treated as an unprepared location change");
        });
    }

    [Test]
    public async Task An_unparseable_location_changing_target_is_allowed_untouched()
    {
        using var h = new Harness();
        await h.Coordinator.NavigateInitialAsync();
        var committed = h.Store.Current;
        var navigations = h.Diagnostics.History.Count;
        var decision = LocationChangingDecision.Prevent;

        Assert.DoesNotThrowAsync(async () => decision = await h.Adapter.Handler!(new LocationChangingRequest(Unparseable, null, false, default)));

        Assert.Multiple(() =>
        {
            Assert.That(decision, Is.EqualTo(LocationChangingDecision.Allow));
            Assert.That(h.Store.Current, Is.SameAs(committed));
            Assert.That(h.Diagnostics.History, Has.Count.EqualTo(navigations), "no navigation ran for it");
            Assert.That(h.Log.WithId(1007).Single()["Location"], Is.EqualTo(Unparseable));
        });
    }

    [Test]
    public async Task A_pending_state_is_released_when_an_unparseable_location_arrives()
    {
        using var h = new Harness();
        await h.Coordinator.NavigateInitialAsync();

        // The router prepared /customers and the platform was to report it; it reports something unparseable instead.
        var decision = await h.Adapter.Handler!(new LocationChangingRequest("customers", null, true, default));
        Assert.That(decision, Is.EqualTo(LocationChangingDecision.Allow));
        h.Adapter.RaiseChanged(Unparseable);

        var released = h.Diagnostics.History.Single(record => record.NavigationId == 2);
        Assert.Multiple(() =>
        {
            Assert.That(h.Store.Current!.Path, Is.EqualTo("/"), "the prepared state was not committed");
            Assert.That((released.Outcome, released.OutcomeDetail), Is.EqualTo((NavigationRecordOutcome.Superseded, "released before its location change")));
            Assert.That(h.Log.WithId(1007), Has.Count.EqualTo(1));
        });

        // The next navigation commits normally.
        h.Adapter.Uri = Base + "login";
        Assert.That(await h.Adapter.Handler!(new LocationChangingRequest("login", null, true, default)), Is.EqualTo(LocationChangingDecision.Allow));
        h.Adapter.RaiseChanged(Base + "login");

        Assert.That(h.Store.Current!.Path, Is.EqualTo("/login"));
    }

    [Test]
    public async Task An_arrived_state_waiting_on_its_hook_is_released_when_an_unparseable_location_arrives()
    {
        using var h = new Harness();
        var hook = new TaskCompletionSource();
        h.Coordinator.OnNavigate = (_, _) => hook.Task;
        await h.Coordinator.NavigateInitialAsync();

        await h.Adapter.Handler!(new LocationChangingRequest("customers", null, true, default));
        h.Adapter.RaiseChanged(Base + "customers");
        Assert.That(h.Store.IsNavigating, Is.True, "the old page waits for the hook of the navigation that arrived");

        Assert.DoesNotThrow(() => h.Adapter.RaiseChanged(Unparseable));

        Assert.Multiple(() =>
        {
            Assert.That(h.Store.IsNavigating, Is.False, "the arrived navigation was released");
            Assert.That(h.Store.Current!.Path, Is.EqualTo("/"));
            Assert.That(h.Log.WithId(1007), Has.Count.EqualTo(1));
        });

        hook.SetResult();
        await Task.Yield();
        await Task.Yield();
        Assert.That(h.Store.Current!.Path, Is.EqualTo("/"), "the released navigation does not commit when its hook returns");
    }

    // Whatever the platform could hand over: each is parsed (resolved against the base) or ignored and logged once per
    // handler, and never an exception. "//", "http://" and a bracket host with no closing bracket have no valid URI form.
    [TestCase("//", true)]
    [TestCase("http://", true)]
    [TestCase("http://[::1", true)]
    [TestCase("", false)]
    [TestCase("relative/path", false)]
    [TestCase("mailto:", false)]
    public async Task A_location_in_any_shape_never_throws_from_either_handler(string location, bool ignored)
    {
        using var h = new Harness();
        await h.Coordinator.NavigateInitialAsync();

        Assert.DoesNotThrow(() => h.Adapter.RaiseChanged(location), "LocationChanged");
        Assert.DoesNotThrowAsync(async () => await h.Adapter.Handler!(new LocationChangingRequest(location, null, false, default)), "LocationChanging");
        await Task.Yield();

        Assert.That(h.Log.WithId(1007), Has.Count.EqualTo(ignored ? 2 : 0), "logged once by each handler when the location does not parse");
    }
}
