using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Bunit;
using ModRouter.Components;
using ModRouter.Core.Compilation;
using ModRouter.Core.Definitions;
using ModRouter.Core.Loading;
using ModRouter.Discovery;
using ModRouter.Navigation;
using ModRouter.Test.Pages;
using ModRouter.Test.Pages.App;
using Microsoft.Extensions.DependencyInjection;
using static ModRouter.Test.ComponentTestKit;

namespace ModRouter.Test;

/// <summary>
/// <c>AddPages</c>: every routable component of an assembly (a non-abstract, non-generic <c>IComponent</c> with
/// <c>[Route]</c>, not <c>[ExcludeFromInteractiveRouting]</c>) becomes a route definition whose name, title, breadcrumb,
/// guards, loader, focus, announcement, index and nesting come from the <c>Route*</c> attributes.
/// </summary>
[TestFixture]
public sealed class PageDiscoveryTests
{
    static readonly Assembly Fixtures = typeof(Pages.App.HomePage).Assembly;

    /// <summary>Selects one fixture set: the components in <c>ModRouter.Test.Pages.{set}</c>.</summary>
    static Func<Type, bool> Set(string set) => type => type.Namespace == $"ModRouter.Test.Pages.{set}";

    static IReadOnlyList<RouteDefinition> Discover(string set) => RouteMap.Create().AddPages(Fixtures, Set(set)).Build();

    static RouteDefinition Named(IEnumerable<RouteDefinition> definitions, string name)
        => Flatten(definitions).Single(d => d.Name == name);

    static IEnumerable<RouteDefinition> Flatten(IEnumerable<RouteDefinition> definitions)
        => definitions.SelectMany(d => Flatten(d.Children).Prepend(d));

    static RouteCompilationException Fails(string set, Action<RouteMap>? before = null)
        => Assert.Throws<RouteCompilationException>(() =>
        {
            var map = RouteMap.Create();
            before?.Invoke(map);
            RouteCompiler.Compile(map.AddPages(Fixtures, Set(set)).Build());
        })!;

    [SetUp]
    public void SetUp() => RecordingGuard.Calls.Clear();

    // ---- mapping ------------------------------------------------------------------------------------------------

    [Test]
    public void Discovers_the_routable_components_of_the_set_as_top_level_routes_in_a_stable_order()
    {
        var definitions = Discover("App");

        Assert.That(definitions.Select(d => d.Component.Name), Is.EquivalentTo(new[] { "HomePage", "OrgChartPage", "CustomersPage", "VendorsPage", "FilteredPage" }));
        Assert.That(definitions.Select(d => d.Component), Is.EqualTo(Discover("App").Select(d => d.Component)), "the order is deterministic");
        Assert.That(definitions.Select(d => d.Component), Is.All.Matches<Type>(t => typeof(Microsoft.AspNetCore.Components.IComponent).IsAssignableFrom(t)));
    }

    [Test]
    public void Name_title_and_source_come_from_the_attributes()
    {
        var home = Named(Discover("App"), "home");

        Assert.That(home.Template, Is.EqualTo("/"));
        Assert.That(home.Component, Is.EqualTo(typeof(Pages.App.HomePage)));
        Assert.That(home.StaticTitle, Is.EqualTo("Home"));
        Assert.That(home.Title, Is.Null);
        Assert.That(home.Source, Is.EqualTo("page: ModRouter.Test.Pages.App.HomePage (ModRouter.Test)"));
    }

    [Test]
    public void The_first_Route_is_the_template_and_the_others_are_aliases()
    {
        var orgChart = Named(Discover("App"), "org-chart");

        Assert.That(orgChart.Template, Is.EqualTo("/org-chart"));
        Assert.That(orgChart.Aliases, Is.EqualTo(new[] { "/org-chart/{employeeId:int}" }));
        Assert.That(orgChart.Metadata.Contains<RouteAliasMetadata>(), Is.False, "the alias behavior is the router default");
    }

