using ModRouter.Components;
using ModRouter.Core.Compilation;
using ModRouter.Core.Definitions;
using ModRouter.Core.Matching;
using ModRouter.Navigation;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace ModRouter.Test;

[TestFixture]
public sealed class RouteComponentBinderTests
{
    static readonly RouteComponentBinder Binder = new();

    static RouteContext Level(CompiledRouteTable table, string path, int index = -1)
    {
        var chain = RouteTestKit.Match(table, path);

        return index < 0 ? chain.Leaf : chain.Matches[index];
    }

    [Test]
    public void Binds_route_values_to_declared_parameters_case_insensitively_with_conversion()
    {
        var table = RouteTestKit.Compile(map => map.Route("/q/{id}/{plain}/{optionalNumber}/{DAY}", typeof(QueryParameterPage)));

        Assert.That(Binder.TryBind(Level(table, "/q/1/abc/7/monday"), out var values, out var error), Is.True, error);

        var byName = values.ToDictionary(v => v.Key, v => v.Value);
        Assert.That(byName["Plain"], Is.EqualTo("abc"));
        Assert.That(byName["OptionalNumber"], Is.EqualTo(7));
        Assert.That(byName["Day"], Is.EqualTo(DayOfWeek.Monday));
    }

    [Test]
    public void Query_attributed_cascading_and_unmatched_value_properties_are_never_bound_from_the_path()
    {
        var table = RouteTestKit.Compile(map => map.Route("/q/{id}/{cascaded}/{rest}/{other}", typeof(QueryParameterPage)));

        Binder.TryBind(Level(table, "/q/1/2/3/4"), out var values, out _);

        Assert.That(values.Select(v => v.Key), Is.Empty);
    }

    [Test]
    public void Names_the_component_does_not_declare_are_ignored()
    {
        var table = RouteTestKit.Compile(map => map.Route("/x/{nothing}", typeof(QueryParameterPage)));

        Assert.That(Binder.TryBind(Level(table, "/x/1"), out var values, out _), Is.True);
        Assert.That(values, Is.Empty);
    }

    [Test]
    public void A_child_receives_its_ancestors_values_when_it_declares_the_parameter()
    {
        var table = RouteTestKit.CustomersTable();
        var order = Level(table, "/customers/42/orders/4ec9a2a2-1b9f-47b9-8d1a-6e8b2f2b9d77");

        Binder.TryBind(order, out var values, out _);

        Assert.That(values, Is.Empty, "the plain OrderDetails test class declares no [Parameter]s");

        var withParameters = RouteTestKit.Compile(map => map.Route("/c/{id:int}", typeof(DetailsProbe), children: c => c.Route("o/{orderId:guid}", typeof(OrderProbe))));
        Binder.TryBind(Level(withParameters, "/c/42/o/4ec9a2a2-1b9f-47b9-8d1a-6e8b2f2b9d77"), out values, out _);

        Assert.That(values.Select(v => v.Key).Order(), Is.EqualTo(new[] { "Id", "OrderId" }));
    }

    [Test]
    public void A_value_that_cannot_convert_is_reported_with_both_types()
    {
        var table = RouteTestKit.Compile(map => map.Route("/g/{id}", typeof(GuidProbe)));

        Assert.That(Binder.TryBind(Level(table, "/g/abc"), out _, out var error), Is.False);
        Assert.That(error, Does.Contain("'id'").And.Contain("GuidProbe.Id").And.Contain("Guid"));
    }

    [Test]
    public void Validation_accepts_runtime_convertible_pairs_and_rejects_impossible_ones()
    {
        var ok = RouteTestKit.Compile(map => map.Route("/g/{id}", typeof(GuidProbe)));
        var impossible = RouteTestKit.Compile(map => map.Route("/g/{id:int}", typeof(GuidProbe), name: "bad"));
        var notAComponent = RouteTestKit.Compile(map => map.Route("/h", typeof(HomePage), name: "plain"));

        Assert.That(Binder.Validate(ok), Is.Empty);
        Assert.That(Binder.Validate(impossible), Has.Exactly(1).Contain("'id' is Int32 but GuidProbe.Id is Guid, which can never be converted").And.Exactly(1).Contain("'bad'"));
        Assert.That(Binder.Validate(notAComponent), Has.Exactly(1).Contain("is not a component"));
    }

