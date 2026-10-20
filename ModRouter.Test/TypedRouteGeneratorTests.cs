using System.Reflection;
using Microsoft.AspNetCore.Components;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using ModRouter.Generators;
using ModRouter.Navigation;
using Microsoft.Extensions.DependencyInjection;

namespace ModRouter.Test;

/// <summary>
/// The typed-route generator, run against small compilations: what it generates for pages that carry <c>[Route]</c> and
/// <c>[RouteName]</c>, that the generated code compiles, and that its helpers produce the URLs the router would.
/// </summary>
[TestFixture]
public sealed class TypedRouteGeneratorTests
{
    static readonly IReadOnlyList<MetadataReference> References = BuildReferences();

    static IReadOnlyList<MetadataReference> BuildReferences()
    {
        var paths = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))!.Split(Path.PathSeparator);

        return
        [
            .. paths.Select(path => (MetadataReference)MetadataReference.CreateFromFile(path)),
            MetadataReference.CreateFromFile(typeof(Components.ModRouter).Assembly.Location),
        ];
    }

    static string Source(string pages) => $$"""
        using Microsoft.AspNetCore.Components;
        using ModRouter.Discovery;

        namespace Shop;

        {{pages}}
        """;

    static (Compilation Output, IReadOnlyList<Diagnostic> Diagnostics, string Generated) Run(string pages, string assembly = "Shop.App")
    {
        var compilation = CSharpCompilation.Create(
            assembly,
            [CSharpSyntaxTree.ParseText(Source(pages), new CSharpParseOptions(LanguageVersion.Latest))],
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        var driver = CSharpGeneratorDriver.Create(new TypedRouteGenerator()).RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);
        var generated = string.Concat(driver.GetRunResult().GeneratedTrees.Select(tree => tree.GetText().ToString()));

        return (output, diagnostics, generated);
    }

    static void AssertCompiles(Compilation output)
    {
        var errors = output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.ToString()).ToList();

        Assert.That(errors, Is.Empty);
    }

    const string Customer = """
        [Route("/customers/{id:int}")]
        [RouteName("customer")]
        public class CustomerPage : ComponentBase { }
        """;

    [Test]
    public void A_page_with_a_route_and_a_name_gets_a_constant_and_typed_helpers()
    {
        var (output, diagnostics, generated) = Run(Customer);

        Assert.That(diagnostics, Is.Empty);
        Assert.That(generated, Does.Contain("namespace Shop.App;"));
        Assert.That(generated, Does.Contain("public const string Customer = \"customer\";"));
        Assert.That(generated, Does.Contain("GetCustomerUrl(this global::ModRouter.Navigation.INavigationService navigation, int id,"));
        Assert.That(generated, Does.Contain("NavigateToCustomerAsync(this global::ModRouter.Navigation.INavigationService navigation, int id,"));
        AssertCompiles(output);
    }

    [Test]
    public void Parameters_are_typed_from_their_constraints_and_optional_ones_are_nullable_with_a_default()
    {
        var (output, _, generated) = Run("""
            [Route("/orders/{orderId:guid}/{page:int?}/{from:datetime?}/{ratio:double}/{flag:bool}/{big:long}/{amount:decimal}/{ratio2:float}/{name}/{*rest}")]
            [RouteName("order")]
            public class OrderPage : ComponentBase { }
            """);

        Assert.That(generated, Does.Contain("global::System.Guid orderId"));
        Assert.That(generated, Does.Contain("int? page = null"));
        Assert.That(generated, Does.Contain("global::System.DateTime? @from = null"));
        Assert.That(generated, Does.Contain("double ratio,").And.Contain("bool flag,").And.Contain("long big,").And.Contain("decimal amount,").And.Contain("float ratio2,"));
        Assert.That(generated, Does.Contain("string name,").And.Contain("string rest,"));
        AssertCompiles(output);
    }

    [Test]
    public void A_name_becomes_a_PascalCase_identifier_and_keyword_or_reserved_parameter_names_are_made_safe()
    {
        var (output, _, generated) = Run("""
            [Route("/a/{class}/{query}/{fragment}/{options}")]
            [RouteName("customer-orders")]
            public class OrdersPage : ComponentBase { }

            [Route("/not-found")]
            [RouteName("404")]
            public class MissingPage : ComponentBase { }
            """);

        Assert.That(generated, Does.Contain("public const string CustomerOrders = \"customer-orders\";"));
        Assert.That(generated, Does.Contain("public const string Route404 = \"404\";"));
        Assert.That(generated, Does.Contain("string @class,").And.Contain("string queryValue,").And.Contain("string fragmentValue,").And.Contain("string optionsValue"));
        AssertCompiles(output);
    }

    [Test]
    public void A_page_without_a_name_or_without_a_route_generates_nothing()
    {
        var (_, diagnostics, generated) = Run("""
            [Route("/plain")]
            public class PlainPage : ComponentBase { }

            [RouteName("nameless")]
            public class NoRoutePage : ComponentBase { }
            """);

        Assert.That(generated, Is.Empty);
        Assert.That(diagnostics, Is.Empty);
    }

    [Test]
    public void Two_names_that_generate_the_same_helper_are_reported_and_the_second_is_skipped()
    {
        var (output, diagnostics, generated) = Run("""
            [Route("/a")]
            [RouteName("customer-list")]
            public class AaPage : ComponentBase { }

            [Route("/b")]
            [RouteName("customerList")]
            public class BbPage : ComponentBase { }
            """);

        var error = diagnostics.Single();

        Assert.That(error.Id, Is.EqualTo("MRG001"));
        Assert.That(error.Severity, Is.EqualTo(DiagnosticSeverity.Error));
        Assert.That(error.GetMessage(), Does.Contain("customer-list").And.Contain("customerList").And.Contain("CustomerList"));
        Assert.That(generated, Does.Contain("\"customer-list\"").And.Not.Contain("\"customerList\""));
        AssertCompiles(output);
    }

    [Test]
    public void A_template_that_cannot_be_parsed_is_a_warning_and_gets_no_helper()
    {
        var (_, diagnostics, generated) = Run("""
            [Route("/broken/{id")]
            [RouteName("broken")]
            public class BrokenPage : ComponentBase { }

            [Route("/fine")]
            [RouteName("fine")]
            public class FinePage : ComponentBase { }
            """);

        var warning = diagnostics.Single();

        Assert.That(warning.Id, Is.EqualTo("MRG002"));
        Assert.That(warning.Severity, Is.EqualTo(DiagnosticSeverity.Warning));
        Assert.That(generated, Does.Contain("Fine").And.Not.Contain("Broken"));
    }

    [Test]
    public async Task The_generated_helpers_produce_the_URLs_the_router_builds()
    {
        var (output, _, _) = Run($$"""
            {{Customer}}

            [Route("/search/{term}/{page:int?}")]
            [RouteName("search")]
            public class SearchPage : ComponentBase { }
            """);

        using var stream = new MemoryStream();
        Assert.That(output.Emit(stream).Success, Is.True);
        var assembly = Assembly.Load(stream.ToArray());
        var urls = assembly.GetType("Shop.App.RouteUrls")!;

        var services = new ServiceCollection();
        services.AddModRouter(map => map
            .Route("/customers/{id:int}", typeof(ShowProbe), name: "customer")
            .Route("/search/{term}/{page:int?}", typeof(ShowProbe), name: "search"));
        var adapter = new FakeNavigationAdapter("http://localhost/app/", "http://localhost/app/") { ExecuteNavigateTo = true };
        services.AddSingleton<INavigationPlatformAdapter>(adapter);
        await using var provider = services.BuildServiceProvider();
        var navigation = provider.GetRequiredService<INavigationService>();

        string Call(string method, params object?[] arguments)
            => (string)urls.GetMethod(method)!.Invoke(null, [navigation, .. arguments, null, null])!;

        Assert.That(Call("GetCustomerUrl", 42), Is.EqualTo("/app/customers/42"));
        Assert.That(Call("GetSearchUrl", "a b", null), Is.EqualTo("/app/search/a%20b"));
        Assert.That(Call("GetSearchUrl", "a b", 3), Is.EqualTo("/app/search/a%20b/3"));
        Assert.That(assembly.GetType("Shop.App.RouteNames")!.GetField("Customer")!.GetRawConstantValue(), Is.EqualTo("customer"));
    }

    [Test]
    public void The_generator_is_incremental_and_a_second_run_over_the_same_input_reuses_its_output()
    {
        var compilation = CSharpCompilation.Create(
            "Shop.App",
            [CSharpSyntaxTree.ParseText(Source(Customer), new CSharpParseOptions(LanguageVersion.Latest))],
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new TypedRouteGenerator().AsSourceGenerator()],
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));

        driver = driver.RunGenerators(compilation);
        driver = driver.RunGenerators(compilation.WithOptions(compilation.Options.WithOptimizationLevel(OptimizationLevel.Release)));

        var outputs = driver.GetRunResult().Results.Single().TrackedSteps[TypedRouteGenerator.PagesStep].SelectMany(step => step.Outputs).ToList();

        Assert.That(outputs, Is.Not.Empty);
        Assert.That(outputs.Select(output => output.Reason), Is.All.AnyOf(IncrementalStepRunReason.Cached, IncrementalStepRunReason.Unchanged));
    }
}