    [Test]
    public void Breadcrumb_guards_loader_focus_announce_and_canonical_alias_become_metadata()
    {
        var definitions = Discover("App");
        var customers = Named(definitions, "customers");
        var customer = Named(definitions, "customer");
        var order = Named(definitions, "order");
        var vendors = Named(definitions, "vendors");

        Assert.That(customers.Metadata.GetRequired<RouteBreadcrumbMetadata>(), Is.EqualTo(new RouteBreadcrumbMetadata("All customers", null, false)));
        Assert.That(customers.Metadata.GetAll<RouteGuardMetadata>().Select(g => g.GuardType), Is.EqualTo(new[] { typeof(AllowAllGuard), typeof(RecordingGuard) }), "the generic and the Type form, in attribute order");
        Assert.That(customers.Metadata.GetRequired<RouteLoaderMetadata>(), Is.EqualTo(new RouteLoaderMetadata(typeof(EmptyLoader), RouteReloadPolicy.Never)));
        Assert.That(customer.Metadata.GetRequired<RouteFocusMetadata>(), Is.EqualTo(new RouteFocusMetadata("#customer")));
        Assert.That(customer.Metadata.GetRequired<RouteAnnounceMetadata>(), Is.EqualTo(new RouteAnnounceMetadata(false)));
        Assert.That(order.Metadata.GetRequired<RouteBreadcrumbMetadata>(), Is.EqualTo(new RouteBreadcrumbMetadata(null, null, true)));
        Assert.That(order.Metadata.GetRequired<RouteLoaderMetadata>(), Is.EqualTo(new RouteLoaderMetadata(typeof(EmptyLoader), RouteReloadPolicy.WhenParametersChange)), "the Type form with the default policy");
        Assert.That(vendors.Metadata.GetRequired<RouteAliasMetadata>().Behavior, Is.EqualTo(AliasBehavior.RedirectToCanonical));
        Assert.That(vendors.Aliases, Is.EqualTo(new[] { "/suppliers" }));
        Assert.That(Named(definitions, "home").Metadata.Count, Is.Zero);
    }

    [Test]
    public void RouteParent_nests_the_page_with_its_template_made_relative_to_the_parent()
    {
        var definitions = Discover("App");
        var customers = Named(definitions, "customers");

        Assert.That(customers.Children.Select(c => c.Name), Is.EquivalentTo(new[] { "customer-list", "customer" }));
        var list = customers.Children.Single(c => c.Name == "customer-list");
        var customer = customers.Children.Single(c => c.Name == "customer");
        Assert.That(list.IsIndex, Is.True);
        Assert.That(list.Template, Is.Empty);
        Assert.That(customer.Template, Is.EqualTo("{Id:int}"), "the parent's template is removed from the page's full template");
        Assert.That(customer.Children.Single().Template, Is.EqualTo("orders/{OrderId:guid}"));
        Assert.That(customer.Children.Single().Name, Is.EqualTo("order"));
        Assert.That(Named(definitions, "vendors").Children.Single().Template, Is.EqualTo("{Id:int}"), "a template without a leading '/' is already relative");
    }

    [Test]
    public void A_top_level_template_or_alias_without_a_leading_slash_is_normalized_to_a_rooted_one()
    {
        var definitions = Discover("BareTemplate");
        var tool = Named(definitions, "tool");

        Assert.That(tool.Template, Is.EqualTo("/tool"), "the built-in router accepts [Route(\"tool\")]; discovery roots it instead of failing RT0014");
        Assert.That(tool.Aliases, Is.EqualTo(new[] { "/tools/{Id:int}" }), "an alias is rooted the same way");
        Assert.That(tool.Children.Single().Template, Is.EqualTo("{Section:alpha}"), "a child's relative template is left alone");

        var table = RouteCompiler.Compile(definitions);
        Assert.That(RouteTestKit.Leaf(table, "/tool"), Is.EqualTo("tool"));
        Assert.That(RouteTestKit.Match(table, "/tools/5").MatchedViaAlias, Is.True);
        Assert.That(RouteTestKit.Leaf(table, "/tool/docs"), Is.EqualTo("tool-section"));
        Assert.That(table.GenerateUrl("tool"), Is.EqualTo("/tool"));
        Assert.That(table.GenerateUrl("tool", new Dictionary<string, object?> { ["id"] = 5 }), Is.EqualTo("/tools/5"));
    }

