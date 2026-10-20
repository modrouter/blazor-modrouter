using System.Collections.Concurrent;
using System.Reflection;
using ModRouter.Core.Compilation;
using ModRouter.Core.Definitions;
using ModRouter.Core.Matching;
using ModRouter.Core.Syntax;
using Microsoft.AspNetCore.Components;

namespace ModRouter.Components;

/// <summary>Thrown by <c>AddModRouter</c> when a route's parameters cannot be bound to its component.</summary>
public sealed class RouteBindingValidationException(IReadOnlyList<string> errors)
    : Exception($"Route component binding validation failed with {errors.Count} {(errors.Count == 1 ? "error" : "errors")}:{Environment.NewLine}{string.Join(Environment.NewLine, errors.Select(e => "  " + e))}")
{
    /// <summary>
    /// One entry per binding problem, across every non-alias route of the table rather than just the first one found.
    /// </summary>
    public IReadOnlyList<string> Errors { get; } = errors;
}

/// <summary>
/// Binds the matched route values of one chain level to the <c>[Parameter]</c> properties of its component.
/// Metadata is reflected once per component type. Cascading, query-attributed (<c>[SupplyParameterFromQuery]</c>)
/// and <c>CaptureUnmatchedValues</c> properties are never bound from the path: query-attributed properties are
/// query-only.
/// </summary>
/// <remarks>
/// Blazor keeps a property's old value when a re-render passes no parameter for it, so a reused component would
/// keep a stale route value (<c>/list/3</c> then <c>/list</c> with <c>{page:int?}</c>; <c>/a/5</c> then <c>/b</c>
/// sharing a component with <c>int Id</c>). <see cref="Resets"/> therefore gives the type default of every parameter
/// the previous render supplied and the current match does not; the outlet asks for them only for an instance Blazor
/// will reuse, and only for the names that instance really received. A parameter no URL supplied is never touched, so
/// a property initializer (<c>bool? DeleteFile { get; set; } = true</c>) survives a query-only navigation or a child
/// route opening in the parent's outlet.
/// </remarks>
internal sealed class RouteComponentBinder
{
    readonly ConcurrentDictionary<Type, IReadOnlyDictionary<string, PropertyInfo>> _metadata = new();

    /// <summary>Binds <paramref name="level"/>'s inherited route values to the parameters its component declares; false (with a message) if a value cannot be converted.</summary>
    public bool TryBind(RouteContext level, out IReadOnlyList<KeyValuePair<string, object?>> values, out string? error)
        => TryBind(level, level.Route.Component, out values, out error);

    /// <summary>Binds <paramref name="level"/>'s inherited route values to the parameters <paramref name="component"/> declares (a slot component receives the values of the level that declares it).</summary>
    public bool TryBind(RouteContext level, Type component, out IReadOnlyList<KeyValuePair<string, object?>> values, out string? error)
    {
        var parameters = Parameters(component);
        var bound = new List<KeyValuePair<string, object?>>();

        foreach (var (name, raw) in level.InheritedParameters.Values)
        {
            if (!parameters.TryGetValue(name, out var property))
            {
                continue;
            }

            if (!ValueConverter.TryConvert(raw, property.PropertyType, out var converted))
            {
                values = [];
                error = $"The route value '{name}' = '{raw}' ({raw?.GetType().Name ?? "null"}) cannot be converted to {component.Name}.{property.Name} ({property.PropertyType.Name}).";

                return false;
            }

            bound.Add(new KeyValuePair<string, object?>(property.Name, converted));
        }

        values = bound;
        error = null;

        return true;
    }

    /// <summary>Like <c>TryBind</c> but throws; used at render time, after the bind stage has already succeeded.</summary>
    public IReadOnlyList<KeyValuePair<string, object?>> Bind(RouteContext level)
        => Bind(level, level.Route.Component);

    /// <summary>Like <see cref="TryBind(RouteContext, Type, out IReadOnlyList{KeyValuePair{string, object?}}, out string?)"/> but throws.</summary>
    public IReadOnlyList<KeyValuePair<string, object?>> Bind(RouteContext level, Type component)
        => TryBind(level, component, out var values, out var error)
            ? values
            : throw new InvalidOperationException(error);

