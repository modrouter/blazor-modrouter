using System.Text;
using ModRouter.Core.Compilation;
using ModRouter.Core.Constraints;
using ModRouter.Core.Definitions;

namespace ModRouter.Test;

internal sealed record RandomParam(string Name, string Constraint, bool Optional, bool CatchAll);

internal sealed record RandomRoute(string Name, string Template, IReadOnlyList<RandomParam> Parameters);

/// <summary>Seeded random route tables (flat, and nested with aliases and index children) and paths for the differential and round-trip tests.</summary>
internal static class RandomRouteTables
{
    static readonly string[] Literals = ["a", "b", "new", "files"];
    static readonly string[] Constraints = ["", "", "int", "alpha", "int:min(10)"];
    static readonly string[] PathPool = ["a", "b", "new", "files", "5", "42", "x", "abc", "12", "9"];

    static readonly string[] WideConstraints =
    [
        "", "", "int", "alpha", "int:min(10)", "min(5):int", "long", "bool", "guid", "double", "datetime", "alphanum",
        "length(2,4)", "range(5,50)", "min(1):max(99)", "int:int",
    ];

    static readonly string[] WidePathPool =
    [
        "a", "b", "new", "files", "5", "42", "x", "abc", "12", "9", "A", "FILES", "New", "1.5", "true", "2024-01-31",
        "0f8fad5b-d9cb-469f-a165-70867728950e", "%41", "%20", "caf%C3%A9", "%E2%82%AC", "%2F", "%ZZ", "%C3", "%4", "é", "ab12",
        "-3", "99999999999",
    ];

    /// <summary>Per-table counters that keep route and parameter names unique across every nesting level.</summary>
    sealed class NestedState
    {
        public bool Widened { get; init; }

        public List<string> Lines { get; } = [];

        public int Routes { get; set; }

        public int Parameters { get; set; }
    }

    /// <summary>
    /// Generates routes and compiles them, retrying (up to 25 times) when the random routes happen to be
    /// ambiguous. Returns an empty list and a null table if every attempt failed.
    /// </summary>
    public static IReadOnlyList<RandomRoute> Build(Random random, out CompiledRouteTable? table, bool widened = false)
    {
        for (var attempt = 0; attempt < 25; attempt++)
        {
            var routes = Generate(random, random.Next(3, 13), widened);
            table = TryCompile(routes);

            if (table is not null)
            {
                return routes;
            }
        }

        table = null;

        return [];
    }

    /// <summary>
    /// Generates a nested table (2-5 roots, up to three levels, optional aliases and at most one index child per
    /// parent) and compiles it, retrying up to 25 times when the routes happen to be ambiguous. The description
    /// lists the routes one per line, indented by level, for failure messages. Null if every attempt failed.
    /// </summary>
    public static CompiledRouteTable? BuildNested(Random random, out string description, bool widened = false)
    {
        for (var attempt = 0; attempt < 25; attempt++)
        {
            var state = new NestedState { Widened = widened };

            try
            {
                var table = RouteTestKit.Compile(map => FillNested(map, random, state, depth: 0));
                description = string.Join("\n", state.Lines);

                return table;
            }
            catch (RouteCompilationException)
            {
            }
        }

        description = string.Empty;

        return null;
    }

    public static string RandomPath(Random random, int maxSegments = 4, bool widened = false)
    {
        var pool = widened ? WidePathPool : PathPool;
        var segments = Enumerable.Range(0, random.Next(0, maxSegments + 1)).Select(_ => pool[random.Next(pool.Length)]).ToArray();

        if (!widened)
        {
            return "/" + string.Join('/', segments);
        }

        var builder = new StringBuilder("/");

        for (var i = 0; i < segments.Length; i++)
        {
            builder.Append(segments[i]);

            if (i < segments.Length - 1 || random.Next(4) == 0)
            {
                builder.Append(random.Next(5) == 0 ? "//" : "/");
            }
        }

        return builder.ToString();
    }

    /// <summary>Values that satisfy the route: all required parameters, a prefix of the optionals, maybe the catch-all.</summary>
    public static Dictionary<string, object?> ValuesFor(RandomRoute route, Random random, out Dictionary<string, string> expected)
    {
        var values = new Dictionary<string, object?>();
        expected = new Dictionary<string, string>();

        var optionalCount = route.Parameters.Count(p => p.Optional);
        var optionalsToInclude = optionalCount == 0 ? 0 : random.Next(0, optionalCount + 1);
        var includedOptionals = 0;

        foreach (var parameter in route.Parameters)
        {
            if (parameter.Optional)
            {
                if (includedOptionals >= optionalsToInclude)
                {
                    continue;
                }

                includedOptionals++;
            }

            if (parameter.CatchAll && random.Next(2) == 0)
            {
                // A catch-all matches zero segments and is then present with an empty value (spec: Blazor-compatible).
                expected[parameter.Name] = string.Empty;

                continue;
            }

            var value = ValueFor(parameter, random);
            values[parameter.Name] = value;
            expected[parameter.Name] = ValueFormatter.Format(value);
        }

        return values;
    }

    static List<RandomRoute> Generate(Random random, int count, bool widened)
    {
        var routes = new List<RandomRoute>();

        for (var i = 0; i < count; i++)
        {
            routes.Add(GenerateRoute(random, $"r{i}", widened));
        }

        return routes;
    }

