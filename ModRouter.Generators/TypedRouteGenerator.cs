using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ModRouter.Generators;

/// <summary>
/// Generates compile-checked route helpers for the pages that carry both <c>[Route]</c> (what <c>@page</c> compiles to) and
/// <c>[RouteName]</c>: a <c>RouteNames</c> class of constants and, on <c>INavigationService</c>, a <c>Get{Name}Url</c> and a
/// <c>NavigateTo{Name}Async</c> method per route whose parameters are the template's, typed from its constraints. The URL is still built by
/// the router, so base paths, encoding and constraint checks are unchanged.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class TypedRouteGenerator : IIncrementalGenerator
{
    const string RouteNameAttribute = "ModRouter.Discovery.RouteNameAttribute";
    const string RouteAttribute = "Microsoft.AspNetCore.Components.RouteAttribute";

    static readonly DiagnosticDescriptor DuplicateMember = new(
        "MRG001",
        "Two routes generate the same helper name",
        "The route names '{0}' and '{1}' both generate the helper name '{2}'; the second one is skipped. Give one of the routes a different [RouteName].",
        "ModRouter",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    static readonly DiagnosticDescriptor BadTemplate = new(
        "MRG002",
        "A route template cannot be turned into a helper",
        "The template '{0}' of the page {1} cannot be turned into a typed helper ({2}), so the route '{3}' gets none",
        "ModRouter",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    /// <summary>The name of the step that holds one entry per page, so a test can see that an unchanged page is not recomputed.</summary>
    internal const string PagesStep = "Pages";

    static readonly string[] ReservedParameterNames = ["navigation", "query", "fragment", "options", "cancellationToken"];

    /// <summary>Registers the pipeline: the pages that declare a route name, collected, then one generated file.</summary>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var pages = context.SyntaxProvider
            .ForAttributeWithMetadataName(RouteNameAttribute, static (node, _) => node is ClassDeclarationSyntax, Collect)
            .Where(static page => page is not null)
            .Select(static (page, _) => page!)
            .WithTrackingName(PagesStep);

        var assembly = context.CompilationProvider.Select(static (compilation, _) => compilation.AssemblyName ?? "App");

        context.RegisterSourceOutput(pages.Collect().Combine(assembly), static (output, input) => Emit(output, input.Left, input.Right));
    }

    static PageRoute? Collect(GeneratorAttributeSyntaxContext context, CancellationToken cancellationToken)
    {
        if (context.TargetSymbol is not INamedTypeSymbol type)
        {
            return null;
        }

        string? name = null;
        string? template = null;

        foreach (var attribute in type.GetAttributes())
        {
            var fullName = attribute.AttributeClass?.ToDisplayString();

            var arguments = attribute.ConstructorArguments;

            if (arguments.Length != 1 || arguments[0].Value is not string text)
            {
                continue;
            }

            if (fullName == RouteNameAttribute)
            {
                name ??= text;
            }
            else if (fullName == RouteAttribute)
            {
                template ??= text;
            }
        }

        if (string.IsNullOrWhiteSpace(name) || template is null)
        {
            return null;
        }

        var location = type.Locations.FirstOrDefault();

        return new PageRoute(name!, template, type.ToDisplayString(), location is null ? null : new LocationInfo(location.SourceTree?.FilePath ?? string.Empty, location.SourceSpan, location.GetLineSpan().Span));
    }

    static void Emit(SourceProductionContext context, ImmutableArray<PageRoute> pages, string assemblyName)
    {
        var routes = new List<(PageRoute Page, string Helper, IReadOnlyList<RouteParameter> Parameters)>();
        var taken = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var page in pages.OrderBy(p => p.Name, StringComparer.Ordinal).ThenBy(p => p.Type, StringComparer.Ordinal))
        {
            if (!TryParseTemplate(page.Template, out var parameters, out var problem))
            {
                context.ReportDiagnostic(Diagnostic.Create(BadTemplate, page.Location?.ToLocation(), page.Template, page.Type, problem, page.Name));

                continue;
            }

            var helper = ToIdentifier(page.Name);

            if (taken.TryGetValue(helper, out var first))
            {
                context.ReportDiagnostic(Diagnostic.Create(DuplicateMember, page.Location?.ToLocation(), first, page.Name, helper));

                continue;
            }

            taken[helper] = page.Name;
            routes.Add((page, helper, parameters));
        }

        if (routes.Count == 0)
        {
            return;
        }

        context.AddSource("ModRouter.TypedRoutes.g.cs", Render(routes, ToNamespace(assemblyName)));
    }

    // ---- template -> parameters ------------------------------------------------------------------------------------

    static bool TryParseTemplate(string template, out IReadOnlyList<RouteParameter> parameters, out string problem)
    {
        var result = new List<RouteParameter>();
        parameters = result;
        problem = string.Empty;
        var index = 0;

        while (index < template.Length)
        {
            var open = template.IndexOf('{', index);

            if (open < 0)
            {
                break;
            }

            var close = template.IndexOf('}', open + 1);

            if (close < 0)
            {
                problem = "an opening brace has no closing one";

                return false;
            }

            var body = template.Substring(open + 1, close - open - 1);
            index = close + 1;

            var catchAll = body.StartsWith("*", StringComparison.Ordinal);
            body = body.TrimStart('*');
            var optional = body.EndsWith("?", StringComparison.Ordinal);
            body = body.TrimEnd('?');

            var parts = body.Split(':');
            var name = parts[0].Trim();

            if (name.Length == 0 || !SyntaxFacts.IsValidIdentifier(name))
            {
                problem = $"'{name}' is not a valid parameter name";

                return false;
            }

            var constraint = parts.Length > 1 ? parts[1].Trim() : string.Empty;
            var parenthesis = constraint.IndexOf('(');
            constraint = (parenthesis >= 0 ? constraint.Substring(0, parenthesis) : constraint).ToLowerInvariant();

            result.Add(new RouteParameter(name, catchAll ? "string" : TypeOf(constraint), optional));
        }

        return true;
    }

    static string TypeOf(string constraint)
        => constraint switch
        {
            "int" => "int",
            "long" => "long",
            "bool" => "bool",
            "double" => "double",
            "float" => "float",
            "decimal" => "decimal",
            "guid" => "global::System.Guid",
            "datetime" => "global::System.DateTime",
            _ => "string",
        };

    // ---- names -----------------------------------------------------------------------------------------------------

    /// <summary>The route name as a PascalCase C# identifier: "customer-orders" becomes "CustomerOrders", "404" becomes "Route404".</summary>
    static string ToIdentifier(string name)
    {
        var builder = new StringBuilder();
        var upper = true;

        foreach (var character in name)
        {
            if (!char.IsLetterOrDigit(character) && character != '_')
            {
                upper = true;

                continue;
            }

            builder.Append(upper ? char.ToUpperInvariant(character) : character);
            upper = false;
        }

        if (builder.Length == 0 || char.IsDigit(builder[0]))
        {
            builder.Insert(0, "Route");
        }

        return builder.ToString();
    }

    static string ToNamespace(string assemblyName)
    {
        var segments = assemblyName.Split('.').Select(segment => SyntaxFacts.IsValidIdentifier(segment) ? segment : "_" + new string(segment.Where(char.IsLetterOrDigit).ToArray()));

        return string.Join(".", segments);
    }

    static string ParameterName(string name)
    {
        var camel = name.Length > 0 ? char.ToLowerInvariant(name[0]) + name.Substring(1) : name;

        if (Array.IndexOf(ReservedParameterNames, camel) >= 0)
        {
            camel += "Value";
        }

        return SyntaxFacts.GetKeywordKind(camel) != SyntaxKind.None || SyntaxFacts.GetContextualKeywordKind(camel) != SyntaxKind.None ? "@" + camel : camel;
    }

    static string Literal(string value) => SymbolDisplay.FormatLiteral(value, quote: true);

    // ---- output ----------------------------------------------------------------------------------------------------

    static string Render(List<(PageRoute Page, string Helper, IReadOnlyList<RouteParameter> Parameters)> routes, string ns)
    {
        var text = new StringBuilder();
        text.AppendLine("// <auto-generated>Generated by ModRouter.Generators from the [Route] and [RouteName] attributes of the pages.</auto-generated>");
        text.AppendLine("#nullable enable");
        text.AppendLine();
        text.AppendLine($"namespace {ns};");
        text.AppendLine();
        text.AppendLine("/// <summary>The names of the routes declared with <c>[RouteName]</c>, for <c>RouteLink Route=\"...\"</c> and the router's named-route methods.</summary>");
        text.AppendLine("public static class RouteNames");
        text.AppendLine("{");

        foreach (var (page, helper, _) in routes)
        {
            text.AppendLine($"    /// <summary>The route '{Escape(page.Name)}' of {Escape(page.Type)}: <c>{Escape(page.Template)}</c>.</summary>");
            text.AppendLine($"    public const string {helper} = {Literal(page.Name)};");
            text.AppendLine();
        }

        text.AppendLine("}");
        text.AppendLine();
        text.AppendLine("/// <summary>Typed URL and navigation helpers for the routes declared with <c>[RouteName]</c>; the router builds the URL.</summary>");
        text.AppendLine("public static class RouteUrls");
        text.AppendLine("{");

        foreach (var (page, helper, parameters) in routes)
        {
            RenderRoute(text, page, helper, parameters);
        }

        text.AppendLine("}");

        return text.ToString();
    }

    static void RenderRoute(StringBuilder text, PageRoute page, string helper, IReadOnlyList<RouteParameter> parameters)
    {
        var required = parameters.Where(p => !p.Optional).ToList();
        var optional = parameters.Where(p => p.Optional).ToList();
        var declared = string.Concat(required.Concat(optional).Select(p => $", {p.Type}{(p.Optional ? "?" : string.Empty)} {ParameterName(p.Name)}{(p.Optional ? " = null" : string.Empty)}"));
        var passed = string.Concat(required.Concat(optional).Select(p => $"{ParameterName(p.Name)}, "));

        text.AppendLine($"    /// <summary>The URL of the route '{Escape(page.Name)}' (<c>{Escape(page.Template)}</c>).</summary>");
        text.AppendLine($"    public static string Get{helper}Url(this global::ModRouter.Navigation.INavigationService navigation{declared}, global::System.Collections.Generic.IReadOnlyDictionary<string, object?>? query = null, string? fragment = null)");
        text.AppendLine($"        => navigation.GetRouteUrl(RouteNames.{helper}, Values{helper}({passed.TrimEnd(',', ' ')}), query, fragment);");
        text.AppendLine();
        text.AppendLine($"    /// <summary>Navigates to the route '{Escape(page.Name)}' (<c>{Escape(page.Template)}</c>) and tells how the navigation ended.</summary>");
        text.AppendLine($"    public static global::System.Threading.Tasks.Task<global::ModRouter.Navigation.NavigationResult> NavigateTo{helper}Async(this global::ModRouter.Navigation.INavigationService navigation{declared}, global::System.Collections.Generic.IReadOnlyDictionary<string, object?>? query = null, string? fragment = null, global::ModRouter.Navigation.ModNavigationOptions? options = null, global::System.Threading.CancellationToken cancellationToken = default)");
        text.AppendLine($"        => navigation.NavigateToRouteAsync(RouteNames.{helper}, Values{helper}({passed.TrimEnd(',', ' ')}), query, fragment, options, cancellationToken: cancellationToken);");
        text.AppendLine();
        text.AppendLine($"    static global::System.Collections.Generic.Dictionary<string, object?> Values{helper}({string.Join(", ", required.Concat(optional).Select(p => $"{p.Type}{(p.Optional ? "?" : string.Empty)} {ParameterName(p.Name)}"))})");
        text.AppendLine("    {");
        text.AppendLine("        var values = new global::System.Collections.Generic.Dictionary<string, object?>();");

        foreach (var parameter in required)
        {
            text.AppendLine($"        values[{Literal(parameter.Name)}] = {ParameterName(parameter.Name)};");
        }

        foreach (var parameter in optional)
        {
            text.AppendLine($"        if ({ParameterName(parameter.Name)} is not null)");
            text.AppendLine("        {");
            text.AppendLine($"            values[{Literal(parameter.Name)}] = {ParameterName(parameter.Name)};");
            text.AppendLine("        }");
        }

        text.AppendLine();
        text.AppendLine("        return values;");
        text.AppendLine("    }");
        text.AppendLine();
    }

    static string Escape(string value) => value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    // ---- model: value-equal, so the pipeline caches --------------------------------------------------------------

    sealed record RouteParameter(string Name, string Type, bool Optional);

    sealed record LocationInfo(string FilePath, Microsoft.CodeAnalysis.Text.TextSpan Span, Microsoft.CodeAnalysis.Text.LinePositionSpan LineSpan)
    {
        public Location ToLocation() => Location.Create(FilePath, Span, LineSpan);
    }

    sealed record PageRoute(string Name, string Template, string Type, LocationInfo? Location);
}
