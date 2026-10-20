using System.Reflection;
using ModRouter.Core.Constraints;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ModRouter.Test;

/// <summary>
/// Parity with the built-in router's constraints. Since .NET 8 the Blazor router compiles the shared ASP.NET Core routing
/// sources: the constraint names come from <c>Microsoft.AspNetCore.Routing.RouteOptions.GetDefaultConstraintMap</c> (plus
/// <c>regex</c>, which <c>RouteTableFactory</c> adds; in the browser only with the
/// <c>Microsoft.AspNetCore.Components.Routing.RegexConstraintSupport</c> switch), and each value is checked by that map's
/// <c>IRouteConstraint.Match</c>. Both are internal to <c>Microsoft.AspNetCore.Components</c>, so the tests reach them by
/// reflection and compare them with <see cref="ConstraintRegistry"/> on the same specifications and value strings.
/// </summary>
[TestFixture]
public sealed class BuiltInConstraintParityTests
{
    static readonly Assembly Components = typeof(Microsoft.AspNetCore.Components.RouteData).Assembly;
    static readonly ConstraintRegistry Registry = new();

    /// <summary>The names the built-in router accepts, read from the framework assembly itself.</summary>
    static IReadOnlyList<string> BuiltInNames()
    {
        var options = Components.GetType("Microsoft.AspNetCore.Routing.RouteOptions", throwOnError: true)!;
        var map = (IDictionary<string, Type>)options.GetMethod("GetDefaultConstraintMap", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, null)!;

        // RouteTableFactory registers regex itself (outside the browser, or with the support switch on); the map only ever
        // holds the browser's error stub for it.
        return [.. map.Keys.Where(k => k != "regex").Order(StringComparer.Ordinal), "regex"];
    }

    /// <summary>The built-in's <c>Match</c> for <paramref name="specification"/>, resolved exactly as the built-in router resolves an inline constraint.</summary>
    static Func<string, bool> BuiltIn(string specification)
    {
        var optionsType = Components.GetType("Microsoft.AspNetCore.Routing.RouteOptions", throwOnError: true)!;
        var resolverType = Components.GetType("Microsoft.AspNetCore.Routing.DefaultInlineConstraintResolver", throwOnError: true)!;
        var options = typeof(Options).GetMethod(nameof(Options.Create))!.MakeGenericMethod(optionsType).Invoke(null, [Activator.CreateInstance(optionsType)!])!;

        // The map's regex entry is the browser error stub; the factory adds the real one the same way.
        optionsType.GetMethod("SetParameterPolicy", [typeof(string), typeof(Type)])!.Invoke(
            optionsType.GetProperty("Value")?.GetValue(options) ?? options.GetType().GetProperty("Value")!.GetValue(options),
            ["regex", Components.GetType("Microsoft.AspNetCore.Routing.Constraints.RegexInlineRouteConstraint", throwOnError: true)!]);

        var resolver = Activator.CreateInstance(resolverType, options, new ServiceCollection().BuildServiceProvider())!;
        var constraint = resolverType.GetMethod("ResolveConstraint")!.Invoke(resolver, [specification])
            ?? throw new InvalidOperationException($"The built-in router does not resolve '{specification}'.");
        // Match(string routeKey, RouteValueDictionary values): the dictionary type is whichever assembly the built-in compiled it from.
        var match = constraint.GetType().GetMethods().Single(m => m.Name == "Match" && m.GetParameters().Length == 2);
        var valuesType = match.GetParameters()[1].ParameterType;

        return value =>
        {
            var values = (IDictionary<string, object?>)Activator.CreateInstance(valuesType)!;
            values["x"] = value;

            return (bool)match.Invoke(constraint, ["x", values])!;
        };
    }

    static Func<string, bool> Ours(string specification)
    {
        Assert.That(Registry.TryCreate(specification, out var constraint, out var error), Is.True, error);

        return value => constraint!.TryMatch(value, out _);
    }

    /// <summary>Every built-in name resolves in our registry (with an argument where the built-in needs one).</summary>
    static readonly Dictionary<string, string> SampleSpecification = new(StringComparer.Ordinal)
    {
        ["length"] = "length(2,4)",
        ["minlength"] = "minlength(2)",
        ["maxlength"] = "maxlength(4)",
        ["min"] = "min(5)",
        ["max"] = "max(5)",
        ["range"] = "range(2,8)",
        ["regex"] = "regex(^[a-z]+$)",
    };