    [Test]
    public void The_fluent_map_still_rejects_a_bare_top_level_template_as_RT0014()
    {
        var exception = Assert.Throws<RouteCompilationException>(() => RouteCompiler.Compile(RouteMap.Create().Route("tool", typeof(Pages.BareTemplate.ToolPage)).Build()))!;

        Assert.That(exception.Errors.Select(e => e.Code), Is.EqualTo(new[] { "RT0014" }));
        Assert.That(exception.Errors[0].Message, Does.Contain("The template 'tool' of a top-level route must start with '/'"));
    }

    [Test]
    public void Abstract_generic_excluded_unrouted_and_non_component_types_are_skipped_and_the_filter_excludes_more()
    {
        var all = Flatten(RouteMap.Create().AddPages(Fixtures, Set("App")).Build()).Select(d => d.Component).ToList();
        var filtered = Flatten(RouteMap.Create().AddPages(Fixtures, type => Set("App")(type) && type != typeof(FilteredPage)).Build()).Select(d => d.Component).ToList();

        Assert.That(all, Does.Contain(typeof(FilteredPage)));
        Assert.That(all, Does.Not.Contain(typeof(AbstractPage)).And.Not.Contain(typeof(GenericPage<>)).And.Not.Contain(typeof(UnroutedComponent)).And.Not.Contain(typeof(RoutedButNotAComponent)));
#if NET9_0_OR_GREATER
        Assert.That(all, Does.Not.Contain(typeof(ExcludedPage)));
#endif
        Assert.That(filtered, Does.Not.Contain(typeof(FilteredPage)));
        Assert.That(filtered, Has.Count.EqualTo(all.Count - 1));
    }

    [Test]
    public void Without_a_filter_every_set_of_the_assembly_is_discovered_and_the_mistakes_show_up()
    {
        var exception = Assert.Throws<RouteCompilationException>(() => RouteMap.Create().AddPages(Fixtures))!;

        Assert.That(exception.Errors.Select(e => e.Code), Does.Contain("RT0019").And.Contain("RT0020").And.Contain("RT0021").And.Contain("RT0022"));
    }

    // ---- through the compiler ---------------------------------------------------------------------------------

    [Test]
    public void Discovered_pages_compile_match_and_generate_like_fluent_routes()
    {
        var table = RouteCompiler.Compile(Discover("App"));

        Assert.That(RouteTestKit.Names(RouteTestKit.Match(table, "/customers/5/orders/4ec9a2a2-1b9f-47b9-8d1a-6e8b2f2b9d77")), Is.EqualTo(new[] { "customers", "customer", "order" }));
        Assert.That(RouteTestKit.Leaf(table, "/customers"), Is.EqualTo("customer-list"));
        Assert.That(RouteTestKit.Leaf(table, "/vendors/7"), Is.EqualTo("vendor"));
        Assert.That(RouteTestKit.Match(table, "/suppliers/7").MatchedViaAlias, Is.True);
        Assert.That(RouteTestKit.Leaf(table, "/org-chart/12"), Is.EqualTo("org-chart"));
        Assert.That(table.GenerateUrl("org-chart"), Is.EqualTo("/org-chart"));
        Assert.That(table.GenerateUrl("org-chart", new Dictionary<string, object?> { ["employeeId"] = 3 }), Is.EqualTo("/org-chart/3"));
        Assert.That(table.GenerateUrl("order", new Dictionary<string, object?> { ["id"] = 1, ["orderId"] = Guid.Empty }), Is.EqualTo("/customers/1/orders/00000000-0000-0000-0000-000000000000"));
        Assert.That(table.Match("/abstract").IsMatch, Is.False);
        Assert.That(table.Match("/excluded").IsMatch, Is.False);
    }

