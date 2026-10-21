using ModRouter.Core.Guards;
using ModRouter.Lab.Services;

namespace ModRouter.Lab.Lessons.L6;

/// <summary>
/// A leave guard is asked before a page that is on screen is left. This one lets the user go when nothing is unsaved and
/// otherwise asks. Answering "stay" cancels the navigation: the page, the URL and the text in the box stay as they are.
/// </summary>
public sealed class UnsavedDraftGuard(Draft draft, ConfirmDialog dialog) : IRouteLeaveGuard
{
    public async ValueTask<RouteLeaveResult> CanDeactivateAsync(RouteLeaveContext context)
    {
        if (!draft.IsDirty)
        {
            return RouteLeaveResult.Allow();
        }

        // Pass the token: if the user clicks somewhere else while the question is open, that newer navigation cancels this one.
        var leave = await dialog.AskAsync("This log has changes that are not saved. Leave anyway?", context.CancellationToken);

        if (leave)
        {
            draft.Discard();
        }

        return leave ? RouteLeaveResult.Allow() : RouteLeaveResult.Cancel();
    }
}
