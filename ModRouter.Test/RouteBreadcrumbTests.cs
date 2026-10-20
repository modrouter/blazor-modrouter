using Bunit;
using ModRouter.Components;
using static ModRouter.Test.ComponentTestKit;

namespace ModRouter.Test;

[TestFixture]
public sealed class RouteBreadcrumbTests
{
    const string OrderId = "4ec9a2a2-1b9f-47b9-8d1a-6e8b2f2b9d77";

    BunitContext _ctx = default!;
    bool _expectsRenderException;

    [SetUp]
    public void SetUp()
    {
        _ctx = new BunitContext();
        _expectsRenderException = false;
    }

    [TearDown]
    public async Task TearDown()
    {
        if (!_expectsRenderException)
        {
            Assert.That(_ctx.Renderer.UnhandledException.IsCompleted, Is.False, () => "a render exception was swallowed: " + _ctx.Renderer.UnhandledException.Result);
        }

        await _ctx.DisposeAsync();
    }

    IRenderedComponent<RouteBreadcrumb> Render(Action<ComponentParameterCollectionBuilder<RouteBreadcrumb>>? parameters = null)
        => _ctx.Render<RouteBreadcrumb>(p => parameters?.Invoke(p));

    /// <summary>One entry per crumb: its text, and its href when it is a link (the last crumb is not).</summary>
    static IReadOnlyList<(string Text, string? Href)> Crumbs(IRenderedComponent<RouteBreadcrumb> cut)
        => [.. All(cut, "nav ol > li").Select(item =>
        {
            var crumb = item.QuerySelector("a, span:not([aria-hidden])")!;

            return (crumb.TextContent.Trim(), crumb.GetAttribute("href"));
        })];

    [Test]
    public async Task Each_titled_level_is_a_crumb_and_every_crumb_but_the_last_is_a_link()
    {
        await using var router = await RouterHarness.StartAsync(_ctx, uri: "http://localhost/customers/42");

        var cut = Render();

        Assert.That(Crumbs(cut), Is.EqualTo(new (string, string?)[] { ("Customers", "/customers"), ("Customer 42", null) }));
        Assert.That(One(cut, "[aria-current=page]").TextContent, Is.EqualTo("Customer 42"));
    }

    [Test]
    public async Task A_three_level_chain_uses_the_matched_paths_as_hrefs()
    {
        await using var router = await RouterHarness.StartAsync(_ctx, uri: $"http://localhost/customers/42/orders/{OrderId}");

        var cut = Render();

        Assert.That(Crumbs(cut), Is.EqualTo(new (string, string?)[]
        {
            ("Customers", "/customers"),
            ("Customer 42", "/customers/42"),
            ($"Order {OrderId}", null),
        }));
    }

    [Test]
    public async Task Hrefs_include_the_base_path()
    {
        await using var router = await RouterHarness.StartAsync(_ctx, uri: "http://localhost/myapp/customers/42", baseUri: "http://localhost/myapp/");

        var cut = Render(p => p.Add(b => b.Home, "Home"));

        Assert.That(Crumbs(cut), Is.EqualTo(new (string, string?)[] { ("Home", "/myapp/"), ("Customers", "/myapp/customers"), ("Customer 42", null) }));
    }

    [Test]
    public async Task A_Home_crumb_is_not_added_when_the_trail_already_starts_at_the_application_root()
    {
        await using var router = await RouterHarness.StartAsync(_ctx, uri: "http://localhost/");

        var cut = Render(p => p.Add(b => b.Home, "Home"));

        Assert.That(Crumbs(cut), Is.EqualTo(new (string, string?)[] { ("Home", null) }), "a titled root route is its own home crumb");
    }

    [Test]
    public async Task A_Home_crumb_is_not_added_at_the_root_under_a_base_path_either()
    {
        await using var router = await RouterHarness.StartAsync(_ctx, uri: "http://localhost/myapp/", baseUri: "http://localhost/myapp/");

        var cut = Render(p => p.Add(b => b.Home, "Start"));

        Assert.That(Crumbs(cut), Is.EqualTo(new (string, string?)[] { ("Home", null) }));
    }

    [Test]
    public async Task An_index_route_adds_no_crumb_of_its_own()
    {
        await using var router = await RouterHarness.StartAsync(_ctx, uri: "http://localhost/customers");

        var cut = Render();

        Assert.That(Crumbs(cut), Is.EqualTo(new (string, string?)[] { ("Customers", null) }));
    }

    [Test]
    public async Task The_trail_follows_navigation()
    {
        await using var router = await RouterHarness.StartAsync(_ctx, uri: "http://localhost/about");
        var cut = Render();
        Assert.That(Crumbs(cut), Is.EqualTo(new (string, string?)[] { ("About", null) }));

        await router.GoAsync("customers/7");

        Assert.That(Crumbs(cut), Is.EqualTo(new (string, string?)[] { ("Customers", "/customers"), ("Customer 7", null) }));
    }