    [Test]
    public void Pages_and_fluent_routes_share_one_table_and_a_page_can_nest_under_a_fluent_route()
    {
        var table = RouteTestKit.Compile(map => map
            .Route("/", typeof(HomeProbe), name: "home")
            .Route("/customers", typeof(CustomersProbe), name: "customers", children: c => c.Route("{id:int}", typeof(DetailsProbe), name: "customer-details"))
            .AddPages(Fixtures, Set("FluentParent")));

        Assert.That(RouteTestKit.Names(RouteTestKit.Match(table, "/customers/3/notes")), Is.EqualTo(new[] { "customers", "notes" }));
        Assert.That(RouteTestKit.Leaf(table, "/customers/3"), Is.EqualTo("customer-details"), "the fluent child is still there");
        Assert.That(table.GenerateUrl("notes", new Dictionary<string, object?> { ["id"] = 3 }), Is.EqualTo("/customers/3/notes"));
    }

    [Test]
    public void A_page_that_conflicts_with_a_fluent_route_is_reported_by_the_compiler()
    {
        var exception = Fails("Conflicts", map => map.Route("/", typeof(HomeProbe), name: "home"));

        Assert.That(exception.Errors.Select(e => e.Code), Is.EquivalentTo(new[] { "RT0010", "RT0012" }));
        Assert.That(exception.Errors.Single(e => e.Code == "RT0010").Message, Does.Contain("page: ModRouter.Test.Pages.Conflicts.DuplicateNamePage"));
    }

    // ---- diagnostics --------------------------------------------------------------------------------------------

    [Test]
    public void RT0019_an_unknown_parent()
    {
        var error = Fails("UnknownParent").Errors.Single();

        Assert.That(error.Code, Is.EqualTo("RT0019"));
        Assert.That(error.Message, Does.Contain("NotInTheSetPage").And.Contain("OrphanPage").And.Contain("AddPages"));
        Assert.That(error.Component, Is.EqualTo(typeof(Pages.UnknownParent.OrphanPage)));
    }

    [TestCase(false, TestName = "RT0024_a_parent_declared_at_two_routes_is_reported_whichever_is_declared_first")]
    [TestCase(true, TestName = "RT0024_a_parent_declared_at_two_routes_is_reported_in_the_reversed_order_too")]
    public void A_route_parent_declared_at_two_routes_is_reported_as_RT0024(bool reversed)
    {
        var exception = Fails("FluentParent", map =>
        {
            var first = reversed ? "/clients" : "/customers";
            var second = reversed ? "/customers" : "/clients";
            map.Route(first, typeof(CustomersProbe)).Route(second, typeof(CustomersProbe));
        });

        var error = exception.Errors.Single();
        Assert.That(error.Code, Is.EqualTo("RT0024"), "one error for the one child, not a misleading RT0022 in the reversed order");
        Assert.That(error.Component, Is.EqualTo(typeof(Pages.FluentParent.NotesPage)));
        Assert.That(error.Message, Does.Contain("CustomersProbe").And.Contain("2 routes").And.Contain("'/customers'").And.Contain("'/clients'"));
        Assert.That(error.Message, Does.Contain("route of its own").And.Contain("declared once"), "says how to disambiguate");
        Assert.That(exception.Message, Does.Contain("RT0024"));
    }

    [Test]
    public void RT0024_names_the_full_templates_of_a_parent_declared_under_another_route()
    {
        var error = Fails("FluentParent", map => map
            .Route("/admin", typeof(HomeProbe), children: c => c.Route("customers", typeof(CustomersProbe)))
            .Route("/customers", typeof(CustomersProbe))).Errors.Single();

        Assert.That(error.Code, Is.EqualTo("RT0024"));
        Assert.That(error.Message, Does.Contain("'/admin/customers'").And.Contain("'/customers'"));
    }

