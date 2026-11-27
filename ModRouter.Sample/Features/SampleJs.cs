using Microsoft.JSInterop;

namespace ModRouter.Sample.Features;

/// <summary>
/// Reads the <c>?mode=</c> and <c>?scroller=1</c> switches from the page before the host is built, when no
/// <c>NavigationManager</c> exists yet. The sample runs in the browser, where the runtime is in-process; anywhere else, or if the
/// call fails, the fallback applies.
/// </summary>
static class SampleJs
{
    /// <summary>Calls the in-process function <paramref name="identifier"/>, or returns <paramref name="fallback"/>.</summary>
    public static T Read<T>(IJSRuntime js, string identifier, T fallback)
    {
        if (js is not IJSInProcessRuntime inProcess)
        {
            return fallback;
        }

        try
        {
            return inProcess.Invoke<T>(identifier);
        }
        catch (JSException)
        {
            return fallback;
        }
    }
}