    /// <summary>
    /// For a reused instance of <paramref name="component"/>: the type default (null, or zero-initialized for a
    /// non-nullable value type) of every parameter in <paramref name="previouslySupplied"/> (property names, as
    /// <c>Bind</c> returned them) that <paramref name="supplied"/> does not contain.
    /// </summary>
    public IReadOnlyList<KeyValuePair<string, object?>> Resets(Type component, IReadOnlyList<KeyValuePair<string, object?>> supplied, IEnumerable<string> previouslySupplied)
    {
        var parameters = Parameters(component);

        return
        [
            .. previouslySupplied
                .Where(name => !supplied.Any(s => string.Equals(s.Key, name, StringComparison.Ordinal)) && parameters.ContainsKey(name))
                .Select(name => parameters[name])
                .Select(property => new KeyValuePair<string, object?>(property.Name, IsNonNullableValueType(property.PropertyType) ? Activator.CreateInstance(property.PropertyType) : null)),
        ];
    }

    /// <summary>
    /// Startup validation: every route's component must be an <see cref="IComponent"/>, and every route parameter
    /// whose name matches a component parameter must be convertible to its type. Returns the problems found.
    /// </summary>
    public IReadOnlyList<string> Validate(CompiledRouteTable table)
    {
        var errors = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entry in table.Routes.Where(r => !r.IsAlias))
        {
            for (var level = 0; level < entry.Chain.Count; level++)
            {
                var definition = entry.Chain[level];

                if (definition.Component == typeof(RedirectOnly))
                {
                    continue;
                }

                if (!typeof(IComponent).IsAssignableFrom(definition.Component))
                {
                    AddOnce(errors, seen, $"Route {Describe(definition.Name, entry.Template)}: {definition.Component.FullName} is not a component (it does not implement IComponent).");

                    continue;
                }

                var parameters = Parameters(definition.Component);
                var end = entry.LevelEnds[level];

                for (var i = 0; i < end; i++)
                {
                    var segment = entry.Segments[i];

                    if (!segment.IsParameter || !parameters.TryGetValue(segment.Text, out var property))
                    {
                        continue;
                    }

                    var source = segment.Kind == TemplateSegmentKind.CatchAll
                        ? typeof(string)
                        : entry.Constraints[i]?.ValueType ?? typeof(string);

                    if (!ValueConverter.CanEverConvert(source, property.PropertyType))
                    {
                        AddOnce(errors, seen, $"Route {Describe(definition.Name, entry.Template)}: parameter '{segment.Text}' is {source.Name} but {definition.Component.Name}.{property.Name} is {property.PropertyType.Name}, which can never be converted.");
                    }

                    if (segment.Kind == TemplateSegmentKind.OptionalParameter && IsNonNullableValueType(property.PropertyType))
                    {
                        AddOnce(errors, seen, $"Route {Describe(definition.Name, entry.Template)}: parameter '{segment.Text}' is optional but {definition.Component.Name}.{property.Name} is {property.PropertyType.Name}; the property must be nullable so the omitted value can be bound as null.");
                    }
                }
            }
        }

        return errors;
    }

    IReadOnlyDictionary<string, PropertyInfo> Parameters(Type component) => _metadata.GetOrAdd(component, Reflect);

    static bool IsNonNullableValueType(Type type) => type.IsValueType && Nullable.GetUnderlyingType(type) is null;

    static IReadOnlyDictionary<string, PropertyInfo> Reflect(Type component)
    {
        var map = new Dictionary<string, PropertyInfo>(StringComparer.OrdinalIgnoreCase);

        foreach (var property in component.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var parameter = property.GetCustomAttribute<ParameterAttribute>(inherit: true);

            if (parameter is null || parameter.CaptureUnmatchedValues || property.SetMethod is not { IsPublic: true })
            {
                continue;
            }

            if (property.GetCustomAttributes(inherit: true).Any(a => a is CascadingParameterAttributeBase))
            {
                continue;
            }

            map[property.Name] = property;
        }

        return map;
    }

    static string Describe(string? name, string template) => $"{(name is null ? "(unnamed)" : $"'{name}'")} ('{template}')";

    static void AddOnce(List<string> errors, HashSet<string> seen, string message)
    {
        if (seen.Add(message))
        {
            errors.Add(message);
        }
    }
}
