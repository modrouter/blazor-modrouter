using ModRouter.Navigation;

namespace ModRouter.Test;

[TestFixture]
public sealed class BaseUriHelperTests
{
    [TestCase("http://localhost/", "http://localhost/customers/42", "/customers/42", "", null)]
    [TestCase("http://localhost/", "customers/42", "/customers/42", "", null)]
    [TestCase("http://localhost/", "http://localhost/", "/", "", null)]
    [TestCase("http://localhost/", "", "/", "", null)]
    [TestCase("http://localhost/myapp/", "http://localhost/myapp/customers/42", "/customers/42", "", null)]
    [TestCase("http://localhost/myapp/", "customers/42", "/customers/42", "", null)]
    [TestCase("http://localhost/myapp/", "http://localhost/myapp/", "/", "", null)]
    [TestCase("http://localhost/myapp/", "http://localhost/myapp", "/", "", null)]
    [TestCase("http://localhost/", "http://localhost/a/b?x=1&y=2#frag", "/a/b", "?x=1&y=2", "frag")]
    [TestCase("http://localhost/", "customers?tab=a", "/customers", "?tab=a", null)]
    [TestCase("http://localhost/", "http://LOCALHOST/Customers", "/Customers", "", null)]
    [TestCase("http://localhost/", "http://localhost/caf%C3%A9", "/caf%C3%A9", "", null)]
    [TestCase("http://localhost/", "/customers/42", "/customers/42", "", null)]
    [TestCase("http://localhost/", "/customers/42?tab=a#top", "/customers/42", "?tab=a", "top")]
    [TestCase("http://localhost/myapp/", "/myapp/customers/42", "/customers/42", "", null)]
    [TestCase("http://localhost/myapp/", "/myapp", "/", "", null)]
    public void Resolves_to_a_base_relative_location(string baseUri, string uri, string path, string query, string? fragment)
    {
        Assert.That(BaseUriHelper.TryGetBaseRelative(baseUri, uri, out var location), Is.True);
        Assert.That(location, Is.EqualTo(new BaseRelativeLocation(path, query, fragment)));
    }

    [TestCase("http://localhost/myapp/", "http://localhost/other")]
    [TestCase("http://localhost/myapp/", "http://localhost/myapplication/x")]
    [TestCase("http://localhost/", "http://other.example/customers")]
    [TestCase("http://localhost/", "https://localhost/customers")]
    [TestCase("http://localhost:5000/", "http://localhost:5001/customers")]
    [TestCase("http://localhost/myapp/", "/other")]
    [TestCase("http://localhost/myapp/", "/myapplication/x")]
    public void Rejects_uris_outside_the_base(string baseUri, string uri)
    {
        Assert.That(BaseUriHelper.TryGetBaseRelative(baseUri, uri, out _), Is.False);
    }

    [Test]
    public void Rejects_an_invalid_base()
    {
        Assert.That(BaseUriHelper.TryGetBaseRelative("not a uri", "x", out _), Is.False);
    }

    [TestCase("http://localhost/", "customers/42", "http://localhost/customers/42")]
    [TestCase("http://localhost/myapp/", "customers/42", "http://localhost/myapp/customers/42")]
    [TestCase("http://localhost/myapp/", "http://localhost/other", "http://localhost/other")]
    [TestCase("http://localhost/", "", "http://localhost/")]
    [TestCase("http://localhost/", "/customers/42", "http://localhost/customers/42")]
    [TestCase("http://localhost/myapp/", "/myapp/customers/42", "http://localhost/myapp/customers/42")]
    [TestCase("http://localhost/myapp/", "/other", "http://localhost/other")]
    public void ToAbsolute_resolves_relative_uris_against_the_base(string baseUri, string uri, string expected)
    {
        Assert.That(BaseUriHelper.ToAbsolute(baseUri, uri), Is.EqualTo(expected));
    }

    [Test]
    public void A_rooted_target_is_never_parsed_on_its_own()
    {
        // On non-Windows runtimes (including browser-wasm) a string starting with '/' parses as an absolute file: URI,
        // so a standalone absolute parse would judge "/customers/42" to be outside every http base. The helper must
        // resolve against the base instead, which is also what NavigationManager.ToAbsoluteUri does.
        Assert.That(BaseUriHelper.TryGetBaseRelative("http://localhost/", "/customers/42", out var location), Is.True);
        Assert.That(location.Path, Is.EqualTo("/customers/42"));
        Assert.That(new Uri(new Uri("http://localhost/"), "/customers/42").AbsoluteUri, Is.EqualTo(BaseUriHelper.ToAbsolute("http://localhost/", "/customers/42")));
    }
}

[TestFixture]
public sealed class RouteStateStoreTests
{
    static RouteState State(long id, NavigationTrigger trigger = NavigationTrigger.Programmatic)
        => new() { NavigationId = id, AbsoluteUri = "http://localhost/", Path = "/", Trigger = trigger };

    [Test]
    public void Commit_replaces_the_current_state_and_announces_it()
    {
        var store = new RouteStateStore();
        var announced = new List<RouteState>();
        store.Changed += announced.Add;
        Assert.That(store.Current, Is.Null);

        var first = State(1);
        var second = State(2);
        store.Commit(first);
        store.Commit(second);

        Assert.That(store.Current, Is.SameAs(second));
        Assert.That(announced, Is.EqualTo(new[] { first, second }));
    }

    [Test]
    public void WithTrigger_copies_everything_but_the_trigger()
    {
        var original = new RouteState
        {
            NavigationId = 7,
            AbsoluteUri = "http://localhost/a?b=1#c",
            Path = "/a",
            Query = "?b=1",
            Fragment = "c",
            Failure = RouteFailure.NotFound,
            Trigger = NavigationTrigger.Programmatic,
            HistoryEntryState = "s",
        };

        var changed = original.WithTrigger(NavigationTrigger.HistoryRestore);

        Assert.That(changed, Is.Not.SameAs(original));
        Assert.That(changed.Trigger, Is.EqualTo(NavigationTrigger.HistoryRestore));
        Assert.That(
            (changed.NavigationId, changed.AbsoluteUri, changed.Path, changed.Query, changed.Fragment, changed.Failure, changed.HistoryEntryState),
            Is.EqualTo((7L, "http://localhost/a?b=1#c", "/a", "?b=1", "c", RouteFailure.NotFound, "s")));
    }
}
