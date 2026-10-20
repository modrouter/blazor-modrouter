using ModRouter.Core.Matching;

namespace ModRouter.Test;

[TestFixture]
public sealed class RouteParametersTests
{
    static RouteParameters Sample() => new(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
    {
        ["id"] = 42,
        ["slug"] = "abc",
        ["when"] = null,
    });

    [Test]
    public void Get_returns_the_typed_value_case_insensitively()
    {
        Assert.That(Sample().Get<int>("ID"), Is.EqualTo(42));
    }

    [Test]
    public void Get_converts_between_numeric_types()
    {
        Assert.That(Sample().Get<long>("id"), Is.EqualTo(42L));
        Assert.That(Sample().Get<string>("id"), Is.EqualTo("42"));
    }

    [Test]
    public void Get_for_a_missing_name_lists_the_available_parameters()
    {
        var exception = Assert.Throws<KeyNotFoundException>(() => Sample().Get<int>("missing"));

        Assert.That(exception!.Message, Does.Contain("'missing'").And.Contain("id, slug, when"));
    }

    [Test]
    public void Get_for_an_unconvertible_value_names_both_types()
    {
        var exception = Assert.Throws<InvalidCastException>(() => Sample().Get<Guid>("slug"));

        Assert.That(exception!.Message, Does.Contain("'slug'").And.Contain("Guid"));
        Assert.Throws<InvalidCastException>(() => Sample().Get<int>("slug"));
    }

    [Test]
    public void TryGet_reports_success_and_failure_without_throwing()
    {
        var parameters = Sample();

        Assert.That(parameters.TryGet<int>("id", out var id), Is.True);
        Assert.That(id, Is.EqualTo(42));
        Assert.That(parameters.TryGet<int>("missing", out _), Is.False);
        Assert.That(parameters.TryGet<string>("when", out _), Is.False);
        Assert.That(parameters.TryGet<Guid>("slug", out _), Is.False);
    }

    [Test]
    public void Indexer_returns_the_raw_value_and_throws_for_missing_names()
    {
        var parameters = Sample();

        Assert.That(parameters["id"], Is.EqualTo(42));
        Assert.That(parameters["slug"], Is.EqualTo("abc"));
        Assert.Throws<KeyNotFoundException>(() => _ = parameters["missing"]);
    }

    [Test]
    public void Empty_has_no_values()
    {
        Assert.That(RouteParameters.Empty.Count, Is.EqualTo(0));
        Assert.That(RouteParameters.Empty.Contains("id"), Is.False);
    }

    static RouteParameters Build(params (string Name, object? Value)[] pairs)
        => new(pairs.ToDictionary(p => p.Name, p => p.Value, StringComparer.OrdinalIgnoreCase));

    [Test]
    public void Separately_built_equal_parameter_sets_are_equal()
    {
        var left = Build(("id", 42), ("slug", "abc"));
        var right = Build(("ID", 42), ("SLUG", "abc"));

        Assert.That(left, Is.EqualTo(right));
        Assert.That(left == right, Is.True);
        Assert.That(left != right, Is.False);
        Assert.That(left.Equals((object)right), Is.True);
        Assert.That(left.GetHashCode(), Is.EqualTo(right.GetHashCode()));
    }

    [Test]
    public void Parameter_sets_differing_in_a_value_or_a_key_are_not_equal()
    {
        var baseline = Build(("id", 42), ("slug", "abc"));

        Assert.That(baseline, Is.Not.EqualTo(Build(("id", 43), ("slug", "abc"))));
        Assert.That(baseline, Is.Not.EqualTo(Build(("id", 42))));
        Assert.That(baseline, Is.Not.EqualTo(Build(("id", 42), ("slug", "abc"), ("tab", "x"))));
        Assert.That(baseline, Is.Not.EqualTo(Build(("id", 42), ("other", "abc"))));
        Assert.That(baseline != Build(("id", 43), ("slug", "abc")), Is.True);
        Assert.That(baseline.Equals(null), Is.False);
        Assert.That(baseline == null, Is.False);
    }

    [Test]
    public void Equal_sets_built_in_a_different_insertion_order_share_a_hash()
    {
        var forward = Build(("a", 1), ("b", "two"), ("c", null));
        var backward = Build(("c", null), ("b", "two"), ("a", 1));

        Assert.That(forward, Is.EqualTo(backward));
        Assert.That(forward.GetHashCode(), Is.EqualTo(backward.GetHashCode()));
    }

    [Test]
    public void Values_is_not_a_mutable_dictionary()
    {
        var parameters = Build(("id", 1));

        Assert.That(parameters.Values, Is.Not.InstanceOf<Dictionary<string, object?>>());
        Assert.That(RouteParameters.Empty.Values, Is.Not.InstanceOf<Dictionary<string, object?>>());
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, object?>)RouteParameters.Empty.Values).Add("x", 1));
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, object?>)parameters.Values)["id"] = 2);
    }

    [Test]
    public void The_public_constructor_copies_its_input()
    {
        var source = new Dictionary<string, object?> { ["id"] = 1 };
        var parameters = new RouteParameters(source);

        source["id"] = 2;
        source["extra"] = 3;

        Assert.That(parameters["id"], Is.EqualTo(1));
        Assert.That(parameters.Contains("extra"), Is.False);
        Assert.That(parameters.Contains("ID"), Is.True);
    }

    [Test]
    public void Two_matches_of_the_same_url_have_equal_parameters()
    {
        var table = RouteTestKit.CustomersTable();
        var first = RouteTestKit.Match(table, "/customers/42/orders/4ec9a2a2-1b9f-47b9-8d1a-6e8b2f2b9d77");
        var second = RouteTestKit.Match(table, "/customers/42/orders/4ec9a2a2-1b9f-47b9-8d1a-6e8b2f2b9d77");

        Assert.That(first.Leaf.Parameters, Is.EqualTo(second.Leaf.Parameters));
        Assert.That(first.Leaf.InheritedParameters, Is.EqualTo(second.Leaf.InheritedParameters));
        Assert.That(first.Leaf.InheritedParameters, Is.Not.EqualTo(RouteTestKit.Match(table, "/customers/43/orders/4ec9a2a2-1b9f-47b9-8d1a-6e8b2f2b9d77").Leaf.InheritedParameters));
    }
}