    // ---- binding supplies only what the URL has; the outlet asks for resets of what the previous render supplied ----

    static Dictionary<string, object?> Bound(RouteComponentBinder binder, CompiledRouteTable table, string path)
    {
        Assert.That(binder.TryBind(Level(table, path), out var values, out var error), Is.True, error);

        return values.ToDictionary(v => v.Key, v => v.Value);
    }

    [Test]
    public void An_omitted_optional_parameter_and_a_parameter_only_another_route_supplies_are_not_bound()
    {
        var optional = RouteTestKit.Compile(map => map.Route("/list/{page:int?}", typeof(PageProbe)));
        var shared = RouteTestKit.Compile(map => map.Route("/a/{id:int}", typeof(DetailsProbe)).Route("/b", typeof(DetailsProbe)));

        Assert.That(Bound(Binder, optional, "/list/3")["Page"], Is.EqualTo(3));
        Assert.That(Bound(Binder, optional, "/list"), Is.Empty, "nothing is supplied, so nothing is bound: a fresh instance keeps its initializers");
        Assert.That(Bound(Binder, shared, "/a/5")["Id"], Is.EqualTo(5));
        Assert.That(Bound(Binder, shared, "/b"), Is.Empty);
    }

    [Test]
    public void Resets_give_the_type_default_of_what_the_previous_render_supplied_and_this_match_omits()
    {
        var optional = RouteTestKit.Compile(map => map.Route("/list/{page:int?}", typeof(PageProbe)));
        var shared = RouteTestKit.Compile(map => map.Route("/a/{id:int}", typeof(DetailsProbe)).Route("/b", typeof(DetailsProbe)));

        var page = Binder.Resets(typeof(PageProbe), Binder.Bind(Level(optional, "/list")), Binder.Bind(Level(optional, "/list/3")).Select(v => v.Key));
        Assert.That(page.Select(v => $"{v.Key}={v.Value ?? "null"}"), Is.EqualTo(new[] { "Page=null" }), "/list/3 then /list: a nullable gets null");

        var id = Binder.Resets(typeof(DetailsProbe), Binder.Bind(Level(shared, "/b")), Binder.Bind(Level(shared, "/a/5")).Select(v => v.Key));
        Assert.That(id.Select(v => $"{v.Key}={v.Value}"), Is.EqualTo(new[] { "Id=0" }), "/a/5 then /b: an int gets its default, so a reused instance cannot keep the old id");
    }

    [Test]
    public void Resets_never_touch_a_parameter_the_previous_render_did_not_supply_or_this_one_supplies()
    {
        var shared = RouteTestKit.Compile(map => map.Route("/a/{id:int}", typeof(DetailsProbe)).Route("/b", typeof(DetailsProbe)));

        Assert.That(Binder.Resets(typeof(DetailsProbe), Binder.Bind(Level(shared, "/b")), []), Is.Empty, "/b then /b?x=1: Id was never supplied, so the initializer stands");
        Assert.That(Binder.Resets(typeof(DetailsProbe), Binder.Bind(Level(shared, "/a/5")), ["Id"]), Is.Empty, "/a/4 then /a/5: Id is supplied again");
        Assert.That(Binder.Resets(typeof(OrderProbe), [], ["Nope"]), Is.Empty, "a name the component does not declare is ignored");
    }

    [Test]
    public void An_ancestor_level_binds_only_its_own_parameters()
    {
        // OrderProbe at depth 1 inherits id; DetailsProbe at depth 0 never receives orderId.
        var table = RouteTestKit.Compile(map => map.Route("/c/{id:int}", typeof(DetailsProbe), children: c => c.Route("o/{orderId:guid}", typeof(OrderProbe))));

        Assert.That(Binder.TryBind(Level(table, "/c/42/o/4ec9a2a2-1b9f-47b9-8d1a-6e8b2f2b9d77", index: 0), out var values, out _), Is.True);
        Assert.That(values.Select(v => v.Key), Is.EqualTo(new[] { "Id" }));
    }

