using ModRouter.Core.Compilation;
using ModRouter.Core.Generation;
using ModRouter.Core.Matching;
using ModRouter.Navigation;

namespace ModRouter.Test;

/// <summary>
/// The structured side of <c>Explain</c> the debug panel's explorer needs (the matched route, an alias flag, a verdict kind
/// and the precedence reason of a matched loser), and <see cref="INavigationService.TryGetRouteUrl"/>. The English verdicts
/// and the selection are unchanged (ExplainTests, DifferentialTests).
/// </summary>
[TestFixture]
public sealed class ExplainAdditionsTests
{
    static CompiledRouteTable Table() => RouteTestKit.Compile(map => map
        .Route("/customers", typeof(CustomersPage), name: "customers", children: c => c
            .Index(typeof(CustomerList), name: "customer-list")
            .Route("{id:int}", typeof(CustomerDetails), name: "customer-details", children: d => d
                .Route("orders/{orderId:guid}", typeof(OrderDetails), name: "order"))
            .Route("new", typeof(OtherPage), name: "customer-new")
            .Route("{slug}", typeof(OtherPage), name: "customer-slug"))
        .Route("/vendors", typeof(OtherPage), name: "vendors", aliases: ["/suppliers"])
        .Route("/files/{*path}", typeof(OtherPage), name: "files"));

    static RouteCandidate Candidate(RouteExplanation explanation, string name, bool alias = false)
        => explanation.Candidates.Single(c => c.RouteName == name && c.IsAlias == alias);

    [Test]
    public void The_selected_candidate_is_Matched_and_carries_its_compiled_route()
    {
        var table = Table();
        var explanation = table.Explain("/customers/42");
        var details = Candidate(explanation, "customer-details");

        Assert.That(details.Verdict, Is.EqualTo(RouteCandidateVerdict.Matched));
        Assert.That(details.Route, Is.SameAs(table.Routes.Single(r => r.Definition.Name == "customer-details" && !r.IsAlias)));
        Assert.That(details.Route!.Template, Is.EqualTo(details.Template));
        Assert.That(details.PrecedenceReason, Is.Null);
        Assert.That(details.IsAlias, Is.False);
    }

    [Test]
    public void A_matching_candidate_that_was_not_selected_lost_on_precedence_and_says_why()
    {
        var slug = Candidate(Table().Explain("/customers/42"), "customer-slug");

        Assert.That(slug.IsMatch, Is.True, "the English side already said it matched");
        Assert.That(slug.Verdict, Is.EqualTo(RouteCandidateVerdict.LostOnPrecedence));
        Assert.That(slug.PrecedenceReason, Is.EqualTo("At segment 2 (\"42\") the selected route /customers/{id:int} has a constrained parameter, which ranks before a parameter without a constraint."));
    }

    [Test]
    public void A_static_segment_outranks_a_parameter_in_the_precedence_reason()
    {
        var slug = Candidate(Table().Explain("/customers/new"), "customer-slug");

        Assert.That(slug.Verdict, Is.EqualTo(RouteCandidateVerdict.LostOnPrecedence));
        Assert.That(slug.PrecedenceReason, Does.StartWith("At segment 2 (\"new\") the selected route /customers/new has a static segment"));
    }

    // Every precedence tier the reason can name (the loser is declared first, so declaration order never decides).
    [TestCase("/a", "/a/{id?}", "/a", "At the end of the path the selected route /a has no optional parameter, which ranks before a template with one.")]
    [TestCase("/a/{id}", "/a/{id?}", "/a/5", "At the end of the path the selected route /a/{id} has no optional parameter, which ranks before a template with one.")]
    [TestCase("/f", "/f/{*rest}", "/f", "At the end of the path the selected route /f has a template that ends here, which ranks before a catch-all parameter.")]
    [TestCase("/g/{x}", "/g/{*rest}", "/g/5", "At segment 2 (\"5\") the selected route /g/{x} has a parameter without a constraint, which ranks before a catch-all parameter.")]
    [TestCase("/m/{id:int:min(1)}", "/m/{id:int}", "/m/5", "At segment 2 (\"5\") the selected route /m/{id:int:min(1)} has a constrained parameter with 2 constraints, which ranks before one with 1.")]
    [TestCase("/k/{id:int}", "/k/{id:long}", "/k/5", "At segment 2 (\"5\") the selected route /k/{id:int} has the constraint 'int', which sorts before 'long'.")]
    public void The_precedence_reason_names_the_tier_that_decided(string winner, string loser, string path, string reason)
    {
        var table = RouteTestKit.Compile(map => map
            .Route(loser, typeof(OtherPage), name: "loser")
            .Route(winner, typeof(CustomersPage), name: "winner"));
        var explanation = table.Explain(path);

        Assert.That(explanation.SelectedRouteName, Is.EqualTo("winner"));
        Assert.That(Candidate(explanation, "loser").PrecedenceReason, Is.EqualTo(reason));
    }

    [Test]
    public void A_failed_constraint_and_a_failed_static_segment_have_their_own_kinds()
    {
        var explanation = Table().Explain("/customers/abc");

        Assert.Multiple(() =>
        {
            Assert.That(Candidate(explanation, "customer-details").Verdict, Is.EqualTo(RouteCandidateVerdict.ConstraintFailed));
            Assert.That(Candidate(explanation, "customer-new").Verdict, Is.EqualTo(RouteCandidateVerdict.SegmentMismatch));
            Assert.That(Candidate(explanation, "customer-list").Verdict, Is.EqualTo(RouteCandidateVerdict.SegmentMismatch), "an extra path segment is a segment mismatch");
            Assert.That(Candidate(explanation, "order").Verdict, Is.EqualTo(RouteCandidateVerdict.ConstraintFailed), "its first failure is the int constraint of the parent segment");
            Assert.That(Candidate(explanation, "customer-slug").Verdict, Is.EqualTo(RouteCandidateVerdict.Matched));
            Assert.That(explanation.Candidates.Where(c => c.Verdict is not (RouteCandidateVerdict.Matched or RouteCandidateVerdict.LostOnPrecedence)).Select(c => c.IsMatch), Is.All.False);
        });
    }

