using ModRouter.Core.Guards;
using ModRouter.Core.Loading;
using ModRouter.Crm.Data;

namespace ModRouter.Crm.Crm.Routing;

// Chapter 5. A guard runs before a page appears and answers one question: may this navigation go on?

/// <summary>Signed-out visitors are sent to the sign-in page, which remembers where they were going.</summary>
public sealed class SignedInGuard(Session session) : IRouteGuard
{
    public ValueTask<RouteGuardResult> CanActivateAsync(RouteGuardContext context)
    {
        if (session.Role != Role.SignedOut)
        {
            return ValueTask.FromResult(RouteGuardResult.Allow());
        }

        var wanted = new Uri(context.To).PathAndQuery;

        return ValueTask.FromResult(RouteGuardResult.RedirectToRoute("sign-in", query: new Dictionary<string, object?> { ["returnUrl"] = wanted }));
    }
}

/// <summary>Only admins pass. A signed-in person without the right gets a "not allowed" page in place of the content.</summary>
public sealed class AdminOnlyGuard(Session session) : IRouteGuard
{
    public ValueTask<RouteGuardResult> CanActivateAsync(RouteGuardContext context)
        => ValueTask.FromResult(session.Role == Role.Admin
            ? RouteGuardResult.Allow()
            : RouteGuardResult.NotAuthorized("Billing is only open to admins. Switch to the Admin role in the top bar to see it."));
}

// Chapter 6. A loader fetches a page's data before the page appears. The old page stays on screen while it runs.

public sealed record AccountView(Account Account, IReadOnlyList<Contact> Contacts, DateTime LoadedAt);

/// <summary>
/// Loads the account for <c>/accounts/{id}</c>. The account and its contacts are plain data, so the navigation waits for them and
/// <c>CacheFor</c> can keep the answer: a result that carries deferred values is never cached.
/// </summary>
public sealed class AccountLoader(Network network) : IRouteLoader
{
    public async ValueTask<RouteLoadResult> LoadAsync(RouteLoadContext context)
    {
        var id = context.Parameters.Get<int>("id");

        // Pass the token on: a newer navigation cancels it and the pretend request stops.
        await network.WaitAsync(context.CancellationToken);

        var account = CrmData.Account(id) ?? throw new KeyNotFoundException($"There is no account {id}. The URL matched a route, but the loader found nothing.");

        return RouteLoadResult.Create(new AccountView(account, CrmData.ContactsOf(id).ToList(), DateTime.Now));
    }
}

/// <summary>
/// Loads the Activity tab. The tab appears first and the timeline fills in when it arrives: the feed is deferred, so this loader's
/// result is not cached and the feed is fetched on every visit.
/// </summary>
public sealed class ActivityLoader(Network network) : IRouteLoader
{
    public ValueTask<RouteLoadResult> LoadAsync(RouteLoadContext context)
    {
        var id = context.Parameters.Get<int>("id");

        return ValueTask.FromResult(RouteLoadResult.Create(id).Defer("activity", LoadActivityAsync(id, network, context.LifetimeToken)));
    }

    static async Task<IReadOnlyList<Activity>> LoadActivityAsync(int id, Network network, CancellationToken lifetime)
    {
        await Task.Delay(Math.Max(300, network.LatencyMs), lifetime);

        return CrmData.ActivityOf(id).ToList();
    }
}

// Chapter 7. A leave guard is asked before the page on screen is left.

/// <summary>Lets the user go when nothing is unsaved, otherwise asks. "Stay" cancels: the page, the URL and the text stay.</summary>
public sealed class UnsavedChangesGuard(EditDraft draft, Ui.DialogService dialogs) : IRouteLeaveGuard
{
    public async ValueTask<RouteLeaveResult> CanDeactivateAsync(RouteLeaveContext context)
    {
        if (!draft.IsDirty)
        {
            return RouteLeaveResult.Allow();
        }

        var leave = await dialogs.ConfirmAsync(
            "Discard your changes?",
            "You edited this account and did not save. If you leave now, the edit is lost.",
            new Ui.ConfirmOptions { ConfirmText = "Discard", CancelText = "Keep editing", Destructive = true });

        if (leave)
        {
            draft.Discard();
        }

        return leave ? RouteLeaveResult.Allow() : RouteLeaveResult.Cancel();
    }
}
