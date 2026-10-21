using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using ModRouter.Generators;

namespace ModRouter.Test;

/// <summary>
/// The typed-route generator reads <c>@page</c> and <c>@attribute [RouteName("...")]</c> from <c>.razor</c> files, which the Razor SDK hands to
/// every generator as additional files. Razor's own generator turns the same directives into attributes, but one generator cannot see
/// another's output, so without this the helpers are missing for any page that declares its route in markup.
/// </summary>
[TestFixture]
public sealed class TypedRouteRazorTests
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

    sealed class RazorFile(string path, string text) : AdditionalText
    {
        public override string Path { get; } = path;

        public override SourceText GetText(CancellationToken cancellationToken = default) => SourceText.From(text, Encoding.UTF8);
    }

    const string Usings = "using Microsoft.AspNetCore.Components;\nusing ModRouter.Discovery;\nnamespace Shop;\n";

    static (string Generated, IReadOnlyList<Diagnostic> Diagnostics, Compilation Output) Run(IEnumerable<(string Path, string Text)> razor, params (string Path, string Text)[] code)
    {
        var compilation = CSharpCompilation.Create(
            "Shop.App",
            code.Select(file => CSharpSyntaxTree.ParseText(Usings + file.Text, new CSharpParseOptions(LanguageVersion.Latest), file.Path)),
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        var driver = CSharpGeneratorDriver.Create([new TypedRouteGenerator().AsSourceGenerator()], additionalTexts: razor.Select(file => (AdditionalText)new RazorFile(file.Path, file.Text)))
            .RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);

        return (string.Concat(driver.GetRunResult().GeneratedTrees.Select(tree => tree.GetText().ToString())), diagnostics, output);
    }

    static void AssertCompiles(Compilation output)
        => Assert.That(output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.ToString()), Is.Empty);

    [Test]
    public void A_page_that_declares_its_route_and_name_in_markup_gets_helpers()
    {
        var (generated, diagnostics, output) = Run([("Pages/Dock.razor", "@page \"/dock/{Id:int}\"\n@attribute [RouteName(\"dock\")]\n<h1>Dock</h1>\n")]);

        Assert.That(diagnostics, Is.Empty);
        Assert.That(generated, Does.Contain("public const string Dock = \"dock\";"));
        Assert.That(generated, Does.Contain("GetDockUrl(this global::ModRouter.Navigation.INavigationService navigation, int id,"));
        AssertCompiles(output);
    }

    [Test]
    public void The_attribute_may_be_written_in_full_and_among_others()
    {
        var (generated, _, _) = Run([("Pages/A.razor", "@page \"/a\"\n@attribute [ModRouter.Discovery.RouteName(\"full-name\"), Obsolete]\n")]);

        Assert.That(generated, Does.Contain("public const string FullName = \"full-name\";"));
    }

    [Test]
    public void Directives_inside_a_markup_comment_are_ignored()
    {
        var (generated, _, _) = Run([("Pages/Old.razor", "@* @page \"/old\"\n   @attribute [RouteName(\"old\")] *@\n<p>nothing</p>\n")]);

        Assert.That(generated, Is.Empty);
    }

    [Test]
    public void A_page_without_a_name_gets_no_helpers()
    {
        var (generated, diagnostics, _) = Run([("Pages/Plain.razor", "@page \"/plain\"\n")]);

        Assert.That(generated, Is.Empty);
        Assert.That(diagnostics, Is.Empty);
    }

    [Test]
    public void A_route_in_markup_and_a_name_in_the_code_behind_are_joined()
    {
        var (generated, diagnostics, output) = Run(
            [("Pages/Shelf.razor", "@page \"/shelf/{Row:int}\"\n<h1>Shelf</h1>\n")],
            ("Pages/Shelf.razor.cs", "[RouteName(\"shelf\")]\npublic partial class Shelf : ComponentBase { }\n"));

        Assert.That(diagnostics, Is.Empty);
        Assert.That(generated, Does.Contain("GetShelfUrl(this global::ModRouter.Navigation.INavigationService navigation, int row,"));
        AssertCompiles(output);
    }

    [Test]
    public void A_name_in_markup_and_a_route_in_the_code_behind_are_joined()
    {
        var (generated, diagnostics, _) = Run(
            [("Pages/Cart.razor", "@attribute [RouteName(\"cart\")]\n<h1>Cart</h1>\n")],
            ("Pages/Cart.razor.cs", "[Route(\"/cart/{Id:guid}\")]\npublic partial class Cart : ComponentBase { }\n"));

        Assert.That(diagnostics, Is.Empty);
        Assert.That(generated, Does.Contain("global::System.Guid id"));
    }

    [Test]
    public void A_page_seen_both_in_markup_and_in_C_sharp_is_generated_once()
    {
        // What a toolchain that exposes Razor's output looks like: the component class carries the attributes the directives produce.
        var (generated, diagnostics, output) = Run(
            [("Pages/Both.razor", "@page \"/both\"\n@attribute [RouteName(\"both\")]\n")],
            ("Generated/Both.razor.g.cs", "[Route(\"/both\")]\n[RouteName(\"both\")]\npublic partial class Both : ComponentBase { }\n"));

        Assert.That(diagnostics, Is.Empty);
        Assert.That(generated.Split(new[] { "public const string Both" }, StringSplitOptions.None), Has.Length.EqualTo(2));
        AssertCompiles(output);
    }

    [Test]
    public void Two_markup_pages_with_names_that_make_the_same_helper_are_reported()
    {
        var (_, diagnostics, _) = Run(
        [
            ("Pages/One.razor", "@page \"/one\"\n@attribute [RouteName(\"customer-list\")]\n"),
            ("Pages/Two.razor", "@page \"/two\"\n@attribute [RouteName(\"customerList\")]\n"),
        ]);

        Assert.That(diagnostics.Select(d => d.Id), Does.Contain("MRG001"));
    }

    [Test]
    public void Markup_pages_are_read_by_an_incremental_step_that_a_second_run_reuses()
    {
        var compilation = CSharpCompilation.Create("Shop.App", [CSharpSyntaxTree.ParseText(Usings)], References, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new TypedRouteGenerator().AsSourceGenerator()],
            additionalTexts: [new RazorFile("Pages/Dock.razor", "@page \"/dock/{Id:int}\"\n@attribute [RouteName(\"dock\")]\n")],
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));

        driver = driver.RunGenerators(compilation);
        driver = driver.RunGenerators(compilation.WithOptions(compilation.Options.WithOptimizationLevel(OptimizationLevel.Release)));

        var outputs = driver.GetRunResult().Results.Single().TrackedSteps[TypedRouteGenerator.RazorStep].SelectMany(step => step.Outputs).ToList();

        Assert.That(outputs, Is.Not.Empty);
        Assert.That(outputs.Select(output => output.Reason), Is.All.AnyOf(IncrementalStepRunReason.Cached, IncrementalStepRunReason.Unchanged));
    }
}