    [Test]
    public void A_missing_segment_at_the_end_of_the_path_is_a_segment_mismatch()
    {
        var order = Candidate(Table().Explain("/customers/42"), "order");

        Assert.That(order.Verdict, Is.EqualTo(RouteCandidateVerdict.SegmentMismatch));
        Assert.That(order.IsMatch, Is.False);
    }

    [Test]
    public void An_alias_entry_is_flagged_and_points_at_its_alias_route()
    {
        var explanation = Table().Explain("/suppliers");
        var alias = Candidate(explanation, "vendors", alias: true);

        Assert.That(alias.Verdict, Is.EqualTo(RouteCandidateVerdict.Matched));
        Assert.That(alias.Route!.IsAlias, Is.True);
        Assert.That(alias.Route.CanonicalTemplate, Is.EqualTo("/vendors"));
        Assert.That(explanation.Candidates.Count(c => c.RouteName == "vendors"), Is.EqualTo(1), "the canonical /vendors entry does not start with 'suppliers'");
    }

    [Test]
    public void The_verdicts_agree_with_IsMatch_and_the_selection_on_every_candidate_of_random_tables()
    {
        var losers = 0;

        for (var seed = 1; seed <= 60; seed++)
        {
            var random = new Random(seed);
            RandomRouteTables.Build(random, out var table);

            if (table is null)
            {
                continue;
            }

            for (var i = 0; i < 100; i++)
            {
                var path = RandomRouteTables.RandomPath(random);
                var explanation = table.Explain(path);
                losers += explanation.Candidates.Count(c => c.Verdict == RouteCandidateVerdict.LostOnPrecedence);
                var matched = explanation.Candidates.Where(c => c.Verdict == RouteCandidateVerdict.Matched).ToList();

                Assert.That(explanation.Candidates.All(c => c.IsMatch == c.Verdict is RouteCandidateVerdict.Matched or RouteCandidateVerdict.LostOnPrecedence), Is.True, $"seed {seed}, {path}");
                Assert.That(explanation.Candidates.All(c => (c.PrecedenceReason is not null) == (c.Verdict == RouteCandidateVerdict.LostOnPrecedence)), Is.True, $"seed {seed}, {path}");
                Assert.That(explanation.Candidates.All(c => c.Route is not null && c.Route.Template == c.Template && c.IsAlias == c.Route.IsAlias), Is.True, $"seed {seed}, {path}");
                Assert.That(matched, Has.Count.LessThanOrEqualTo(1), $"seed {seed}, {path}");

                Assert.That(matched.SingleOrDefault()?.Template, Is.EqualTo(explanation.SelectedTemplate), $"seed {seed}, {path}");
            }
        }

        Assert.That(losers, Is.GreaterThan(0), "the random tables produce matched losers, so the precedence side is exercised");
    }

    // ---- TryGetRouteUrl ------------------------------------------------------------------------------------------

    static NavigationService Service(string baseUri)
        => new(new FakeNavigationAdapter(baseUri), RouteTestKit.Frozen(RouteTestKit.CustomersTable()), new RouteStateStore());

    [TestCase("http://localhost/", "/customers/42")]
    [TestCase("http://localhost/myapp/", "/myapp/customers/42")]
    public void TryGetRouteUrl_returns_the_base_prefixed_url_like_GetRouteUrl(string baseUri, string expected)
    {
        var service = Service(baseUri);

        var ok = service.TryGetRouteUrl("customer-details", new { id = 42 }, out var url, out var error);

        Assert.That(ok, Is.True);
        Assert.That(url, Is.EqualTo(expected));
        Assert.That(error, Is.Null);
        Assert.That(url, Is.EqualTo(service.GetRouteUrl("customer-details", new Dictionary<string, object?> { ["id"] = 42 })));
    }

    [Test]
    public void TryGetRouteUrl_takes_text_values_query_and_fragment()
    {
        var ok = Service("http://localhost/myapp/").TryGetRouteUrl(
            "customer-details",
            new Dictionary<string, object?> { ["id"] = "7" },
            out var url,
            out _,
            query: new Dictionary<string, object?> { ["tab"] = "orders" },
            fragment: "top");

        Assert.That(ok, Is.True);
        Assert.That(url, Is.EqualTo("/myapp/customers/7?tab=orders#top"));
    }

    [TestCase("customer-details", "abc", UrlGenerationErrorCode.ConstraintViolation)]
    [TestCase("customer-details", null, UrlGenerationErrorCode.MissingParameter)]
    [TestCase("nope", "1", UrlGenerationErrorCode.UnknownRoute)]
    public void TryGetRouteUrl_reports_errors_without_throwing(string route, string? id, UrlGenerationErrorCode code)
    {
        var values = id is null ? null : new Dictionary<string, object?> { ["id"] = id };
        bool ok = true;
        string? url = "unset";
        RouteUrlGenerationError? error = null;

        Assert.DoesNotThrow(() => ok = Service("http://localhost/").TryGetRouteUrl(route, values, out url, out error));
        Assert.That(ok, Is.False);
        Assert.That(url, Is.Null);
        Assert.That(error!.Code, Is.EqualTo(code));
        Assert.That(error.Message, Is.Not.Empty);
    }
}
