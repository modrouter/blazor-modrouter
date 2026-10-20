using ModRouter.Core.Compilation;
using ModRouter.Core.Constraints;
using ModRouter.Core.Definitions;
using ModRouter.Core.Matching;

namespace ModRouter.Test;

internal static class RouteTestKit
{
    public static CompiledRouteTable Compile(Action<RouteMap> configure, IConstraintRegistry? registry = null)
    {
        var map = RouteMap.Create();
        configure(map);

        return RouteCompiler.Compile(map.Build(), registry);
    }

    /// <summary>A holder that is already frozen with <paramref name="table"/>, for tests that wire the pipeline by hand.</summary>
    public static RouteTableHolder Frozen(CompiledRouteTable table)
    {
        var holder = new RouteTableHolder(table);
        holder.Freeze(table);

        return holder;
    }

    /// <summary>Compiles flat, unnested routes: each tuple is (template, name).</summary>
    public static CompiledRouteTable Flat(params (string Template, string Name)[] routes)
        => Compile(map =>
        {
            foreach (var (template, name) in routes)
            {
                map.Route(template, typeof(OtherPage), name: name);
            }
        });

    /// <summary>The hierarchy from the brief: home, customers (index list, details, order details).</summary>
    public static CompiledRouteTable CustomersTable() => Compile(map => map
        .Route("/", typeof(HomePage), name: "home", title: "Home")
        .Route("/customers", typeof(CustomersPage), name: "customers", title: "Customers", children: customers => customers
            .Index(typeof(CustomerList), name: "customer-list")
            .Route("{id:int}", typeof(CustomerDetails), p => $"Customer {p["id"]}", name: "customer-details", children: customer => customer
                .Route("orders/{orderId:guid}", typeof(OrderDetails), p => $"Order {p["orderId"]}", name: "customer-order-details"))));

    /// <summary>Matches and asserts success, so a failing precondition reads clearly.</summary>
    public static RouteMatchChain Match(CompiledRouteTable table, string path)
    {
        var result = table.Match(path);
        Assert.That(result.IsMatch, Is.True, $"Expected '{path}' to match a route (failure: {result.Failure}).");

        return result.Chain!;
    }

    public static string? Leaf(CompiledRouteTable table, string path) => Match(table, path).Leaf.RouteName;

    public static string[] Names(RouteMatchChain chain) => [.. chain.Matches.Select(m => m.RouteName ?? "(unnamed)")];
}
