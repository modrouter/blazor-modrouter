using ModRouter.Core.Titles;

namespace ModRouter.Test;

[TestFixture]
public sealed class RouteTitlesTests
{
    static string? Effective(Action<Core.Definitions.RouteMap> configure, string path)
        => RouteTitles.Effective(RouteTestKit.Match(RouteTestKit.Compile(configure), path));

    [Test]
    public void A_titled_leaf_uses_its_own_title()
    {
        Assert.That(Effective(map => map.Route("/a", typeof(HomePage), title: "A"), "/a"), Is.EqualTo("A"));
    }

    [Test]
    public void A_title_factory_sees_the_inherited_parameters()
    {
        Assert.That(Effective(map => map.Route("/c/{id:int}", typeof(HomePage), p => $"Customer {p["id"]}"), "/c/5"), Is.EqualTo("Customer 5"));
    }

    [Test]
    public void An_untitled_index_route_takes_its_parents_title()
    {
        var title = Effective(map => map.Route("/customers", typeof(CustomersPage), title: "Customers", children: c => c.Index(typeof(CustomerList))), "/customers");

        Assert.That(title, Is.EqualTo("Customers"));
    }

    [Test]
    public void A_titled_index_route_keeps_its_own_title()
    {
        var title = Effective(map => map.Route("/customers", typeof(CustomersPage), title: "Customers", children: c => c.Index(typeof(CustomerList), title: "All")), "/customers");

        Assert.That(title, Is.EqualTo("All"));
    }

    [Test]
    public void An_untitled_child_does_not_borrow_its_parents_title()
    {
        var title = Effective(map => map.Route("/customers", typeof(CustomersPage), title: "Customers", children: c => c.Route("x", typeof(OtherPage))), "/customers/x");

        Assert.That(title, Is.Null);
    }

    [Test]
    public void An_untitled_index_under_an_untitled_parent_has_no_title()
    {
        var title = Effective(map => map.Route("/customers", typeof(CustomersPage), children: c => c.Index(typeof(CustomerList))), "/customers");

        Assert.That(title, Is.Null);
    }

    [Test]
    public void A_null_chain_is_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => RouteTitles.Effective(null!));
    }
}
