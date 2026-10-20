using ModRouter.Core.Constraints;

namespace ModRouter.Test;

[TestFixture]
public sealed class ConstraintTests
{
    static readonly ConstraintRegistry Registry = new();

    static IRouteConstraint Create(string specification)
    {
        Assert.That(Registry.TryCreate(specification, out var constraint, out var error), Is.True, error);

        return constraint!;
    }

    [TestCase("int", "42", typeof(int))]
    [TestCase("long", "9999999999", typeof(long))]
    [TestCase("bool", "true", typeof(bool))]
    [TestCase("bool", "False", typeof(bool))]
    [TestCase("decimal", "1.5", typeof(decimal))]
    [TestCase("double", "1e3", typeof(double))]
    [TestCase("float", "2.5", typeof(float))]
    [TestCase("guid", "123e4567-e89b-12d3-a456-426614174000", typeof(Guid))]
    [TestCase("datetime", "2024-05-01", typeof(DateTime))]
    [TestCase("alpha", "abc", typeof(string))]
    [TestCase("alphanum", "ab12", typeof(string))]
    [TestCase("min(5)", "5", typeof(long))]
    [TestCase("max(5)", "5", typeof(long))]
    [TestCase("length(3)", "abc", typeof(string))]
    [TestCase("length(2,4)", "ab", typeof(string))]
    [TestCase("regex(^[a-z0-9-]+$)", "my-slug-2", typeof(string))]
    [TestCase("minlength(2)", "ab", typeof(string))]
    [TestCase("maxlength(4)", "abcd", typeof(string))]
    [TestCase("range(2,8)", "2", typeof(long))]
    [TestCase("range(2,8)", "8", typeof(long))]
    [TestCase("file", "report.pdf", typeof(string))]
    [TestCase("file", "dir/report.pdf", typeof(string))]
    [TestCase("file", ".htaccess", typeof(string))]
    [TestCase("nonfile", "report", typeof(string))]
    [TestCase("nonfile", "dir.d/report", typeof(string))]
    [TestCase("nonfile", "file..", typeof(string))]
    [TestCase("nonfile", "", typeof(string))]
    public void Accepts_valid_values_and_converts_them(string specification, string raw, Type expectedType)
    {
        var constraint = Create(specification);

        Assert.That(constraint.TryMatch(raw, out var value), Is.True);
        Assert.That(value, Is.InstanceOf(expectedType));
        Assert.That(constraint.ValueType, Is.EqualTo(expectedType));
    }

    [TestCase("int", "4.2")]
    [TestCase("int", "abc")]
    [TestCase("int", "9999999999")]
    [TestCase("long", "1.5")]
    [TestCase("bool", "yes")]
    [TestCase("decimal", "x")]
    [TestCase("double", "x")]
    [TestCase("float", "x")]
    [TestCase("guid", "not-a-guid")]
    [TestCase("datetime", "nope")]
    [TestCase("alpha", "ab1")]
    [TestCase("alpha", "")]
    [TestCase("alpha", "é")]
    [TestCase("alphanum", "a-b")]
    [TestCase("min(5)", "4")]
    [TestCase("min(5)", "x")]
    [TestCase("max(5)", "6")]
    [TestCase("length(3)", "ab")]
    [TestCase("length(2,4)", "abcde")]
    [TestCase("regex(^[a-z]+$)", "abc1")]
    [TestCase("minlength(2)", "a")]
    [TestCase("maxlength(4)", "abcde")]
    [TestCase("range(2,8)", "1")]
    [TestCase("range(2,8)", "9")]
    [TestCase("range(2,8)", "x")]
    [TestCase("file", "report")]
    [TestCase("file", "file.")]
    [TestCase("file", "dir.d/report")]
    [TestCase("file", "")]
    [TestCase("nonfile", "report.pdf")]
    [TestCase("nonfile", "dir/report.pdf")]
    public void Rejects_invalid_values(string specification, string raw)
    {
        Assert.That(Create(specification).TryMatch(raw, out _), Is.False);
    }

    [Test]
    public void Regex_constraint_is_case_insensitive()
    {
        Assert.That(Create("regex(^[a-z-]+$)").TryMatch("MY-SLUG", out _), Is.True);
    }

    [TestCase("int", 42, "42")]
    [TestCase("int", 42L, "42")]
    [TestCase("long", 42, "42")]
    [TestCase("bool", true, "true")]
    [TestCase("decimal", 2, "2")]
    [TestCase("alpha", "abc", "abc")]
    [TestCase("min(5)", 7, "7")]
    [TestCase("range(2,8)", 5, "5")]
    [TestCase("range(2,8)", "5", "5")]
    [TestCase("minlength(2)", "abc", "abc")]
    [TestCase("nonfile", "report", "report")]
    [TestCase("int", "42", "42")]
    public void Formats_values(string specification, object value, string expected)
    {
        Assert.That(Create(specification).TryFormat(value, out var text), Is.True);
        Assert.That(text, Is.EqualTo(expected));
    }

