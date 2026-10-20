using Bunit;
using ModRouter.Components;
using ModRouter.Core.Breadcrumbs;
using ModRouter.Core.Compilation;
using ModRouter.Core.Definitions;
using ModRouter.Core.Titles;
using ModRouter.Navigation;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using static ModRouter.Test.ComponentTestKit;

namespace ModRouter.Test;

/// <summary>A pathless group's layout: a wrapper with an outlet, like a route component that hosts child routes.</summary>
public sealed class GroupLayoutProbe : ProbeBase
{
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "id", "group");
        Outlet(builder, 2);
        builder.CloseElement();
    }
}

/// <summary>
/// A pathless group (<c>RouteMap.Group</c>): a layout level with no URL segment, so the routes inside it keep their own
/// paths but share the layout, the guards and the loader declared on the group.
/// </summary>
[TestFixture]
public sealed class GroupRouteTests
{
    const string Base = "http://localhost/";

    static CompiledRouteTable GroupedTable() => RouteTestKit.Compile(map => map
        .Route("/", typeof(HomeProbe), name: "home", title: "Home")
        .Group(typeof(GroupLayoutProbe), account => account
            .Route("profile", typeof(ShowProbe), name: "profile", title: "Profile")
            .Route("settings/{section}", typeof(ShowProbe), name: "settings", title: "Settings")));

    [Test]
    public void A_group_adds_a_level_to_the_chain_but_nothing_to_the_path()
    {
        var table = GroupedTable();

        var chain = RouteTestKit.Match(table, "/profile");

        Assert.That(chain.Matches, Has.Count.EqualTo(2));
        Assert.That(chain.Matches[0].Route.IsGroup, Is.True);
        Assert.That(chain.Leaf.RouteName, Is.EqualTo("profile"));
        Assert.That(chain.MatchedTemplate, Is.EqualTo("/profile"));
        Assert.That(RouteTestKit.Match(table, "/settings/billing").Leaf.Parameters["section"], Is.EqualTo("billing"));
    }

    [Test]
    public void A_group_is_never_matched_by_itself_and_leaves_the_other_routes_alone()
    {
        var table = GroupedTable();

        Assert.That(RouteTestKit.Match(table, "/").Matches, Has.Count.EqualTo(1));
        Assert.That(table.Match("/nope").IsMatch, Is.False);
    }

    [Test]
    public void The_child_routes_of_a_group_generate_urls_without_the_group()
    {
        var table = GroupedTable();

        Assert.That(table.GenerateUrl("profile"), Is.EqualTo("/profile"));
        Assert.That(table.GenerateUrl("settings", new Dictionary<string, object?> { ["section"] = "billing" }), Is.EqualTo("/settings/billing"));
    }

    [Test]
    public void A_group_can_hold_the_index_route_of_its_parent_path()
    {
        var table = RouteTestKit.Compile(map => map
            .Route("/app", typeof(CustomersProbe), name: "app", title: "App", children: app => app
                .Group(typeof(GroupLayoutProbe), inner => inner
                    .Index(typeof(ListProbe), name: "app-home")
                    .Route("reports", typeof(ShowProbe), name: "reports"))));

        var home = RouteTestKit.Match(table, "/app");

        Assert.That(RouteTestKit.Names(home), Is.EqualTo(new[] { "app", "(unnamed)", "app-home" }));
        Assert.That(RouteTestKit.Leaf(table, "/app/reports"), Is.EqualTo("reports"));
        Assert.That(RouteTitles.Effective(home), Is.EqualTo("App"), "titles pass over the group and the index to the nearest titled level");
    }

    [Test]
    public void Breadcrumbs_leave_the_group_out_even_when_it_declares_a_label()
    {
        var table = RouteTestKit.Compile(map => map
            .Route("/", typeof(HomeProbe), name: "home", title: "Home")
            .Group(typeof(GroupLayoutProbe), account => account
                .Route("profile", typeof(ShowProbe), name: "profile", title: "Profile"))
            .Breadcrumb("Account"));

        var crumbs = BreadcrumbTrail.From(RouteTestKit.Match(table, "/profile"));

        Assert.That(crumbs.Select(c => c.Label), Is.EqualTo(new[] { "Profile" }));
    }