    [Test]
    public void Every_constraint_of_the_built_in_router_is_registered_here()
    {
        var names = BuiltInNames();

        Assert.That(names, Is.EquivalentTo(new[] { "alpha", "bool", "datetime", "decimal", "double", "file", "float", "guid", "int", "length", "long", "max", "maxlength", "min", "minlength", "nonfile", "range", "regex" }),
            "the built-in list changed; extend the registry and this test");

        var missing = names.Where(name => !Registry.TryCreate(SampleSpecification.GetValueOrDefault(name, name), out _, out _)).ToList();

        Assert.That(missing, Is.Empty, "built-in constraints we lack: " + string.Join(", ", missing));
    }

    /// <summary>
    /// The specifications fed to both routers, with the value strings. The corpus mixes numbers (signs, thousands
    /// separators, exponents, overflow, Unicode digits), booleans, guids, dates, letters (ASCII and not), file-name shapes
    /// (extensions, trailing dots, directories, dot-files) and whitespace.
    /// </summary>
    static readonly string[] Specifications =
    [
        "int", "long", "bool", "decimal", "double", "float", "guid", "datetime", "alpha",
        "min(5)", "max(5)", "range(2,8)", "length(3)", "length(2,4)", "minlength(2)", "maxlength(4)",
        "file", "nonfile", "regex(^[a-z0-9-]+$)",
    ];

    static readonly string[] Corpus =
    [
        "", " ", "0", "1", "-1", "+5", "42", "007", "4.2", "1.0", "1e3", "1E-2", "1,000", "1,000.5", "1_000", "0x10", "9999999999", "99999999999999999999",
        "NaN", "Infinity", "٣", " 5", "5 ", "abc", "ABC", "aBc", "é", "a-b", "ab12", "my-slug-2", "true", "False", "yes", "TRUE",
        "123e4567-e89b-12d3-a456-426614174000", "123E4567E89B12D3A456426614174000", "not-a-guid",
        "2024-05-01", "2024-05-01T13:30:15", "2024-05-01T13:30:15Z", "05/01/2024", "nope",
        "file.txt", "file.", "file..", ".htaccess", "dir/file.txt", "dir.d/file", "dir/.", "a/b", "a.b.c", ".", "..", "/", "readme",
    ];

    /// <summary>
    /// Where the two deliberately differ. The built-in <c>alpha</c> is the regex <c>^[A-Za-z]*$</c>, so it accepts an empty
    /// value that no segment ever is; its <c>double</c> and <c>float</c> accept thousands separators that its own value
    /// binder (<c>UrlValueConstraint</c>, <c>NumberStyles.Number</c>) then rejects with an exception, so the router would
    /// match and fail to render. This registry keeps a value's constraint and its conversion consistent.
    /// </summary>
    static readonly HashSet<(string Specification, string Value)> KnownDifferences =
    [
        ("alpha", ""),
        ("double", "1,000"),
        ("double", "1,000.5"),
        ("float", "1,000"),
        ("float", "1,000.5"),
    ];

    [TestCaseSource(nameof(Specifications))]
    public void Matches_the_same_values_as_the_built_in_router(string specification)
    {
        var builtIn = BuiltIn(specification);
        var ours = Ours(specification);
        var disagreements = new List<string>();
        var expectedDifferences = KnownDifferences.Where(d => d.Specification == specification).Select(d => d.Value).ToHashSet(StringComparer.Ordinal);

        foreach (var value in Corpus)
        {
            var theirs = builtIn(value);
            var mine = ours(value);

            if (theirs != mine && !expectedDifferences.Remove(value))
            {
                disagreements.Add($"'{value}': built-in {theirs}, ours {mine}");
            }
            else if (theirs == mine && expectedDifferences.Contains(value))
            {
                disagreements.Add($"'{value}': expected a difference but both say {theirs}");
            }
        }

        Assert.That(disagreements, Is.Empty, string.Join(Environment.NewLine, disagreements));
        Assert.That(expectedDifferences, Is.Empty, "a known difference was not exercised: " + string.Join(", ", expectedDifferences));
    }

    [Test]
    public void The_differential_harness_really_reaches_the_built_in()
    {
        Assert.That(BuiltIn("int")("42"), Is.True);
        Assert.That(BuiltIn("int")("abc"), Is.False);
        Assert.That(BuiltIn("nonfile")("file.txt"), Is.False, "the built-in's nonfile rejects a file name");
        Assert.That(() => BuiltIn("nosuch"), Throws.InvalidOperationException);
    }
}
