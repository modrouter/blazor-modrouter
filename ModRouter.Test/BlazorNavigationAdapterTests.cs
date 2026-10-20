using Bunit;
using Bunit.TestDoubles;
using ModRouter.Navigation;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace ModRouter.Test;

/// <summary>
/// The adapter over a real <see cref="NavigationManager"/> (bUnit's fake). Which navigations Blazor reports to the
/// handler, and with what flags, is verified in a real browser (see tools/verify.mjs); this fixture covers the
/// adapter's own mapping.
/// </summary>
[TestFixture]
public sealed class BlazorNavigationAdapterTests
{
    BunitContext _ctx = default!;
    BunitNavigationManager _navigation = default!;
    BlazorNavigationAdapter _adapter = default!;

    [SetUp]
    public void SetUp()
    {
        _ctx = new BunitContext();
        _navigation = (BunitNavigationManager)_ctx.Services.GetRequiredService<NavigationManager>();
        _adapter = new BlazorNavigationAdapter(_navigation);
    }

    [TearDown]
    public async Task TearDown()
    {
        _adapter.Dispose();
        await _ctx.DisposeAsync();
    }

    /// <summary>A NavigationManager like the one of static server-side rendering: it cannot hold a navigation lock.</summary>
    sealed class StaticRenderingNavigationManager : NavigationManager
    {
        public StaticRenderingNavigationManager() => Initialize("http://localhost/", "http://localhost/customers");

        protected override void NavigateToCore(string uri, NavigationOptions options)
            => throw new NotSupportedException("static rendering cannot navigate in this test");
    }

    [Test]
    public void A_navigation_manager_without_navigation_locks_registers_nothing_instead_of_failing()
    {
        using var adapter = new BlazorNavigationAdapter(new StaticRenderingNavigationManager());

        IDisposable? registration = null;

        Assert.DoesNotThrow(() => registration = adapter.RegisterLocationChanging(_ => ValueTask.FromResult(LocationChangingDecision.Allow)));
        Assert.That(registration, Is.Not.Null);
        Assert.DoesNotThrow(() => registration!.Dispose());
        Assert.That(adapter.Uri, Is.EqualTo("http://localhost/customers"));
    }

    [Test]
    public void Exposes_the_current_and_base_uri()
    {
        Assert.That(_adapter.BaseUri, Is.EqualTo(_navigation.BaseUri));
        Assert.That(_adapter.Uri, Is.EqualTo(_navigation.Uri));
    }

    [Test]
    public void Location_changes_are_forwarded()
    {
        var seen = new List<LocationChangedNotification>();
        _adapter.LocationChanged += seen.Add;

        _navigation.NavigateTo("customers/42");

        Assert.That(seen, Has.Count.EqualTo(1));
        Assert.That(seen[0].Location, Does.EndWith("/customers/42"));
    }

    [Test]
    public void Disposing_stops_forwarding_location_changes()
    {
        var seen = new List<LocationChangedNotification>();
        _adapter.LocationChanged += seen.Add;
        _adapter.Dispose();

        _navigation.NavigateTo("customers/42");

        Assert.That(seen, Is.Empty);
    }

    [Test]
    public void NavigateTo_passes_the_options_through()
    {
        _adapter.NavigateTo("customers/7", replaceHistory: true, forceLoad: true, historyState: "state-1");

        var entry = _navigation.History.First();
        Assert.That(entry.Uri, Is.EqualTo("customers/7"));
        Assert.That(entry.Options.ReplaceHistoryEntry, Is.True);
        Assert.That(entry.Options.ForceLoad, Is.True);
        Assert.That(entry.Options.HistoryEntryState, Is.EqualTo("state-1"));
    }

    [Test]
    public void A_registered_handler_sees_the_target_and_can_prevent_the_navigation()
    {
        var requests = new List<LocationChangingRequest>();
        using var registration = _adapter.RegisterLocationChanging(request =>
        {
            requests.Add(request);

            return ValueTask.FromResult(LocationChangingDecision.Prevent);
        });
        var before = _navigation.Uri;

        _navigation.NavigateTo("customers/42");

        Assert.That(requests, Has.Count.EqualTo(1));
        Assert.That(requests[0].TargetLocation, Does.EndWith("customers/42"));
        Assert.That(_navigation.Uri, Is.EqualTo(before), "a prevented navigation does not change the URI");
    }

    [Test]
    public void A_handler_that_allows_lets_the_navigation_through_and_disposing_it_unregisters()
    {
        var calls = 0;
        var registration = _adapter.RegisterLocationChanging(_ =>
        {
            calls++;

            return ValueTask.FromResult(LocationChangingDecision.Allow);
        });

        _navigation.NavigateTo("a");
        registration.Dispose();
        _navigation.NavigateTo("b");

        Assert.That(calls, Is.EqualTo(1));
        Assert.That(_navigation.Uri, Does.EndWith("/b"));
    }
}
