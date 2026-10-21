using Microsoft.AspNetCore.Components;
using ModRouter.Components;
using ModRouter.Navigation;

namespace ModRouter.Lab.Lessons.L5;

public readonly record struct Freshness(string Kind, string Note);

public partial class PlanetPage : IDisposable
{
    Timer? _tick;

    // The cascaded ModRouteData carries what the loader returned for this level.
    [CascadingParameter]
    ModRouteData Data { get; set; } = default!;

    [Inject]
    INavigationService Navigation { get; set; } = default!;

    PlanetReading Reading => Data.GetRequired<PlanetReading>();

    TimeSpan Age => DateTime.Now - Reading.LoadedAt;

    // What a visit would do right now, given how old the cached data is.
    Freshness Freshness => Age < PlanetCache.Fresh
        ? new Freshness("fresh", "A visit uses it and does not call the loader.")
        : Age < PlanetCache.Fresh + PlanetCache.Stale
            ? new Freshness("stale", "A visit shows it at once, then refreshes it in the background.")
            : new Freshness("expired", "The next visit calls the loader again.");

    protected override void OnInitialized()
        => _tick = new Timer(_ => _ = InvokeAsync(StateHasChanged), null, TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(500));

    public void Dispose() => _tick?.Dispose();

    // RevalidateAsync runs the loaders of the page on screen again without a guard and without a URL change.
    async Task Revalidate() => await Navigation.RevalidateAsync();
}