    [Test]
    public void A_group_with_one_level_inside_another_group_nests_both_layouts()
    {
        var table = RouteTestKit.Compile(map => map
            .Group(typeof(GroupLayoutProbe), outer => outer
                .Group(typeof(CustomersProbe), inner => inner
                    .Route("deep", typeof(ShowProbe), name: "deep"))));

        var chain = RouteTestKit.Match(table, "/deep");

        Assert.That(chain.Matches.Select(m => m.Route.Component), Is.EqualTo(new[] { typeof(GroupLayoutProbe), typeof(CustomersProbe), typeof(ShowProbe) }));
    }

    [Test]
    public async Task A_guard_on_a_group_guards_every_route_inside_it_and_nothing_else()
    {
        var table = RouteTestKit.Compile(map => map
            .Route("/", typeof(HomeProbe), name: "home")
            .Group(typeof(GroupLayoutProbe), account => account
                .Route("profile", typeof(ShowProbe), name: "profile")
                .Route("billing", typeof(ShowProbe), name: "billing"))
            .Guard<AuthGuard>());

        using var p = Pipeline.Create(table);
        await p.Coordinator.NavigateInitialAsync();

        await p.GoAsync("profile");
        await p.GoAsync("billing");
        await p.GoAsync("/");

        Assert.That(p.Guards.Calls, Is.EqualTo(new[] { "auth@/", "auth@/" }), "the group's own level is asked once for each of the two grouped routes");
    }

    // ---- validation --------------------------------------------------------------------------------------------

    static RouteDefinition Group(Action<RouteDefinitionBuilder>? configure = null)
    {
        var builder = new RouteDefinitionBuilder();
        configure?.Invoke(builder);

        return new RouteDefinition
        {
            Template = builder.Template,
            Name = builder.Name,
            Component = typeof(GroupLayoutProbe),
            StaticTitle = builder.Title,
            Aliases = builder.Aliases,
            IsGroup = true,
            Children = builder.Children,
        };
    }

    sealed class RouteDefinitionBuilder
    {
        public string Template { get; set; } = string.Empty;

        public string? Name { get; set; }

        public string? Title { get; set; }

        public string[] Aliases { get; set; } = [];

        public IReadOnlyList<RouteDefinition> Children { get; set; } =
        [
            new RouteDefinition { Template = "child", Component = typeof(ShowProbe), Name = "child" },
        ];
    }

    [TestCase("a group without children")]
    [TestCase("a group with a name")]
    [TestCase("a group with a title")]
    [TestCase("a group with an alias")]
    [TestCase("a group with its own template")]
    public void A_malformed_group_is_a_startup_error(string scenario)
    {
        var group = Group(b =>
        {
            switch (scenario)
            {
                case "a group without children":
                    b.Children = [];
                    break;
                case "a group with a name":
                    b.Name = "account";
                    break;
                case "a group with a title":
                    b.Title = "Account";
                    break;
                case "a group with an alias":
                    b.Aliases = ["/acct"];
                    break;
                default:
                    b.Template = "account";
                    break;
            }
        });

        var exception = Assert.Throws<RouteCompilationException>(() => RouteCompiler.Compile([group]))!;

        Assert.That(exception.Errors.Select(e => e.Code), Does.Contain("RT0027"));
    }

    // ---- rendering ---------------------------------------------------------------------------------------------

    BunitContext _ctx = default!;
    FakeNavigationAdapter _adapter = default!;

    [SetUp]
    public void SetUp()
    {
        Probes.Reset();
        _ctx = new BunitContext();
    }

    [TearDown]
    public async Task TearDown()
    {
        Assert.That(_ctx.Renderer.UnhandledException.IsCompleted, Is.False, () => "a render exception was swallowed: " + _ctx.Renderer.UnhandledException.Result);

        await _ctx.DisposeAsync();
    }

