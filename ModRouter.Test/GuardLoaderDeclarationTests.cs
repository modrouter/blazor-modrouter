using ModRouter.Core.Compilation;
using ModRouter.Core.Definitions;
using ModRouter.Core.Guards;
using ModRouter.Core.Loading;

namespace ModRouter.Test;

[TestFixture]
public sealed class GuardLoaderDeclarationTests
{
    sealed class AllowGuard : IRouteGuard
    {
        public ValueTask<RouteGuardResult> CanActivateAsync(RouteGuardContext context) => ValueTask.FromResult(RouteGuardResult.Allow());
    }

    sealed class EmptyLoader : IRouteLoader
    {
        public ValueTask<RouteLoadResult> LoadAsync(RouteLoadContext context) => ValueTask.FromResult(RouteLoadResult.Create());
    }

    static RouteCompilationException Fails(Action<RouteMap> configure)
        => Assert.Throws<RouteCompilationException>(() => RouteTestKit.Compile(configure))!;

    [Test]
    public void Guard_and_loader_attach_to_the_route_declared_last()
    {
        var definitions = RouteMap.Create()
            .Route("/a", typeof(HomePage), name: "a")
            .Route("/b", typeof(OtherPage), name: "b").Guard<AllowGuard>().Loader<EmptyLoader>(RouteReloadPolicy.Never)
            .Build();

        Assert.That(definitions[0].Metadata.Count, Is.Zero);
        Assert.That(definitions[1].Metadata.GetAll<RouteGuardMetadata>().Single().GuardType, Is.EqualTo(typeof(AllowGuard)));
        Assert.That(definitions[1].Metadata.Get<RouteLoaderMetadata>(), Is.EqualTo(new RouteLoaderMetadata(typeof(EmptyLoader), RouteReloadPolicy.Never)));
    }

    [Test]
    public void Guards_keep_declaration_order()
    {
        var definitions = RouteMap.Create()
            .Route("/a", typeof(HomePage)).Guard<AllowGuard>().Guard<AllowGuard>()
            .Build();

        Assert.That(definitions[0].Metadata.GetAll<RouteGuardMetadata>().Count(), Is.EqualTo(2));
    }

    [Test]
    public void Attaching_before_any_route_is_a_clear_error()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => RouteMap.Create().Guard<AllowGuard>())!;

        Assert.That(exception.Message, Does.Contain("Guard").And.Contain("no route has been declared"));
    }

    [Test]
    public void Child_declarations_attach_inside_the_children_callback()
    {
        var definitions = RouteMap.Create()
            .Route("/p", typeof(HomePage), children: c => c.Route("x", typeof(OtherPage)).Guard<AllowGuard>())
            .Build();

        Assert.That(definitions[0].Metadata.Count, Is.Zero);
        Assert.That(definitions[0].Children[0].Metadata.Contains<RouteGuardMetadata>(), Is.True);
    }

    [Test]
    public void Redirect_declares_a_definition_that_forwards_to_a_named_route()
    {
        var table = RouteTestKit.Compile(map => map
            .Route("/customers/{id:int}", typeof(CustomerDetails), name: "customer")
            .Redirect("/c/{id:int}", "customer"));

        var redirect = table.Match("/c/7").Chain!.Leaf.Route.Metadata.GetRequired<RouteRedirectMetadata>();

        Assert.That(redirect, Is.EqualTo(new RouteRedirectMetadata("customer", true, true, false)));
    }

    [Test]
    public void Canonical_alias_requires_the_alias_to_declare_the_same_parameters()
    {
        var exception = Fails(map => map
            .Route("/customers/{id:int}", typeof(CustomerDetails), aliases: ["/c/{key:int}"]).CanonicalAlias());

        Assert.That(exception.Errors.Select(e => e.Code), Does.Contain("RT0015"));
        Assert.That(exception.Message, Does.Contain("/c/{key:int}").And.Contain("id"));
    }

    [Test]
    public void Canonical_alias_with_matching_parameters_compiles_and_flags_the_chain()
    {
        var table = RouteTestKit.Compile(map => map
            .Route("/customers/{id:int}", typeof(CustomerDetails), aliases: ["/c/{id:int}"]).CanonicalAlias());

        var chain = table.Match("/c/4").Chain!;

        Assert.That(chain.MatchedViaAlias, Is.True);
        Assert.That(chain.Leaf.Route.Metadata.Get<RouteAliasMetadata>(), Is.EqualTo(new RouteAliasMetadata(AliasBehavior.RedirectToCanonical)));
    }

    [Test]
    public void Only_one_loader_per_level()
    {
        var exception = Fails(map => map
            .Route("/a", typeof(HomePage)).Loader<EmptyLoader>().Loader<EmptyLoader>());

        Assert.That(exception.Errors.Select(e => e.Code), Is.EqualTo(new[] { "RT0016" }));
    }

    [Test]
    public void Redirect_to_an_unknown_route_is_reported()
    {
        var exception = Fails(map => map.Redirect("/old", "missing"));

        Assert.That(exception.Errors.Select(e => e.Code), Is.EqualTo(new[] { "RT0017" }));
        Assert.That(exception.Message, Does.Contain("missing"));
    }

    [Test]
    public void Redirect_must_provide_the_target_parameters()
    {
        var exception = Fails(map => map
            .Route("/customers/{id:int}", typeof(CustomerDetails), name: "customer")
            .Redirect("/c", "customer"));

        Assert.That(exception.Errors.Select(e => e.Code), Is.EqualTo(new[] { "RT0017" }));
        Assert.That(exception.Message, Does.Contain("'id'"));
    }

    [Test]
    public void Redirect_to_a_redirect_is_reported()
    {
        var exception = Fails(map => map
            .Route("/a", typeof(HomePage), name: "a")
            .Redirect("/b", "a")
            .Redirect("/c", "b"));

        Assert.That(exception.Errors.Select(e => e.Code), Does.Contain("RT0017"));
    }

    [Test]
    public void Redirect_definitions_are_not_named_so_a_redirect_target_name_must_come_from_a_real_route()
    {
        var table = RouteTestKit.Compile(map => map
            .Route("/a", typeof(HomePage), name: "a")
            .Redirect("/old", "a"));

        Assert.That(table.TryGetRoute("a", out var route), Is.True);
        Assert.That(route!.Component, Is.EqualTo(typeof(HomePage)));
    }
}