    static RandomRoute GenerateRoute(Random random, string name, bool widened)
    {
        var parts = new List<string>();
        var parameters = new List<RandomParam>();
        var requiredCount = random.Next(0, 4);

        for (var i = 0; i < requiredCount; i++)
        {
            if (random.Next(10) < 6)
            {
                parts.Add(Literals[random.Next(Literals.Length)]);
            }
            else
            {
                AddParameter(parts, parameters, NewParameter(random, parameters.Count, optional: false, widened));
            }
        }

        var tail = random.Next(10);

        if (tail < 2)
        {
            AddParameter(parts, parameters, new RandomParam($"p{parameters.Count}", string.Empty, false, true));
        }
        else if (tail < 4)
        {
            var optionals = random.Next(1, 3);

            for (var i = 0; i < optionals; i++)
            {
                AddParameter(parts, parameters, NewParameter(random, parameters.Count, optional: true, widened));
            }
        }

        return new RandomRoute(name, "/" + string.Join('/', parts), parameters);
    }

    static void AddParameter(List<string> parts, List<RandomParam> parameters, RandomParam parameter)
    {
        parameters.Add(parameter);
        parts.Add(Render(parameter));
    }

    static RandomParam NewParameter(Random random, int index, bool optional, bool widened)
        => new($"p{index}", Pick(random, widened ? WideConstraints : Constraints), optional, false);

    static string Pick(Random random, string[] pool) => pool[random.Next(pool.Length)];

    static string Render(RandomParam parameter)
    {
        if (parameter.CatchAll)
        {
            return "{*" + parameter.Name + "}";
        }

        var constraint = parameter.Constraint.Length == 0 ? string.Empty : ":" + parameter.Constraint;

        return "{" + parameter.Name + constraint + (parameter.Optional ? "?" : string.Empty) + "}";
    }

    static void FillNested(RouteMap map, Random random, NestedState state, int depth)
    {
        var isRoot = depth == 0;
        var count = isRoot ? random.Next(2, 6) : random.Next(1, 4);
        var indent = new string(' ', depth * 2);
        var indexDone = false;

        for (var i = 0; i < count; i++)
        {
            var name = $"r{state.Routes++}";

            if (!isRoot && !indexDone && random.Next(3) == 0)
            {
                indexDone = true;
                state.Lines.Add($"{indent}[index] ({name})");
                map.Index(typeof(CustomerList), name: name);

                continue;
            }

            // A parent with children may not end in an optional or catch-all (RT0007), in its template or an alias.
            var hasChildren = depth < 2 && random.Next(3) == 0;
            var template = NestedTemplate(random, state, isRoot, allowTail: !hasChildren);
            var aliases = random.Next(4) == 0 ? new[] { NestedTemplate(random, state, isRoot, allowTail: !hasChildren) } : [];

            state.Lines.Add($"{indent}{template}{(aliases.Length > 0 ? " alias " + aliases[0] : string.Empty)} ({name}){(hasChildren ? " children:" : string.Empty)}");
            map.Route(
                template,
                typeof(OtherPage),
                name: name,
                aliases: aliases,
                children: hasChildren ? child => FillNested(child, random, state, depth + 1) : null);
        }
    }

    static string NestedTemplate(Random random, NestedState state, bool isRoot, bool allowTail)
    {
        var parts = new List<string>();
        var requiredCount = isRoot ? random.Next(0, 4) : random.Next(1, 3);

        for (var i = 0; i < requiredCount; i++)
        {
            parts.Add(random.Next(10) < 6 ? Literals[random.Next(Literals.Length)] : NestedParameter(random, state, optional: false));
        }

        if (allowTail)
        {
            var tail = random.Next(10);

            if (tail < 2)
            {
                parts.Add("{*p" + state.Parameters++ + "}");
            }
            else if (tail < 4)
            {
                var optionals = random.Next(1, 3);

                for (var i = 0; i < optionals; i++)
                {
                    parts.Add(NestedParameter(random, state, optional: true));
                }
            }
        }

        return (isRoot ? "/" : string.Empty) + string.Join('/', parts);
    }

    static string NestedParameter(Random random, NestedState state, bool optional)
        => Render(new RandomParam($"p{state.Parameters++}", Pick(random, state.Widened ? WideConstraints : Constraints), optional, false));

    static CompiledRouteTable? TryCompile(IReadOnlyList<RandomRoute> routes)
    {
        try
        {
            return RouteTestKit.Compile(map =>
            {
                foreach (var route in routes)
                {
                    map.Route(route.Template, typeof(OtherPage), name: route.Name);
                }
            });
        }
        catch (RouteCompilationException)
        {
            return null;
        }
    }

    static object ValueFor(RandomParam parameter, Random random)
    {
        if (parameter.CatchAll)
        {
            return $"q{random.Next(10)}/q{random.Next(10)}";
        }

        return parameter.Constraint switch
        {
            "int" => random.Next(1, 999),
            "int:min(10)" => random.Next(10, 999),
            "alpha" => new string(Enumerable.Range(0, random.Next(3, 7)).Select(_ => (char)('a' + random.Next(26))).ToArray()),
            _ => $"q{random.Next(100, 999)}z",
        };
    }
}
