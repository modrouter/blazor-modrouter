using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;
using ModRouter.Discovery;
using ModRouter.Core.Compilation;
using ModRouter.Navigation;

namespace ModRouter.Lab.Lessons.L2;

public sealed record ForgeParameter(string Name, string Rule, bool Optional);

// The route of a page is declared with attributes on its class. [Route] is what @page compiles to; [RouteName] names the route.
// They sit in the code-behind, not in the .razor file, so the typed-route generator can see them.
[Route("/lessons/names")]
[RouteName("l2")]
[RouteTitle("Names instead of URLs")]
public partial class Lesson2Page
{
    // The routes the forge offers. They are looked up by name in the compiled table, so the list can only contain real routes.
    static readonly string[] Candidates = ["ship", "ship-crew", "dock", "beacon", "planet", "gate-vault"];

    static readonly Regex Placeholder = new(@"\{\*{0,2}(?<name>[A-Za-z_]\w*)(?::(?<rule>[^}?]*))?(?<optional>\?)?\}", RegexOptions.Compiled);

    readonly Dictionary<string, string?> _values = [];

    [Inject]
    INavigationService Navigation { get; set; } = default!;

    [Inject]
    RouteTableHolder Holder { get; set; } = default!;

    string Selected { get; set; } = "dock";

    IReadOnlyList<string> Names => Candidates.Where(n => Holder.Table.TryGetRoute(n, out _)).ToList();

    IReadOnlyDictionary<string, string?> Values => _values;

    // The full template of the selected route, such as /lessons/names/dock/{Id:int}.
    string Template => Holder.Table.Routes.FirstOrDefault(r => r.Definition.Name == Selected)?.Template ?? string.Empty;

    IReadOnlyList<ForgeParameter> Parameters
        => Placeholder.Matches(Template)
            .Select(m => new ForgeParameter(m.Groups["name"].Value, m.Groups["rule"].Value, m.Groups["optional"].Success))
            .ToList();

    string? Url { get; set; }

    string? Error { get; set; }

    protected override void OnParametersSet() => Rebuild();

    void OnPick(ChangeEventArgs e)
    {
        Selected = e.Value?.ToString() ?? Selected;
        _values.Clear();
        Rebuild();
    }

    void SetValue(string name, string? value)
    {
        _values[name] = value;
        Rebuild();
    }

    // TryGetRouteUrl builds the URL from the name and the values, or says why it cannot. It never throws and never navigates.
    void Rebuild()
    {
        var values = _values
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
            .ToDictionary(pair => pair.Key, pair => (object?)pair.Value);

        if (Navigation.TryGetRouteUrl(Selected, values, out var url, out var error))
        {
            Url = url;
            Error = null;
        }
        else
        {
            Url = null;
            Error = error.Message;
        }
    }

    void Launch()
    {
        var values = _values
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
            .ToDictionary(pair => pair.Key, pair => (object?)pair.Value);

        Navigation.NavigateToRoute(Selected, values);
    }

    // The same destination through a method that the compiler checks. NavigateToDockAsync is generated from the
    // [Route] and [RouteName] attributes of L2Dock; passing a string for the id would not compile.
    async Task LaunchTyped() => await Navigation.NavigateToDockAsync(4);
}
