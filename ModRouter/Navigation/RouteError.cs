namespace ModRouter.Navigation;

/// <summary>Why a navigation committed with an error.</summary>
public enum RouteErrorKind
{
    /// <summary>A guard said the user may not see a route level. Chain-level.</summary>
    NotAuthorized,

    /// <summary>A loader threw. Chain-level.</summary>
    LoadFailed,

    /// <summary>A guard threw or produced an unusable result. Chain-level.</summary>
    GuardFailed,

    /// <summary>Redirects looped or exceeded the limit. Router-level.</summary>
    RedirectLoop,

    /// <summary>The pipeline itself failed (a stage threw, or the first navigation was canceled). Router-level.</summary>
    NavigationFailed,

    /// <summary>The markup <c>&lt;Routes&gt;</c> entries could not be compiled or bound, so the router never navigated. Router-level; a retry does nothing.</summary>
    ConfigurationFailed,
}

/// <summary>
/// An error the router committed instead of a page. Chain-level errors keep the matched chain: levels above the
/// failed one render and the failed level's outlet shows the error content. Router-level errors have no usable chain.
/// </summary>
public sealed class RouteError
{
    internal RouteError(RouteErrorKind kind, string message, Exception? exception = null, int? level = null, Type? guardType = null)
    {
        Kind = kind;
        Message = message;
        Exception = exception;
        Level = level;
        GuardType = guardType;
    }

    /// <summary>What failed; decides how much of the page still renders (see <see cref="IsChainLevel"/>).</summary>
    public RouteErrorKind Kind { get; }

    /// <summary>
    /// A readable description of the failure, for display or logging. For a failed guard or loader it includes the
    /// exception's message.
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// The exception behind a failed guard, loader or pipeline stage; null for an error that has none, such as a denial
    /// or a redirect loop.
    /// </summary>
    public Exception? Exception { get; }

    /// <summary>The chain level that failed; null for router-level errors.</summary>
    public int? Level { get; }

    /// <summary>
    /// The guard that produced a <see cref="RouteErrorKind.NotAuthorized"/> or <see cref="RouteErrorKind.GuardFailed"/>
    /// error; null for every other kind. The re-check on authentication change uses it to tell an authorization guard's
    /// denial from an ordinary guard's.
    /// </summary>
    public Type? GuardType { get; }

    /// <summary>
    /// True for a kind that belongs to one level of the matched chain (not authorized, load failed, guard failed): the
    /// levels above it render and its own outlet shows the error. False for a router-level error, which leaves no
    /// usable chain.
    /// </summary>
    public bool IsChainLevel => Kind is RouteErrorKind.NotAuthorized or RouteErrorKind.LoadFailed or RouteErrorKind.GuardFailed;

    internal Func<Task>? RetryAction { get; set; }

    /// <summary>
    /// Runs the current URL through guards and loaders again, reloading the failed level (the others are reused).
    /// Nothing happens when the router was disposed or no navigation has committed.
    /// </summary>
    public Task Retry() => RetryAction?.Invoke() ?? Task.CompletedTask;
}