    [Test]
    public void A_route_parent_declared_once_still_nests()
    {
        var table = RouteTestKit.Compile(map => map
            .Route("/customers", typeof(CustomersProbe), name: "customers")
            .AddPages(Fixtures, Set("FluentParent")));

        Assert.That(RouteTestKit.Names(RouteTestKit.Match(table, "/customers/3/notes")), Is.EqualTo(new[] { "customers", "notes" }));
    }

    [Test]
    public void Pages_added_by_several_AddPages_calls_nest_under_a_parent_a_previous_call_added_without_RT0024()
    {
        var map = RouteMap.Create()
            .AddPages(Fixtures, type => type == typeof(Pages.App.CustomersPage))
            .AddPages(Fixtures, type => type == typeof(Pages.App.CustomerListPage) || type == typeof(Pages.App.CustomerPage))
            .AddPages(Fixtures, type => type == typeof(Pages.App.OrderPage));

        var customers = Named(map.Build(), "customers");
        var customer = customers.Children.Single(c => c.Name == "customer");
        Assert.That(customers.Children.Select(c => c.Name), Is.EquivalentTo(new[] { "customer-list", "customer" }));
        Assert.That(customer.Children.Single().Name, Is.EqualTo("order"), "the parent a previous call added (and nested) is still found exactly once");
    }

    [Test]
    public void RT0020_a_parent_cycle()
    {
        var errors = Fails("Cycle").Errors;

        Assert.That(errors.Select(e => e.Code), Is.All.EqualTo("RT0020"));
        Assert.That(errors.Select(e => e.Component), Is.EquivalentTo(new[] { typeof(Pages.Cycle.CycleAPage), typeof(Pages.Cycle.CycleBPage), typeof(Pages.Cycle.HangsOffTheCyclePage) }), "every page whose parent chain never reaches a root");
        Assert.That(errors[0].Message, Does.Contain("CycleAPage").And.Contain("CycleBPage"));
    }

    [Test]
    public void RT0021_an_index_without_a_parent_or_with_a_template_of_its_own()
    {
        var lonely = Fails("IndexWithoutParent").Errors.Single();
        var wrong = Fails("IndexWithTemplate").Errors.Single();

        Assert.That(lonely.Code, Is.EqualTo("RT0021"));
        Assert.That(lonely.Message, Does.Contain("LonelyIndexPage").And.Contain("[RouteParent]"));
        Assert.That(wrong.Code, Is.EqualTo("RT0021"));
        Assert.That(wrong.Message, Does.Contain("WrongIndexPage").And.Contain("'/p/extra'").And.Contain("'/p'"));
    }

    [Test]
    public void RouteKeepAlive_becomes_keep_alive_metadata_and_a_non_positive_Max_is_RT0022()
    {
        Assert.That(Named(Discover("Kept"), "kept").Metadata.Get<RouteKeepAliveMetadata>(), Is.EqualTo(new RouteKeepAliveMetadata(4)));
        Assert.That(typeof(RouteKeepAliveAttribute).GetConstructors().Single().GetParameters().Single().DefaultValue, Is.EqualTo(3));

        var bad = Fails("BadKeep").Errors.Single();

        Assert.That(bad.Code, Is.EqualTo("RT0022"));
        Assert.That(bad.Message, Does.Contain("[RouteKeepAlive]").And.Contain("Max"));
    }

    [Test]
    public void RouteSlot_attributes_become_slot_metadata_and_a_non_component_is_RT0022()
    {
        var slotted = Named(Discover("Slotted"), "slotted");

        Assert.That(slotted.Metadata.GetAll<RouteSlotMetadata>().Select(s => (s.Name, s.Component.Name)), Is.EqualTo(new[] { ("side", "SidePanel"), ("tools", "SidePanel") }));

        var bad = Fails("BadSlot").Errors.Single();

        Assert.That(bad.Code, Is.EqualTo("RT0022"));
        Assert.That(bad.Message, Does.Contain("[RouteSlot]").And.Contain("System.String").And.Contain("IComponent"));
    }