    [Test]
    public void Validation_rejects_an_optional_parameter_bound_to_a_non_nullable_value_type()
    {
        var bad = RouteTestKit.Compile(map => map.Route("/d/{id:int?}", typeof(DetailsProbe), name: "opt"));
        var ok = RouteTestKit.Compile(map => map.Route("/list/{page:int?}", typeof(PageProbe)).Route("/s/{plain?}", typeof(QueryParameterPage)));

        Assert.That(Binder.Validate(bad), Has.Exactly(1).Contain("'id' is optional").And.Contain("DetailsProbe.Id").And.Contain("must be nullable").And.Contain("'opt'"));
        Assert.That(Binder.Validate(ok), Is.Empty);
    }

    // ---- numeric conversions: widening only, never a lossy one --------------------------------------------------

    sealed class IntXPage : ComponentBase
    {
        [Parameter]
        public int X { get; set; }
    }

    [TestCase(2.5, typeof(int), false)]
    [TestCase(2.0, typeof(int), true)]
    [TestCase(5, typeof(double), true)]
    [TestCase(3000000000L, typeof(int), false)]
    [TestCase(0.1f, typeof(double), true)]
    [TestCase(-1, typeof(uint), false)]
    [TestCase(2.5, typeof(int?), false)]
    public void Numeric_conversion_refuses_a_lossy_result(object value, Type target, bool expected)
    {
        Assert.That(ValueConverter.TryConvert(value, target, out var result), Is.EqualTo(expected));

        if (expected)
        {
            Assert.That(result, Is.EqualTo(Convert.ChangeType(value, Nullable.GetUnderlyingType(target) ?? target, System.Globalization.CultureInfo.InvariantCulture)));
        }
    }

    [Test]
    public void A_fractional_decimal_is_not_rounded_into_an_integer()
    {
        Assert.That(ValueConverter.TryConvert(2.5m, typeof(int), out _), Is.False);
        Assert.That(ValueConverter.TryConvert(2m, typeof(int), out var whole), Is.True);
        Assert.That(whole, Is.EqualTo(2));
    }

    [TestCase(typeof(double), typeof(int), false)]
    [TestCase(typeof(float), typeof(long), false)]
    [TestCase(typeof(decimal), typeof(int), false)]
    [TestCase(typeof(double), typeof(int?), false)]
    [TestCase(typeof(double), typeof(DayOfWeek), false)]
    [TestCase(typeof(int), typeof(double), true)]
    [TestCase(typeof(int), typeof(long), true)]
    [TestCase(typeof(long), typeof(int), true)]
    [TestCase(typeof(double), typeof(decimal), true)]
    public void CanEverConvert_rejects_floating_point_sources_for_integral_targets(Type source, Type target, bool expected)
    {
        Assert.That(ValueConverter.CanEverConvert(source, target), Is.EqualTo(expected));
    }

    [Test]
    public void Validation_rejects_a_double_parameter_bound_to_an_int_property()
    {
        var table = RouteTestKit.Compile(map => map.Route("/p/{x:double}", typeof(IntXPage), name: "p"));

        Assert.That(Binder.Validate(table), Has.Exactly(1).Contain("'x' is Double but IntXPage.X is Int32, which can never be converted"));
    }

    [Test]
    public void A_double_parameter_binds_to_a_double_property()
    {
        var table = RouteTestKit.Compile(map => map.Route("/p/{x:double}", typeof(DoubleProbe)));

        Assert.That(Bound(Binder, table, "/p/2.5")["X"], Is.EqualTo(2.5));
    }

    [TestCase(42, typeof(int), true)]
    [TestCase(42, typeof(long), true)]
    [TestCase(42, typeof(string), true)]
    [TestCase("5", typeof(int), true)]
    [TestCase("x", typeof(int), false)]
    [TestCase("Monday", typeof(DayOfWeek), true)]
    [TestCase("Nope", typeof(DayOfWeek), false)]
    [TestCase(null, typeof(int?), true)]
    [TestCase(null, typeof(int), false)]
    [TestCase(true, typeof(Guid), false)]
    public void ValueConverter_converts_or_refuses(object? value, Type target, bool expected)
    {
        Assert.That(ValueConverter.TryConvert(value, target, out _), Is.EqualTo(expected));
    }
}
