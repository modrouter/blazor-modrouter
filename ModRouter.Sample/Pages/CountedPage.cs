using Microsoft.AspNetCore.Components;

namespace ModRouter.Sample.Pages;

/// <summary>
/// Base for the sample pages: shows which instance it is and how many times its parameters were set, so the
/// browser check (and a human) can see that parents are retained and children updated in place.
/// </summary>
public abstract class CountedPage : ComponentBase
{
    static readonly Dictionary<string, int> Instances = [];

    protected CountedPage()
    {
        var name = GetType().Name;
        Instances[name] = Instances.GetValueOrDefault(name) + 1;
        Instance = Instances[name];
    }

    protected int Instance { get; }

    protected int ParametersSet { get; private set; }

    protected string Counters => $"instance #{Instance}, parameters set {ParametersSet}x";

    protected override void OnParametersSet() => ParametersSet++;
}
