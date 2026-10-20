using ModRouter.Core.Definitions;

namespace ModRouter.Core.Compilation;

internal static class RouteDescriptions
{
    /// <summary>One-line identification used in every compile error: name, template, component and source.</summary>
    public static string Describe(RouteDefinition definition)
        => $"{(definition.Name is null ? "(unnamed)" : $"'{definition.Name}'")} [template '{definition.Template}', component {definition.Component.Name}, source {definition.Source ?? "unknown"}]";
}
