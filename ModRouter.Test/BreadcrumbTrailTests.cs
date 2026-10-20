using ModRouter.Core.Breadcrumbs;
using ModRouter.Core.Compilation;

namespace ModRouter.Test;

[TestFixture]
public sealed class BreadcrumbTrailTests
{
    static CompiledRouteTable Table() => RouteTestKit.Compile(map => map
        .Route("/", typeof(HomePage), name: "home", title: "Home")
        .Route("/customers", typeof(CustomersPage), name: "customers", title: "Customers", children: customers => customers
            .Index(typeof(CustomerList), name: "customer-list", title: "All customers")
            .Route("{id:int}", typeof(CustomerDetails), p => $"Customer {p["id"]}", name: "customer", children: customer => customer
                .Route("orders/{orderId:guid}", typeof(OrderDetails), p => $"Order {p["orderId"]}", name: "order")))
        .Route("/files/{*path}", typeof(FilesPage), name: "files"));

    static IReadOnlyList<(string Label, string Path)> Trail(CompiledRouteTable table, string path)
        => [.. BreadcrumbTrail.From(RouteTestKit.Match(table, path)).Select(e => (e.Label, e.Path))];

    [Test]
    public void Each_titled_level_of_the_chain_is_a_crumb_with_its_matched_path()
    {
        var trail = Trail(Table(), "/customers/42/orders/4ec9a2a2-1b9f-47b9-8d1a-6e8b2f2b9d77");

        Assert.That(trail, Is.EqualTo(new[]
        {
            ("Customers", "/customers"),
            ("Customer 42", "/customers/42"),
            ("Order 4ec9a2a2-1b9f-47b9-8d1a-6e8b2f2b9d77", "/customers/42/orders/4ec9a2a2-1b9f-47b9-8d1a-6e8b2f2b9d77"),
        }));
    }

    [Test]
    public void An_index_route_adds_no_crumb_of_its_own()
    {
        var trail = Trail(Table(), "/customers");

        Assert.That(trail, Is.EqualTo(new[] { ("Customers", "/customers") }));
    }

    [Test]
    public void A_level_without_a_title_is_skipped()
    {
        var trail = Trail(Table(), "/files/a/b");

        Assert.That(trail, Is.Empty);
    }

    [Test]
    public void The_root_route_is_a_crumb_with_the_root_path()
    {
        Assert.That(Trail(Table(), "/"), Is.EqualTo(new[] { ("Home", "/") }));
    }

    [Test]
    public void Metadata_overrides_the_label_with_a_fixed_text_or_a_factory_and_can_hide_a_level()
    {
        var table = RouteTestKit.Compile(map => map
            .Route("/customers", typeof(CustomersPage), title: "Customers", children: customers => customers
                .Route("{id:int}", typeof(CustomerDetails), p => $"Customer {p["id"]}", children: customer => customer
                    .Route("orders", typeof(OrderDetails), title: "Orders").Breadcrumb(p => $"Orders of {p["id"]}"))
                .Breadcrumb("Everyone"))
            .Breadcrumb(hidden: true));

        var trail = Trail(table, "/customers/7/orders");

        Assert.That(trail, Is.EqualTo(new[]
        {
            ("Everyone", "/customers/7"),
            ("Orders of 7", "/customers/7/orders"),
        }));
    }

    [Test]
    public void A_blank_label_from_a_factory_drops_the_level()
    {
        var table = RouteTestKit.Compile(map => map
            .Route("/a", typeof(HomePage), title: "A", children: a => a
                .Route("b", typeof(OtherPage), title: "B").Breadcrumb(_ => " ")));

        Assert.That(Trail(table, "/a/b"), Is.EqualTo(new[] { ("A", "/a") }));
    }

    [Test]
    public void An_alias_match_uses_the_matched_path_not_the_canonical_one()
    {
        var table = RouteTestKit.Compile(map => map
            .Route("/customers", typeof(CustomersPage), title: "Customers", aliases: ["/clients"]));

        Assert.That(Trail(table, "/clients"), Is.EqualTo(new[] { ("Customers", "/clients") }));
    }

    [Test]
    public void A_null_chain_is_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => BreadcrumbTrail.From(null!));
    }
}
