using Bunit;
using ModRouter.Components;
using ModRouter.Core.Generation;
using static ModRouter.Test.ComponentTestKit;

namespace ModRouter.Test;

[TestFixture]
public sealed class RouteLinkTests
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

    IRenderedComponent<RouteLink> Render(Action<ComponentParameterCollectionBuilder<RouteLink>> parameters)
        => _ctx.Render<RouteLink>(p =>
        {
            p.AddChildContent("go");
            parameters(p);
        });

    static string? Attribute(IRenderedComponent<RouteLink> cut, string name) => One(cut, "a").GetAttribute(name);

    // ---- href --------------------------------------------------------------------------------------------------

    [Test]
    public async Task A_route_name_and_parameters_generate_the_href()
    {
        await using var router = await RouterHarness.StartAsync(_ctx);

        var cut = Render(p => p.Add(l => l.Route, "customer").Add(l => l.Parameters, new { id = 42 }));

        Assert.That(Attribute(cut, "href"), Is.EqualTo("/customers/42"));
        Assert.That(One(cut, "a").TextContent, Is.EqualTo("go"));
    }

    [Test]
    public async Task The_href_includes_the_base_path_query_and_fragment()
    {
        await using var router = await RouterHarness.StartAsync(_ctx, uri: "http://localhost/myapp/", baseUri: "http://localhost/myapp/");

        var cut = Render(p => p
            .Add(l => l.Route, "customer")
            .Add(l => l.Parameters, new Dictionary<string, object?> { ["id"] = 7 })
            .Add(l => l.Query, new Dictionary<string, object?> { ["tab"] = "orders" })
            .Add(l => l.Fragment, "top"));

        Assert.That(Attribute(cut, "href"), Is.EqualTo("/myapp/customers/7?tab=orders#top"));
    }

    [Test]
    public async Task A_plain_Href_is_used_as_given_and_a_route_name_wins_over_it()
    {
        await using var router = await RouterHarness.StartAsync(_ctx);

        Assert.That(Attribute(Render(p => p.Add(l => l.Href, "about")), "href"), Is.EqualTo("about"));
        Assert.That(Attribute(Render(p => p.Add(l => l.Route, "about").Add(l => l.Href, "elsewhere")), "href"), Is.EqualTo("/about"));
    }

    [Test]
    public async Task Ambient_values_fill_what_the_link_does_not_give()
    {
        await using var router = await RouterHarness.StartAsync(_ctx, uri: $"http://localhost/customers/42/orders/{OrderId}");

        var cut = Render(p => p
            .Add(l => l.Route, "order")
            .Add(l => l.Parameters, new { orderId = "11111111-1111-1111-1111-111111111111" })
            .Add(l => l.UseAmbientValues, true));

        Assert.That(Attribute(cut, "href"), Is.EqualTo("/customers/42/orders/11111111-1111-1111-1111-111111111111"));
    }

    [Test]
    public async Task An_unknown_route_name_fails_while_rendering_with_a_clear_message()
    {
        _expectsRenderException = true;
        await using var router = await RouterHarness.StartAsync(_ctx);

        var exception = Assert.Throws<RouteUrlGenerationException>(() => Render(p => p.Add(l => l.Route, "nope")))!;

        Assert.That(exception.Message, Does.Contain("nope"));
    }

    // ---- current page ------------------------------------------------------------------------------------------

    [Test]
    public async Task A_link_to_the_current_page_is_active_and_says_so()
    {
        await using var router = await RouterHarness.StartAsync(_ctx, uri: "http://localhost/customers/42");

        var cut = Render(p => p.Add(l => l.Route, "customer").Add(l => l.Parameters, new { id = 42 }));

        Assert.That(Attribute(cut, "aria-current"), Is.EqualTo("page"));
        Assert.That(One(cut, "a").ClassList, Does.Contain(RouteLink.ActiveClass));
    }

    [Test]
    public async Task A_link_to_a_different_parameter_value_is_not_active()
    {
        await using var router = await RouterHarness.StartAsync(_ctx, uri: "http://localhost/customers/42");

        var cut = Render(p => p.Add(l => l.Route, "customer").Add(l => l.Parameters, new { id = 43 }));

        Assert.That(Attribute(cut, "aria-current"), Is.Null);
        Assert.That(One(cut, "a").ClassList, Does.Not.Contain(RouteLink.ActiveClass));
    }

    [Test]
    public async Task A_link_to_an_ancestor_is_active_by_prefix_and_marked_true_not_page()
    {
        await using var router = await RouterHarness.StartAsync(_ctx, uri: "http://localhost/customers/42");

        var cut = Render(p => p.Add(l => l.Route, "customers"));

        Assert.That(Attribute(cut, "aria-current"), Is.EqualTo("true"));
        Assert.That(One(cut, "a").ClassList, Does.Contain(RouteLink.ActiveClass));
    }

    [Test]
    public async Task Exact_matching_ignores_ancestors()
    {
        await using var router = await RouterHarness.StartAsync(_ctx, uri: "http://localhost/customers/42");

        var cut = Render(p => p.Add(l => l.Route, "customers").Add(l => l.Match, RouteLinkMatch.Exact));

        Assert.That(Attribute(cut, "aria-current"), Is.Null);
    }

    [Test]
    public async Task The_index_route_and_its_parent_are_both_the_current_page_at_the_parents_url()
    {
        await using var router = await RouterHarness.StartAsync(_ctx, uri: "http://localhost/customers");

        var list = Render(p => p.Add(l => l.Route, "customer-list").Add(l => l.Match, RouteLinkMatch.Exact));
        var parent = Render(p => p.Add(l => l.Route, "customers").Add(l => l.Match, RouteLinkMatch.Exact));
        var parentByPrefix = Render(p => p.Add(l => l.Route, "customers"));

        Assert.That(Attribute(list, "aria-current"), Is.EqualTo("page"));
        Assert.That(Attribute(parent, "aria-current"), Is.EqualTo("page"), "the index route renders at its parent's URL, so a link to the parent is a link to this very page");
        Assert.That(Attribute(parentByPrefix, "aria-current"), Is.EqualTo("page"));
    }

    [Test]
    public async Task A_link_to_a_grandparent_of_an_index_leaf_is_still_only_an_ancestor()
    {
        await using var router = await RouterHarness.StartAsync(
            _ctx,
            map => map.Route("/customers", typeof(CustomersProbe), name: "customers", title: "Customers", children: customers => customers
                .Route("{id:int}", typeof(DetailsProbe), p => $"Customer {p["id"]}", name: "customer", children: customer => customer
                    .Index(typeof(ListProbe), name: "customer-home"))),
            uri: "http://localhost/customers/42");

        var parent = Render(p => p.Add(l => l.Route, "customer").Add(l => l.Parameters, new { id = 42 }));
        var grandparent = Render(p => p.Add(l => l.Route, "customers"));

        Assert.That(Attribute(parent, "aria-current"), Is.EqualTo("page"));
        Assert.That(Attribute(grandparent, "aria-current"), Is.EqualTo("true"));
    }

    [Test]
    public async Task Parameter_values_compare_by_their_text_so_a_string_matches_an_int()
    {
        await using var router = await RouterHarness.StartAsync(_ctx, uri: "http://localhost/customers/42");

        var cut = Render(p => p.Add(l => l.Route, "customer").Add(l => l.Parameters, new Dictionary<string, object?> { ["id"] = "42" }));

        Assert.That(Attribute(cut, "aria-current"), Is.EqualTo("page"));
    }

    [Test]
    public async Task A_link_follows_navigation_without_being_rerendered_by_its_parent()
    {
        await using var router = await RouterHarness.StartAsync(_ctx, uri: "http://localhost/about");
        var cut = Render(p => p.Add(l => l.Route, "customer").Add(l => l.Parameters, new { id = 5 }));
        Assert.That(Attribute(cut, "aria-current"), Is.Null);

        await router.GoAsync("customers/5");

        Assert.That(Attribute(cut, "aria-current"), Is.EqualTo("page"));

        await router.GoAsync("about");

        Assert.That(Attribute(cut, "aria-current"), Is.Null);
        Assert.That(One(cut, "a").ClassList, Does.Not.Contain(RouteLink.ActiveClass));
    }

    // ---- Href links --------------------------------------------------------------------------------------------

    [Test]
    public async Task An_Href_link_is_exact_by_default_and_ignores_query_and_a_trailing_slash()
    {
        await using var router = await RouterHarness.StartAsync(_ctx, uri: "http://localhost/customers?x=1");

        Assert.That(Attribute(Render(p => p.Add(l => l.Href, "customers")), "aria-current"), Is.EqualTo("page"));
        Assert.That(Attribute(Render(p => p.Add(l => l.Href, "customers/")), "aria-current"), Is.EqualTo("page"));
        Assert.That(Attribute(Render(p => p.Add(l => l.Href, "about")), "aria-current"), Is.Null);
    }

    [Test]
    public async Task An_Href_link_can_match_by_prefix_on_a_segment_boundary()
    {
        await using var router = await RouterHarness.StartAsync(_ctx, uri: "http://localhost/customers/42");

        var prefix = Render(p => p.Add(l => l.Href, "customers").Add(l => l.Match, RouteLinkMatch.Prefix));
        var partial = Render(p => p.Add(l => l.Href, "custom").Add(l => l.Match, RouteLinkMatch.Prefix));

        Assert.That(Attribute(prefix, "aria-current"), Is.EqualTo("true"));
        Assert.That(Attribute(partial, "aria-current"), Is.Null, "custom is not a segment prefix of customers");
    }

    [Test]
    public async Task A_rooted_Href_under_a_base_path_is_compared_against_the_base_relative_path()
    {
        await using var router = await RouterHarness.StartAsync(_ctx, uri: "http://localhost/myapp/about", baseUri: "http://localhost/myapp/");

        var cut = Render(p => p.Add(l => l.Href, "/myapp/about"));

        Assert.That(Attribute(cut, "aria-current"), Is.EqualTo("page"));
    }

    [Test]
    public async Task An_external_Href_is_never_active()
    {
        await using var router = await RouterHarness.StartAsync(_ctx, uri: "http://localhost/about");

        var cut = Render(p => p.Add(l => l.Href, "https://elsewhere.example/about"));

        Assert.That(Attribute(cut, "aria-current"), Is.Null);
        Assert.That(Attribute(cut, "href"), Is.EqualTo("https://elsewhere.example/about"));
    }

    // ---- pass-through ------------------------------------------------------------------------------------------

    [Test]
    public async Task Disabled_and_Target_and_the_callers_own_attributes_pass_through_to_the_link()
    {
        await using var router = await RouterHarness.StartAsync(_ctx);

        var cut = Render(p => p
            .Add(l => l.Route, "about")
            .Add(l => l.Target, "_blank")
            .Add(l => l.Class, "my-link")
            .AddUnmatched("data-extra", "yes"));

        Assert.That(Attribute(cut, "target"), Is.EqualTo("_blank"));
        Assert.That(Attribute(cut, "data-extra"), Is.EqualTo("yes"));
        Assert.That(One(cut, "a").ClassList, Does.Contain("my-link"));

        var disabled = Render(p => p.Add(l => l.Route, "about").Add(l => l.Disabled, true));

        Assert.That(Attribute(disabled, "aria-disabled"), Is.EqualTo("true"));
        Assert.That(Attribute(disabled, "role"), Is.EqualTo("link"));
        Assert.That(Attribute(disabled, "href"), Is.Null);
        Assert.That(Attribute(disabled, "target"), Is.Null);
    }

    [Test]
    public async Task The_active_class_keeps_the_callers_class()
    {
        await using var router = await RouterHarness.StartAsync(_ctx, uri: "http://localhost/about");

        var cut = Render(p => p.Add(l => l.Route, "about").Add(l => l.Class, "my-link"));

        Assert.That(One(cut, "a").ClassList, Does.Contain("my-link").And.Contain(RouteLink.ActiveClass));
    }

    [Test]
    public async Task The_link_renders_a_plain_anchor_and_passes_style_and_title_through()
    {
        await using var router = await RouterHarness.StartAsync(_ctx);

        var cut = Render(p => p.Add(l => l.Route, "about").Add(l => l.Style, "color: red").AddUnmatched("title", "About us"));

        Assert.That(One(cut, "a").GetAttribute("style"), Is.EqualTo("color: red"));
        Assert.That(One(cut, "a").GetAttribute("title"), Is.EqualTo("About us"));
        Assert.That(All(cut, "a > *"), Is.Empty);
    }

    [Test]
    public async Task A_disposed_link_stops_listening()
    {
        await using var router = await RouterHarness.StartAsync(_ctx, uri: "http://localhost/about");
        var cut = Render(p => p.Add(l => l.Route, "about"));

        await _ctx.DisposeComponentsAsync();

        Assert.DoesNotThrowAsync(() => router.GoAsync("customers/5"));
        Assert.That(cut.IsDisposed, Is.True);
    }
}