    [Test]
    public void RT0022_a_guard_or_loader_type_that_does_not_implement_its_interface()
    {
        var guard = Fails("BadGuard").Errors.Single();
        var loader = Fails("BadLoader").Errors.Single();

        Assert.That(guard.Code, Is.EqualTo("RT0022"));
        Assert.That(guard.Message, Does.Contain("[RouteGuard]").And.Contain("System.String").And.Contain("IRouteGuard"));
        Assert.That(loader.Code, Is.EqualTo("RT0022"));
        Assert.That(loader.Message, Does.Contain("[RouteLoader]").And.Contain("FixturePage").And.Contain("IRouteLoader"));
    }

    [Test]
    public void RT0022_a_malformed_breadcrumb()
    {
        var errors = Fails("BadBreadcrumb").Errors;

        Assert.That(errors.Select(e => e.Code), Is.All.EqualTo("RT0022"));
        Assert.That(errors.Select(e => e.Component), Is.EquivalentTo(new[] { typeof(Pages.BadBreadcrumb.NoLabelPage), typeof(Pages.BadBreadcrumb.LabelAndHiddenPage) }));
        Assert.That(errors.Select(e => e.Message), Is.All.Contains("[RouteBreadcrumb]"));
    }

    [Test]
    public void RT0022_a_nested_page_whose_template_does_not_extend_its_parents()
    {
        var error = Fails("BadNesting").Errors.Single();

        Assert.That(error.Code, Is.EqualTo("RT0022"));
        Assert.That(error.Message, Does.Contain("[RouteParent]").And.Contain("'/q/{id:int}'").And.Contain("'/p'"));
    }

    [Test]
    public void Discovery_errors_are_RouteCompilationErrors_with_the_page_as_source()
    {
        var error = Fails("UnknownParent").Errors.Single();

        Assert.That(error.Source, Does.StartWith("page: ").And.Contain("OrphanPage"));
        Assert.That(error.Template, Is.EqualTo("/orphan"));
    }

    // ---- registration -------------------------------------------------------------------------------------------

    [Test]
    public void Options_Pages_adds_the_assemblies_at_startup_after_the_fluent_routes()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddModRouter(map => map.Route("/about", typeof(ShowProbe), name: "about"), options => options.Pages.Add(Fixtures, Set("App")));

