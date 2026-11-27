using Microsoft.AspNetCore.Components;
using ModRouter.Components;
using ModRouter.Core.Loading;
using ModRouter.Crm.Data;

namespace ModRouter.Crm.Crm.Pages;

/// <summary>The Activity tab: the child route accounts/{id}/activity. Its own loader deferred the feed, and the tab reads it.</summary>
public partial class AccountActivityTab
{
    /// <summary>What the router knows about this level, including the loader's deferred feed.</summary>
    [CascadingParameter]
    ModRouteData Data { get; set; } = default!;

    DeferredValue<IReadOnlyList<Activity>> Feed => Data.Deferred<IReadOnlyList<Activity>>("activity");
}
