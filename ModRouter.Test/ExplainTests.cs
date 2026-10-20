using ModRouter.Core.Compilation;

namespace ModRouter.Test;

[TestFixture]
public sealed class ExplainTests
{
    static CompiledRouteTable Precedence() => RouteTestKit.Flat(
        ("/customers/{id:int}", "by-id"),
        ("/customers/new", "new"),
        ("/about", "about"));

    [Test]
    public void Explains_every_candidate_and_the_selected_route_exactly_like_the_brief()
    {
        var text = Precedence().Explain("/customers/new").ToString();

        Assert.That(text, Is.EqualTo(string.Join(
            "\n",
            "URL: /customers/new",
            "",
            "Candidate: /customers/{id:int}",
            "    \"customers\" → static match",
            "    \"new\" → failed int constraint",
            "",
            "Candidate: /customers/new",
            "    \"customers\" → static match",
            "    \"new\" → static match",
            "",
            "Selected: /customers/new")));
    }

    [Test]
    public void Parameter_and_static_failures_use_distinct_wording()
    {
        var explanation = Precedence().Explain("/customers/5");

        Assert.That(explanation.Candidates[0].Verdicts, Is.EqualTo(new[] { "\"customers\" → static match", "\"5\" → matched 'id' (int)" }));
        Assert.That(explanation.Candidates[1].Verdicts, Is.EqualTo(new[] { "\"customers\" → static match", "\"5\" → failed static match (expected 'new')" }));
        Assert.That(explanation.SelectedTemplate, Is.EqualTo("/customers/{id:int}"));
        Assert.That(explanation.SelectedRouteName, Is.EqualTo("by-id"));
    }

    [Test]
    public void An_unmatched_path_selects_nothing()
    {
        var explanation = Precedence().Explain("/nope");

        Assert.That(explanation.Candidates, Is.Empty);
        Assert.That(explanation.SelectedTemplate, Is.Null);
        Assert.That(explanation.ToString(), Is.EqualTo("URL: /nope\n\nSelected: (none)"));
    }

    [Test]
    public void Extra_segments_are_reported()
    {
        var explanation = RouteTestKit.Flat(("/a", "a")).Explain("/a/b");

        Assert.That(explanation.Candidates.Single().Verdicts, Is.EqualTo(new[] { "\"a\" → static match", "\"b\" → no segment in the template to match it" }));
        Assert.That(explanation.Candidates.Single().IsMatch, Is.False);
        Assert.That(explanation.SelectedTemplate, Is.Null);
    }

    [Test]
    public void Optional_and_catch_all_segments_have_their_own_wording()
    {
        var table = RouteTestKit.Flat(("/a/{id?}", "opt"), ("/f/{*p}", "files"));

        Assert.That(table.Explain("/a").Candidates.Single().Verdicts, Is.EqualTo(new[] { "\"a\" → static match", "(end of path) → optional 'id' omitted" }));
        Assert.That(table.Explain("/f/x/y").Candidates.Single().Verdicts, Is.EqualTo(new[] { "\"f\" → static match", "\"x/y\" → catch-all 'p'" }));
    }

    [Test]
    public void A_missing_required_segment_is_reported()
    {
        var explanation = RouteTestKit.Flat(("/a/{id}", "req")).Explain("/a");

        Assert.That(explanation.Candidates.Single().Verdicts, Is.EqualTo(new[] { "\"a\" → static match", "(end of path) → required parameter 'id' has no value" }));
    }

    [Test]
    public void A_malformed_path_is_explained_without_throwing()
    {
        var explanation = Precedence().Explain("/a/%zz");

        Assert.That(explanation.IsMalformed, Is.True);
        Assert.That(explanation.SelectedTemplate, Is.Null);
        Assert.That(explanation.ToString(), Does.Contain("malformed"));
    }

    [Test]
    public void The_root_path_considers_routes_that_can_match_an_empty_path()
    {
        var table = RouteTestKit.Flat(("/", "home"), ("/a", "a"), ("/{id?}", "opt"));

        var explanation = table.Explain("/");

        Assert.That(explanation.Candidates.Select(c => c.Template), Is.EqualTo(new[] { "/", "/{id?}" }));
        Assert.That(explanation.SelectedTemplate, Is.EqualTo("/"));
    }
}
