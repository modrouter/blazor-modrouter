using ModRouter.Core.Syntax;

namespace ModRouter.Test;

[TestFixture]
public sealed class TemplateParserTests
{
    [Test]
    public void Parses_static_template()
    {
        var result = TemplateParser.Parse("/dashboard");

        Assert.That(result.Succeeded, Is.True);
        Assert.That(result.HasLeadingSlash, Is.True);
        Assert.That(result.Segments, Has.Count.EqualTo(1));
        Assert.That(result.Segments[0].Kind, Is.EqualTo(TemplateSegmentKind.Literal));
        Assert.That(result.Segments[0].Text, Is.EqualTo("dashboard"));
    }

    [TestCase("/")]
    [TestCase("")]
    public void Root_templates_have_no_segments(string template)
    {
        var result = TemplateParser.Parse(template);

        Assert.That(result.Succeeded, Is.True);
        Assert.That(result.Segments, Is.Empty);
    }

    [Test]
    public void Relative_template_has_no_leading_slash()
    {
        var result = TemplateParser.Parse("orders/{orderId:guid}");

        Assert.That(result.Succeeded, Is.True);
        Assert.That(result.HasLeadingSlash, Is.False);
        Assert.That(result.Segments, Has.Count.EqualTo(2));
    }

    [Test]
    public void A_single_trailing_slash_is_ignored()
    {
        var result = TemplateParser.Parse("/a/");

        Assert.That(result.Succeeded, Is.True);
        Assert.That(result.Segments, Has.Count.EqualTo(1));
    }

    [Test]
    public void Parses_parameter_with_multiple_constraints()
    {
        var result = TemplateParser.Parse("/users/{name:alpha:length(2,40)}");

        Assert.That(result.Succeeded, Is.True);
        var segment = result.Segments[1];
        Assert.That(segment.Kind, Is.EqualTo(TemplateSegmentKind.Parameter));
        Assert.That(segment.Text, Is.EqualTo("name"));
        Assert.That(segment.Constraints.Select(c => c.ToString()), Is.EqualTo(new[] { "alpha", "length(2,40)" }));
        Assert.That(segment.ConstraintKey, Is.EqualTo("alpha:length(2,40)"));
        Assert.That(segment.ToString(), Is.EqualTo("{name:alpha:length(2,40)}"));
    }

    [Test]
    public void Parses_regex_constraint_from_the_brief()
    {
        var result = TemplateParser.Parse("/docs/{slug:regex(^[a-z0-9-]+$)}");

        Assert.That(result.Succeeded, Is.True);
        var spec = result.Segments[1].Constraints.Single();
        Assert.That(spec.Name, Is.EqualTo("regex"));
        Assert.That(spec.Argument, Is.EqualTo("^[a-z0-9-]+$"));
    }

    [Test]
    public void Regex_argument_may_contain_braces_slashes_and_colons()
    {
        var result = TemplateParser.Parse("/docs/{slug:regex(^(?:[a-z0-9-]{3}/x)$)}/end");

        Assert.That(result.Succeeded, Is.True, string.Join("; ", result.Errors.Select(e => e.Message)));
        Assert.That(result.Segments, Has.Count.EqualTo(3));
        Assert.That(result.Segments[1].Constraints.Single().Argument, Is.EqualTo("^(?:[a-z0-9-]{3}/x)$"));
        Assert.That(result.Segments[2].Text, Is.EqualTo("end"));
    }

    [TestCase("/customers/{id?}", "id", "{id?}")]
    [TestCase("/customers/{id:int?}", "id", "{id:int?}")]
    public void Parses_optional_parameters(string template, string name, string display)
    {
        var result = TemplateParser.Parse(template);

        Assert.That(result.Succeeded, Is.True);
        var segment = result.Segments[1];
        Assert.That(segment.Kind, Is.EqualTo(TemplateSegmentKind.OptionalParameter));
        Assert.That(segment.Text, Is.EqualTo(name));
        Assert.That(segment.ToString(), Is.EqualTo(display));
    }

    [TestCase("/files/{*path}")]
    [TestCase("/files/{**path}")]
    public void Single_and_double_star_catch_alls_are_the_same(string template)
    {
        var result = TemplateParser.Parse(template);

        Assert.That(result.Succeeded, Is.True);
        Assert.That(result.Segments[1].Kind, Is.EqualTo(TemplateSegmentKind.CatchAll));
        Assert.That(result.Segments[1].Text, Is.EqualTo("path"));
        Assert.That(result.Segments[1].ToString(), Is.EqualTo("{*path}"));
    }

    [TestCase("/a//b", "Empty segment")]
    [TestCase("/a/{id", "Unclosed")]
    [TestCase("/{}", "not a valid parameter name")]
    [TestCase("/{1a}", "not a valid parameter name")]
    [TestCase("/pre{id}", "mixes literal text")]
    [TestCase("/{id}post", "text after '}'")]
    [TestCase("/{id:}", "empty constraint")]
    [TestCase("/{id:int(5}", "Unclosed")]
    [TestCase("/{id:in-t}", "not a valid constraint name")]
    [TestCase("/{*path?}", "cannot also be optional")]
    [TestCase("/{*path:int}", "not supported on catch-all")]
    public void Reports_syntax_errors(string template, string expectedFragment)
    {
        var result = TemplateParser.Parse(template);

        Assert.That(result.Succeeded, Is.False);
        Assert.That(result.Errors.Select(e => e.Message), Has.Some.Contain(expectedFragment));
    }

    [Test]
    public void Error_position_points_at_the_offending_segment()
    {
        var result = TemplateParser.Parse("/a/{id");

        Assert.That(result.Errors.Single().Position, Is.EqualTo(3));
    }
}