    [Test]
    public async Task A_url_that_matches_nothing_renders_no_trail()
    {
        await using var router = await RouterHarness.StartAsync(_ctx, uri: "http://localhost/nope");

        var cut = Render(p => p.Add(b => b.Home, "Home"));

        Assert.That(All(cut, "nav"), Is.Empty);
        Assert.That(cut.Markup.Trim(), Is.Empty);
    }

    [Test]
    public async Task Route_metadata_can_relabel_and_hide_levels()
    {
        await using var router = await RouterHarness.StartAsync(
            _ctx,
            map => map.Route("/customers", typeof(CustomersProbe), title: "Customers", children: customers => customers
                .Route("{id:int}", typeof(DetailsProbe), p => $"Customer {p["id"]}", children: customer => customer
                    .Route("orders/{orderId:guid}", typeof(OrderProbe), title: "Order").Breadcrumb(p => $"Order of customer {p["id"]}"))
                .Breadcrumb(hidden: true))
                .Breadcrumb("Everyone"),
            uri: $"http://localhost/customers/42/orders/{OrderId}");

        var cut = Render();

        Assert.That(Crumbs(cut), Is.EqualTo(new (string, string?)[] { ("Everyone", "/customers"), ("Order of customer 42", null) }));
    }

    [Test]
    public async Task A_long_trail_keeps_its_first_crumbs_and_the_last_ones_and_collapses_the_middle()
    {
        await using var router = await RouterHarness.StartAsync(_ctx, uri: $"http://localhost/customers/42/orders/{OrderId}");

        var cut = Render(p => p.Add(b => b.Home, "Home").Add(b => b.MaxItems, 3));

        Assert.That(Crumbs(cut), Is.EqualTo(new (string, string?)[] { ("Home", "/"), (RouteBreadcrumb.Ellipsis, null), ($"Order {OrderId}", null) }));
    }

    [Test]
    public async Task A_trail_that_fits_is_left_alone()
    {
        await using var router = await RouterHarness.StartAsync(_ctx, uri: "http://localhost/customers/42");

        var cut = Render(p => p.Add(b => b.Home, "Home").Add(b => b.MaxItems, 3));

        Assert.That(Crumbs(cut).Select(c => c.Text), Is.EqualTo(new[] { "Home", "Customers", "Customer 42" }));
    }

    [Test]
    public async Task A_larger_MaxItems_leaves_a_four_crumb_trail_alone()
    {
        await using var router = await RouterHarness.StartAsync(_ctx, uri: $"http://localhost/customers/42/orders/{OrderId}");

        var cut = Render(p => p.Add(b => b.Home, "Home").Add(b => b.MaxItems, 4));

        Assert.That(Crumbs(cut).Select(c => c.Text), Is.EqualTo(new[] { "Home", "Customers", "Customer 42", $"Order {OrderId}" }));
    }

    [Test]
    public async Task MaxItems_below_three_is_rejected()
    {
        _expectsRenderException = true;
        await using var router = await RouterHarness.StartAsync(_ctx);

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => Render(p => p.Add(b => b.MaxItems, 2)))!;

        Assert.That(exception.Message, Does.Contain("at least 3"));
    }

    [Test]
    public async Task The_separator_class_and_attributes_pass_through_to_the_nav()
    {
        await using var router = await RouterHarness.StartAsync(_ctx, uri: "http://localhost/customers/42");

        var cut = Render(p => p
            .Add(b => b.Separator, "/")
            .Add(b => b.Class, "my-trail")
            .Add(b => b.AriaLabel, "You are here")
            .AddUnmatched("data-testid", "trail"));

        Assert.That(All(cut, "nav span[aria-hidden=true]").Single().TextContent, Is.EqualTo("/"));
        Assert.That(One(cut, "nav").GetAttribute("data-testid"), Is.EqualTo("trail"));
        Assert.That(One(cut, "nav").GetAttribute("aria-label"), Is.EqualTo("You are here"));
        Assert.That(One(cut, "nav").ClassList, Does.Contain("my-trail"));
    }

    [Test]
    public async Task The_nav_is_labelled_Breadcrumb_by_default_and_the_current_page_is_marked()
    {
        await using var router = await RouterHarness.StartAsync(_ctx, uri: "http://localhost/customers/42");

        var cut = Render();

        Assert.That(One(cut, "nav").GetAttribute("aria-label"), Is.EqualTo("Breadcrumb"));
        Assert.That(One(cut, "ol > li:last-child [aria-current=page]").TextContent, Is.EqualTo("Customer 42"));
        Assert.That(All(cut, "ol > li:last-child span[aria-hidden=true]"), Is.Empty, "no separator after the current page");
    }

    [Test]
    public async Task A_disposed_breadcrumb_stops_listening()
    {
        await using var router = await RouterHarness.StartAsync(_ctx, uri: "http://localhost/about");
        var cut = Render();

        await _ctx.DisposeComponentsAsync();

        Assert.DoesNotThrowAsync(() => router.GoAsync("customers/5"));
        Assert.That(cut.IsDisposed, Is.True);
    }
}
