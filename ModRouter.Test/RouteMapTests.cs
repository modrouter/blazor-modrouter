using ModRouter.Core.Definitions;
using ModRouter.Core.Matching;

namespace ModRouter.Test;

[TestFixture]
public sealed class RouteMapTests
{
    static RouteParameters Parameters(string name, object value)
        => new(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { [name] = value });

    [Test]
    public void Fluent_map_builds_nested_definitions()
    {
        var definitions = RouteMap.Create()
            .Route("/", typeof(HomePage), name: "home", title: "Home")
            .Route("/customers", typeof(CustomersPage), name: "customers", children: customers => customers
                .Index(typeof(CustomerList), name: "customer-list")
                .Route("{id:int}", typeof(CustomerDetails), p => $"Customer {p["id"]}", name: "customer-details"))
            .Build();

        Assert.That(definitions, Has.Count.EqualTo(2));
        Assert.That(definitions[0].Name, Is.EqualTo("home"));
        Assert.That(definitions[0].StaticTitle, Is.EqualTo("Home"));

        var customers = definitions[1];
        Assert.That(customers.Template, Is.EqualTo("/customers"));
        Assert.That(customers.Component, Is.EqualTo(typeof(CustomersPage)));
        Assert.That(customers.Children, Has.Count.EqualTo(2));
        Assert.That(customers.Children[0].IsIndex, Is.True);
        Assert.That(customers.Children[0].Template, Is.Empty);
        Assert.That(customers.Children[0].Name, Is.EqualTo("customer-list"));
        Assert.That(customers.Children[1].IsIndex, Is.False);
        Assert.That(customers.Children[1].Template, Is.EqualTo("{id:int}"));
        Assert.That(customers.Children[1].Title!(Parameters("id", 42)), Is.EqualTo("Customer 42"));
    }

    [Test]
    public void Named_arguments_in_any_order_work_like_the_brief()
    {
        var definitions = RouteMap.Create()
            .Route(name: "home", template: "/", title: "Home", component: typeof(HomePage))
            .Route(name: "customer", template: "/customers/{id:int}", component: typeof(CustomerDetails), title: p => $"Customer {p["id"]}")
            .Build();

        Assert.That(definitions[0].StaticTitle, Is.EqualTo("Home"));
        Assert.That(definitions[1].Title, Is.Not.Null);
    }

    [Test]
    public void Aliases_metadata_and_reuse_policy_flow_into_the_definition()
    {
        var metadata = RouteMetadataCollection.Create("menu");
        var definition = RouteMap.Create()
            .Route("/customers/{id:int}", typeof(CustomerDetails), name: "customer-details",
                aliases: ["/clients/{id:int}", "/customer/{id:int}"],
                metadata: metadata,
                reuse: RouteComponentReuse.RecreateWhenParametersChange)
            .Build()
            .Single();

        Assert.That(definition.Aliases, Is.EqualTo(new[] { "/clients/{id:int}", "/customer/{id:int}" }));
        Assert.That(definition.Metadata, Is.SameAs(metadata));
        Assert.That(definition.ReusePolicy, Is.EqualTo(RouteComponentReuse.RecreateWhenParametersChange));
    }

    [Test]
    public void Defaults_are_empty_and_reuse_by_component_type()
    {
        var definition = RouteMap.Create().Route("/a", typeof(HomePage)).Build().Single();

        Assert.That(definition.Name, Is.Null);
        Assert.That(definition.Aliases, Is.Empty);
        Assert.That(definition.Children, Is.Empty);
        Assert.That(definition.Metadata, Is.SameAs(RouteMetadataCollection.Empty));
        Assert.That(definition.ReusePolicy, Is.EqualTo(RouteComponentReuse.ReuseWhenComponentTypeMatches));
    }

    [Test]
    public void Definitions_record_where_they_were_declared()
    {
        var definition = RouteMap.Create().Route("/a", typeof(HomePage)).Build().Single();

        Assert.That(definition.Source, Does.StartWith("fluent: RouteMapTests.cs:"));
    }

    [Test]
    public void Build_returns_a_snapshot()
    {
        var map = RouteMap.Create().Route("/a", typeof(HomePage));
        var first = map.Build();
        map.Route("/b", typeof(AboutPage));

        Assert.That(first, Has.Count.EqualTo(1));
        Assert.That(map.Build(), Has.Count.EqualTo(2));
    }
}

[TestFixture]
public sealed class RouteMetadataCollectionTests
{
    sealed record MenuInfo(string Label);

    sealed record AnalyticsName(string Name);

    [Test]
    public void Get_returns_the_first_item_of_the_requested_type()
    {
        var metadata = RouteMetadataCollection.Create(new MenuInfo("A"), new AnalyticsName("x"), new MenuInfo("B"));

        Assert.That(metadata.Get<MenuInfo>(), Is.EqualTo(new MenuInfo("A")));
        Assert.That(metadata.GetAll<MenuInfo>().Select(m => m.Label), Is.EqualTo(new[] { "A", "B" }));
        Assert.That(metadata.Count, Is.EqualTo(3));
    }

    [Test]
    public void Get_returns_default_and_GetRequired_throws_when_absent()
    {
        var metadata = RouteMetadataCollection.Create(new MenuInfo("A"));

        Assert.That(metadata.Get<AnalyticsName>(), Is.Null);
        Assert.That(metadata.Contains<AnalyticsName>(), Is.False);
        Assert.That(metadata.Contains<MenuInfo>(), Is.True);
        var exception = Assert.Throws<InvalidOperationException>(() => metadata.GetRequired<AnalyticsName>());
        Assert.That(exception!.Message, Does.Contain("AnalyticsName"));
    }

    [Test]
    public void With_returns_a_new_collection_and_leaves_the_original_unchanged()
    {
        var original = RouteMetadataCollection.Create(new MenuInfo("A"));
        var extended = original.With(new AnalyticsName("x"));

        Assert.That(original.Count, Is.EqualTo(1));
        Assert.That(extended.Count, Is.EqualTo(2));
        Assert.That(extended.GetRequired<AnalyticsName>().Name, Is.EqualTo("x"));
    }

    [Test]
    public void Create_with_no_items_is_the_shared_empty_instance()
    {
        Assert.That(RouteMetadataCollection.Create(), Is.SameAs(RouteMetadataCollection.Empty));
        Assert.That(RouteMetadataCollection.Empty.Count, Is.EqualTo(0));
    }
}