    IRenderedComponent<Components.ModRouter> Mount(Action<RouteMap>? routes, string uri, RenderFragment? markup = null)
    {
        if (routes is null)
        {
            _ctx.Services.AddModRouter();
        }
        else
        {
            _ctx.Services.AddModRouter(routes);
        }

        _adapter = new FakeNavigationAdapter(Base, uri) { ExecuteNavigateTo = true };
        _ctx.Services.AddSingleton<INavigationPlatformAdapter>(_adapter);

        return _ctx.Render<Components.ModRouter>(p =>
        {
            p.Add(r => r.DefaultLayout, typeof(MainLayoutProbe));

            if (markup is not null)
            {
                p.Add(r => r.Routes, markup);
            }
        });
    }

    async Task GoAsync(string uri)
    {
        await _adapter.NavigateAsync(uri, intercepted: true);
        await _adapter.SettledAsync();
    }

    [Test]
    public async Task A_grouped_route_renders_inside_the_groups_layout_and_an_ungrouped_one_does_not()
    {
        var cut = Mount(map => map
            .Route("/", typeof(HomeProbe), name: "home")
            .Group(typeof(GroupLayoutProbe), account => account.Route("profile", typeof(ShowProbe), name: "profile")), "http://localhost/profile");

        await EventuallyAsync(() => All(cut, "#group #show").Count, Is.EqualTo(1));

        await GoAsync("/");

        await EventuallyAsync(() => All(cut, "#home").Count, Is.EqualTo(1));
        Assert.That(All(cut, "#group"), Is.Empty);

        await GoAsync("profile");

        await EventuallyAsync(() => All(cut, "#group #show").Count, Is.EqualTo(1));
    }

    [Test]
    public async Task Moving_between_routes_of_one_group_keeps_the_layout_instance()
    {
        var cut = Mount(map => map
            .Group(typeof(GroupLayoutProbe), account => account
                .Route("profile", typeof(ShowProbe), name: "profile")
                .Route("billing", typeof(HomeProbe), name: "billing")), "http://localhost/profile");

        await EventuallyAsync(() => All(cut, "#group #show").Count, Is.EqualTo(1));

        await GoAsync("billing");
        await EventuallyAsync(() => All(cut, "#group #home").Count, Is.EqualTo(1));

        Assert.That(Probes.Count("ctor:GroupLayoutProbe"), Is.EqualTo(1), "the group's layout is the shared level: it is not rebuilt");
    }

    static RenderFragment GroupEntry(RenderFragment children)
        => builder =>
        {
            builder.OpenComponent<ModRouteEntry>(0);
            builder.AddComponentParameter(1, nameof(ModRouteEntry.Component), typeof(GroupLayoutProbe));
            builder.AddComponentParameter(2, nameof(ModRouteEntry.Group), true);
            builder.AddComponentParameter(3, nameof(ModRouteEntry.ChildContent), children);
            builder.CloseComponent();
        };

    static RenderFragment ChildEntry(string path, Type component, string name)
        => builder =>
        {
            builder.OpenComponent<ModRouteEntry>(0);
            builder.AddComponentParameter(1, nameof(ModRouteEntry.Path), path);
            builder.AddComponentParameter(2, nameof(ModRouteEntry.Component), component);
            builder.AddComponentParameter(3, nameof(ModRouteEntry.Name), name);
            builder.CloseComponent();
        };

    [Test]
    public async Task A_group_declared_in_markup_wraps_its_nested_entries()
    {
        var cut = Mount(null, "http://localhost/profile", GroupEntry(ChildEntry("profile", typeof(ShowProbe), "profile")));

        await EventuallyAsync(() => All(cut, "#group #show").Count, Is.EqualTo(1));
    }

    [Test]
    public void A_markup_group_with_a_path_is_a_startup_error()
    {
        var node = new RouteEntryNode { Path = "account", Component = typeof(GroupLayoutProbe), IsGroup = true };
        node.Children.Add(new RouteEntryNode { Path = "profile", Component = typeof(ShowProbe) });
        var errors = new List<RouteCompilationError>();

        node.ToDefinition(errors);

        Assert.That(errors.Select(e => e.Message), Has.Some.Contains("Group entry"));
    }
}