        using var provider = services.BuildServiceProvider();
        var holder = provider.GetRequiredService<RouteTableHolder>();
        Assert.That(holder.StartupDefinitions.Select(d => d.Component.Name), Is.EqualTo(new[] { "ShowProbe", "HomePage", "OrgChartPage", "CustomersPage", "VendorsPage", "FilteredPage" }));
        Assert.That(holder.Table.TryGetRoute("customer", out _), Is.True);
    }

    [Test]
    public void A_startup_error_in_a_discovered_page_still_throws_from_AddModRouter()
    {
        var services = new ServiceCollection();

        var exception = Assert.Throws<RouteCompilationException>(() => services.AddModRouter(configure: options => options.Pages.Add(Fixtures, Set("Cycle"))))!;

        Assert.That(exception.Errors.Select(e => e.Code), Is.All.EqualTo("RT0020"));
    }

    [Test]
    public void AddPages_requires_unreferenced_code_and_the_attributes_are_class_only_and_not_inherited()
    {
        var addPages = typeof(RouteMapPagesExtensions).GetMethod(nameof(RouteMapPagesExtensions.AddPages))!;
        Assert.That(addPages.GetCustomAttribute<RequiresUnreferencedCodeAttribute>()?.Message, Does.Contain("TrimmerRootAssembly"));
        var pagesAdd = typeof(PageAssemblyCollection).GetMethod(nameof(PageAssemblyCollection.Add))!;
        Assert.That(pagesAdd.GetCustomAttribute<RequiresUnreferencedCodeAttribute>()?.Message, Is.EqualTo(addPages.GetCustomAttribute<RequiresUnreferencedCodeAttribute>()!.Message), "options.Pages.Add discovers by reflection exactly like AddPages, so a trimmable caller must get the same warning");

        var attributes = typeof(RouteNameAttribute).Assembly.GetExportedTypes()
            .Where(t => t.Namespace == typeof(RouteNameAttribute).Namespace && typeof(Attribute).IsAssignableFrom(t) && !t.IsGenericTypeDefinition)
            .ToList();
        Assert.That(attributes.Select(t => t.Name), Is.EquivalentTo(new[]
        {
            "RouteNameAttribute", "RouteTitleAttribute", "RouteBreadcrumbAttribute", "RouteGuardAttribute", "RouteLoaderAttribute",
            "RouteFocusAttribute", "RouteNoAnnounceAttribute", "RouteIndexAttribute", "RouteCanonicalAliasAttribute", "RouteParentAttribute", "RoutePersistLoaderDataAttribute", "RouteLeaveGuardAttribute", "RouteLoaderCacheAttribute", "RouteSlotAttribute", "RouteKeepAliveAttribute",
        }));

        foreach (var attribute in attributes)
        {
            var usage = attribute.GetCustomAttribute<AttributeUsageAttribute>(inherit: true)!;
            Assert.That(usage.ValidOn, Is.EqualTo(AttributeTargets.Class), attribute.Name);
            Assert.That(usage.Inherited, Is.False, attribute.Name);
            Assert.That(usage.AllowMultiple, Is.EqualTo(attribute == typeof(RouteGuardAttribute) || attribute == typeof(RouteLeaveGuardAttribute) || attribute == typeof(RouteSlotAttribute)), attribute.Name);
        }

        Assert.That(typeof(RouteGuardAttribute<>).BaseType, Is.EqualTo(typeof(RouteGuardAttribute)));
        Assert.That(typeof(RouteLoaderAttribute<>).BaseType, Is.EqualTo(typeof(RouteLoaderAttribute)));
        Assert.That(typeof(RouteLeaveGuardAttribute<>).BaseType, Is.EqualTo(typeof(RouteLeaveGuardAttribute)));
        Assert.That(typeof(RoutePersistLoaderDataAttribute<>).BaseType, Is.EqualTo(typeof(RoutePersistLoaderDataAttribute)));
    }

    // ---- rendering ----------------------------------------------------------------------------------------------

    [Test]
    public async Task Discovered_pages_render_as_a_chain_with_their_guards_and_loader()
    {
        await using var ctx = new BunitContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddModRouter(map => map.AddPages(Fixtures, Set("App")));
        var adapter = new FakeNavigationAdapter("http://localhost/", "http://localhost/customers/5/orders/4ec9a2a2-1b9f-47b9-8d1a-6e8b2f2b9d77") { ExecuteNavigateTo = true };
        ctx.Services.AddSingleton<INavigationPlatformAdapter>(adapter);

        var cut = ctx.Render<Components.ModRouter>(p => p.Add(r => r.DefaultLayout, typeof(MainLayoutProbe)));
        await EventuallyAsync(() => All(cut, "#layout #customers #customer #order").Count, Is.EqualTo(1));

        var store = ctx.Services.GetRequiredService<RouteStateStore>();
        Assert.That(One(cut, "#order").TextContent, Does.StartWith("order 4ec9a2a2-1b9f-47b9-8d1a-6e8b2f2b9d77 of 5"));
        Assert.That(RecordingGuard.Calls, Is.EqualTo(new[] { "customers" }));
        Assert.That(store.Current!.LevelData[0]!.Data, Is.EqualTo("loaded"));
        Assert.That(store.Current.Title, Is.EqualTo("Order"), "the leaf's [RouteTitle] through the default title format");

        await adapter.NavigateAsync("org-chart/9", intercepted: true);
        await adapter.SettledAsync();
        Assert.That(One(cut, "#org-chart").TextContent, Is.EqualTo("employee 9"));
        Assert.That(ctx.Renderer.UnhandledException.IsCompleted, Is.False, () => "a render exception was swallowed: " + ctx.Renderer.UnhandledException.Result);
    }
}
