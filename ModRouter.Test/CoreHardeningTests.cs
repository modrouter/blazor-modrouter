using ModRouter.Core.Compilation;
using ModRouter.Core.Constraints;
using ModRouter.Core.Definitions;
using ModRouter.Core.Generation;
using ModRouter.Core.Syntax;

namespace ModRouter.Test;

/// <summary>Slice 1 review follow-ups that were scheduled for slice 2.</summary>
[TestFixture]
public sealed class CoreHardeningTests
{
    // ---- numeric and date formatting in generation --------------------------------------------------------------

    static IRouteConstraint Create(string specification)
    {
        Assert.That(new ConstraintRegistry().TryCreate(specification, out var constraint, out var error), Is.True, error);

        return constraint!;
    }

    [TestCase("min(1)", (byte)5, "5")]
    [TestCase("min(1)", 5u, "5")]
    [TestCase("min(1)", (short)5, "5")]
    [TestCase("max(10)", (sbyte)5, "5")]
    [TestCase("double", 1.5f, "1.5")]
    [TestCase("double", 2, "2")]
    [TestCase("float", 1.5, "1.5")]
    [TestCase("decimal", 1.5, "1.5")]
    [TestCase("long", (byte)7, "7")]
    [TestCase("int", 7u, "7")]
    public void Generation_accepts_any_numeric_type_that_converts_without_loss(string specification, object value, string expected)
    {
        Assert.That(Create(specification).TryFormat(value, out var text), Is.True);
        Assert.That(text, Is.EqualTo(expected));
    }

    [TestCase("int", 1.5)]
    [TestCase("int", 3000000000L)]
    [TestCase("long", 1.5f)]
    [TestCase("min(5)", (byte)4)]
    [TestCase("max(5)", 6u)]
    public void Generation_still_refuses_lossy_or_out_of_range_numbers(string specification, object value)
    {
        Assert.That(Create(specification).TryFormat(value, out _), Is.False);
    }

    [Test]
    public void Generation_accepts_a_decimal_for_a_double()
    {
        Assert.That(Create("double").TryFormat(1.5m, out var text), Is.True);
        Assert.That(text, Is.EqualTo("1.5"));
    }

    [Test]
    public void A_DateTimeOffset_formats_for_a_datetime_constraint()
    {
        var value = new DateTimeOffset(2024, 5, 1, 0, 0, 0, TimeSpan.FromHours(2));

        Assert.That(Create("datetime").TryFormat(value, out var text), Is.True);
        Assert.That(text, Is.EqualTo("2024-05-01"));
    }

    // ---- parser checks ------------------------------------------------------------------------------------------

    [TestCase("//", "Empty segment")]
    [TestCase("/a?b", "contains '?' or '#'")]
    [TestCase("/a/b#c", "contains '?' or '#'")]
    [TestCase("/caf%C3%A9", "percent-encoded sequence")]
    [TestCase("/a%2Fb", "percent-encoded sequence")]
    public void The_parser_rejects_literals_that_can_never_match(string template, string expectedFragment)
    {
        var result = TemplateParser.Parse(template);

        Assert.That(result.Succeeded, Is.False);
        Assert.That(result.Errors.Select(e => e.Message), Has.Some.Contain(expectedFragment));
    }

    [TestCase("/")]
    [TestCase("/café")]
    [TestCase("/100%")]
    [TestCase("/a/{id?}")]
    [TestCase("/files/{*path}")]
    public void The_parser_still_accepts_ordinary_templates(string template)
    {
        Assert.That(TemplateParser.Parse(template).Succeeded, Is.True);
    }

    [Test]
    public void A_top_level_template_must_start_with_a_slash()
    {
        var exception = Assert.Throws<RouteCompilationException>(() => RouteTestKit.Compile(map => map.Route("customers", typeof(HomePage))))!;

        Assert.That(exception.Errors.Select(e => e.Code), Is.EqualTo(new[] { "RT0014" }));
        Assert.That(exception.Errors[0].Message, Does.Contain("must start with '/'"));
        Assert.That(exception.Errors[0].Template, Is.EqualTo("customers"));
    }

    // ---- dot segments -------------------------------------------------------------------------------------------

    [TestCase("..")]
    [TestCase(".")]
    public void Generation_rejects_dot_segments(string value)
    {
        var table = RouteTestKit.Flat(("/a/{x}", "one"), ("/f/{*path}", "files"));

        var parameter = Assert.Throws<RouteUrlGenerationException>(() => table.GenerateUrl("one", new Dictionary<string, object?> { ["x"] = value }))!;
        var catchAll = Assert.Throws<RouteUrlGenerationException>(() => table.GenerateUrl("files", new Dictionary<string, object?> { ["path"] = "a/" + value + "/b" }))!;

        Assert.That(parameter.Code, Is.EqualTo(UrlGenerationErrorCode.ConstraintViolation));
        Assert.That(parameter.Message, Does.Contain("dot segment"));
        Assert.That(catchAll.Code, Is.EqualTo(UrlGenerationErrorCode.ConstraintViolation));
    }

    [Test]
    public void Names_that_merely_contain_dots_are_fine()
    {
        var table = RouteTestKit.Flat(("/a/{x}", "one"));

        Assert.That(table.GenerateUrl("one", new Dictionary<string, object?> { ["x"] = "..a" }), Is.EqualTo("/a/..a"));
        Assert.That(table.GenerateUrl("one", new Dictionary<string, object?> { ["x"] = "v1.2" }), Is.EqualTo("/a/v1.2"));
    }

    // ---- custom constraints -------------------------------------------------------------------------------------

    [Test]
    public void A_constraint_factory_that_throws_anything_becomes_a_compile_error_not_an_escaped_exception()
    {
        var registry = new ConstraintRegistry();
        registry.Register("boom", _ => throw new InvalidOperationException("factory exploded"));

        var exception = Assert.Throws<RouteCompilationException>(
            () => RouteTestKit.Compile(map => map.Route("/a/{x:boom}", typeof(HomePage)), registry))!;

        Assert.That(exception.Errors.Select(e => e.Code), Is.EqualTo(new[] { "RT0004" }));
        Assert.That(exception.Errors[0].Message, Does.Contain("factory exploded"));
    }

    [Test]
    public void A_custom_refinement_does_not_take_over_the_converted_type()
    {
        var registry = new ConstraintRegistry();
        registry.Register("mine", _ => new OddOnly());
        var table = RouteTestKit.Compile(map => map.Route("/n/{x:mine:int}", typeof(HomePage), name: "n"), registry);

        var chain = RouteTestKit.Match(table, "/n/7");

        Assert.That(chain.Leaf.Parameters["x"], Is.TypeOf<int>().And.EqualTo(7));
        Assert.That(table.Match("/n/8").IsMatch, Is.False);
    }

    sealed class OddOnly : IRefinementConstraint
    {
        public Type ValueType => typeof(string);

        public bool TryMatch(string rawValue, out object? convertedValue)
        {
            convertedValue = rawValue;

            return int.TryParse(rawValue, out var number) && number % 2 == 1;
        }

        public bool TryFormat(object value, out string text)
        {
            text = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;

            return TryMatch(text, out _);
        }
    }
}