    [TestCase("int", 1.5)]
    [TestCase("int", "x")]
    [TestCase("int", true)]
    [TestCase("alpha", "ab1")]
    [TestCase("min(5)", 4)]
    [TestCase("guid", 5)]
    [TestCase("range(2,8)", 9)]
    [TestCase("maxlength(2)", "abc")]
    [TestCase("file", "report")]
    public void Refuses_to_format_values_that_violate_the_constraint(string specification, object value)
    {
        Assert.That(Create(specification).TryFormat(value, out _), Is.False);
    }

    [Test]
    public void Formats_guids_in_D_format_and_dates_round_trippably()
    {
        var guid = Guid.Parse("4EC9A2A2-1B9F-47B9-8D1A-6E8B2F2B9D77");
        Assert.That(Create("guid").TryFormat(guid, out var guidText), Is.True);
        Assert.That(guidText, Is.EqualTo("4ec9a2a2-1b9f-47b9-8d1a-6e8b2f2b9d77"));

        var datetime = Create("datetime");
        Assert.That(datetime.TryFormat(new DateTime(2024, 5, 1), out var dateOnly), Is.True);
        Assert.That(dateOnly, Is.EqualTo("2024-05-01"));

        var withTime = new DateTime(2024, 5, 1, 13, 30, 15);
        Assert.That(datetime.TryFormat(withTime, out var timeText), Is.True);
        Assert.That(datetime.TryMatch(timeText, out var parsed), Is.True);
        Assert.That(parsed, Is.EqualTo(withTime));
    }

    [Test]
    public void Composite_requires_every_part_and_converts_with_the_typed_part()
    {
        var composite = new CompositeConstraint([Create("min(10)"), Create("int")]);

        Assert.That(composite.TryMatch("42", out var value), Is.True);
        Assert.That(value, Is.EqualTo(42).And.InstanceOf<int>());
        Assert.That(composite.ValueType, Is.EqualTo(typeof(int)));
        Assert.That(composite.TryMatch("5", out _), Is.False);
        Assert.That(composite.TryMatch("x", out _), Is.False);
        Assert.That(composite.TryFormat(42, out var text), Is.True);
        Assert.That(text, Is.EqualTo("42"));
        Assert.That(composite.TryFormat(5, out _), Is.False);
    }

    [Test]
    public void Composite_of_only_refinements_yields_the_raw_string()
    {
        var composite = new CompositeConstraint([Create("alpha"), Create("length(2,4)")]);

        Assert.That(composite.TryMatch("abc", out var value), Is.True);
        Assert.That(value, Is.EqualTo("abc"));
        Assert.That(composite.ValueType, Is.EqualTo(typeof(string)));
        Assert.That(composite.TryMatch("abcdef", out _), Is.False);
    }

    [Test]
    public void Constraint_names_are_case_insensitive()
    {
        Assert.That(Create("INT").TryMatch("5", out _), Is.True);
    }

    [TestCase("foo", "Unknown route constraint 'foo'")]
    [TestCase("int(5)", "does not take an argument")]
    [TestCase("length(x)", "length expects")]
    [TestCase("length(5,2)", "smaller")]
    [TestCase("length(3", "closing")]
    [TestCase("regex()", "expects a pattern")]
    [TestCase("regex(()", "Invalid argument for constraint 'regex'")]
    [TestCase("min(abc)", "not a whole number")]
    [TestCase("range(8,2)", "smaller")]
    [TestCase("range(5)", "range expects")]
    [TestCase("range(a,b)", "range expects")]
    [TestCase("minlength(-1)", "minlength expects")]
    [TestCase("maxlength(x)", "maxlength expects")]
    [TestCase("file(x)", "does not take an argument")]
    [TestCase("nonfile(x)", "does not take an argument")]
    public void Reports_actionable_errors_for_invalid_specifications(string specification, string expectedFragment)
    {
        Assert.That(Registry.TryCreate(specification, out var constraint, out var error), Is.False);
        Assert.That(constraint, Is.Null);
        Assert.That(error, Does.Contain(expectedFragment));
    }

    [Test]
    public void Unknown_constraint_error_lists_the_registered_names()
    {
        Registry.TryCreate("foo", out _, out var error);

        Assert.That(error, Does.Contain("Registered constraints:").And.Contain("alphanum").And.Contain("guid"));
    }

    [Test]
    public void Custom_constraints_receive_their_argument()
    {
        var registry = new ConstraintRegistry();
        registry.Register("multiple", argument => new MultipleOfConstraint(int.Parse(argument)));

        Assert.That(registry.TryCreate("multiple(3)", out var constraint), Is.True);
        Assert.That(constraint!.TryMatch("9", out var value), Is.True);
        Assert.That(value, Is.EqualTo(9));
        Assert.That(constraint.TryMatch("10", out _), Is.False);
    }

    sealed class MultipleOfConstraint(int factor) : IRouteConstraint
    {
        public Type ValueType => typeof(int);

        public bool TryMatch(string rawValue, out object? convertedValue)
        {
            if (int.TryParse(rawValue, out var number) && number % factor == 0)
            {
                convertedValue = number;

                return true;
            }

            convertedValue = null;

            return false;
        }

        public bool TryFormat(object value, out string text)
        {
            text = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;

            return TryMatch(text, out _);
        }
    }
}
