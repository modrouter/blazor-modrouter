using ModRouter.Navigation;

namespace ModRouter.Diagnostics;

/// <summary>What became of a navigation (one hop of a redirect chain is one navigation).</summary>
public enum NavigationRecordOutcome
{
    /// <summary>Not ended yet. The history holds ended navigations only; a trace reports this while it is open.</summary>
    InProgress,

    /// <summary>
    /// A state was committed (see <see cref="NavigationRecord.OutcomeDetail"/>: <c>NotFound</c>, <c>RedirectLoop</c>, or an
    /// error kind at its level such as <c>LoadFailed@1</c>; null for a page).
    /// </summary>
    Committed,

    /// <summary>
    /// A redirect replaced it; the detail is the next hop's absolute path (or <c>external </c> and the target's scheme, host
    /// and path for a full page load), never a query or fragment.
    /// </summary>
    Redirected,

    /// <summary>
    /// A stage (detail <c>canceled by &lt;stage&gt;</c>), the platform or the router's disposal (detail <c>the router was
    /// disposed</c> when it was waiting for its location change or for <c>OnNavigateAsync</c>) canceled it; nothing was committed.
    /// </summary>
    Canceled,

    /// <summary>
    /// A newer navigation (or a location change, or a reload) replaced it before it committed; the detail is <c>released
    /// before its location change</c> or <c>released while OnNavigateAsync ran</c> when it was already prepared.
    /// </summary>
    Superseded,

    /// <summary>A stage threw; the detail is the exception type.</summary>
    Failed,
}

/// <summary>What one pipeline stage (or <c>OnNavigateAsync</c>) did.</summary>
public enum StageOutcome
{
    /// <summary>It let the navigation go on.</summary>
    Continued,

    /// <summary>It asked for a redirect.</summary>
    Redirected,

    /// <summary>It canceled the navigation, or was canceled.</summary>
    Canceled,

    /// <summary>It threw.</summary>
    Failed,
}

/// <summary>One stage of a navigation: its name (the stage type without "Stage", or <c>OnNavigateAsync</c>), how long it ran, what it did.</summary>
public sealed record StageRecord(string Name, TimeSpan Duration, StageOutcome Outcome);

/// <summary>
/// One ended navigation as <see cref="RouterDiagnostics"/> keeps it: an immutable snapshot of strings, numbers and enums
/// only (no route state, component or context is referenced, so a kept record holds nothing alive).
/// </summary>
public sealed record NavigationRecord
{
    readonly IReadOnlyList<StageRecord> _stages = [];

    /// <summary>The coordinator's id of this navigation (each redirect hop has its own).</summary>
    public required long NavigationId { get; init; }

    /// <summary>
    /// The absolute path the navigation went to (<c>/myapp/customers/5</c>): never the scheme, host, query or fragment, which
    /// can carry tokens, codes or e-mail addresses.
    /// </summary>
    public required string Target { get; init; }

    /// <summary>The trigger the navigation started with.</summary>
    public required NavigationTrigger Trigger { get; init; }

    /// <summary>
    /// The trigger it committed as, when it committed: a programmatic navigation recognized at its location change as a
    /// history traversal commits as <see cref="NavigationTrigger.HistoryRestore"/>, and a reload that replaces a navigation
    /// waiting on <c>OnNavigateAsync</c> commits as that navigation. Null when nothing was committed.
    /// </summary>
    public NavigationTrigger? CommittedAs { get; init; }

    /// <summary>When the navigation was requested (the router's <see cref="TimeProvider"/>).</summary>
    public required DateTimeOffset StartedAt { get; init; }

    /// <summary>From the request to the end: including the wait for the location change and <c>OnNavigateAsync</c>.</summary>
    public required TimeSpan Duration { get; init; }

    /// <summary>
    /// How the navigation ended; <see cref="OutcomeDetail"/> says what the outcome refers to. A record in the history
    /// is never <see cref="NavigationRecordOutcome.InProgress"/>.
    /// </summary>
    public required NavigationRecordOutcome Outcome { get; init; }

    /// <summary>What the outcome refers to (see <see cref="NavigationRecordOutcome"/>), or null. A URL in it is a path only.</summary>
    public string? OutcomeDetail { get; init; }

    /// <summary>The absolute path the redirect chain started at, for a redirect hop; null otherwise.</summary>
    public string? RedirectOrigin { get; init; }

    /// <summary>The redirect hops before this one (0 for a navigation that is not a redirect hop).</summary>
    public int RedirectCount { get; init; }

    /// <summary>The stages that ran, in order (a copy: the list given is not kept).</summary>
    public IReadOnlyList<StageRecord> Stages
    {
        get => _stages;
        init => _stages = Array.AsReadOnly([.. value]);
    }
}
