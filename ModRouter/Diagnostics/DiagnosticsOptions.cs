namespace ModRouter.Diagnostics;

/// <summary>
/// Options for the router's in-memory navigation history (<c>ModRouterOptions.Diagnostics</c>). Logging and the
/// <c>ActivitySource</c> need no option: they cost nothing until a logger or listener asks for them.
/// </summary>
public sealed class DiagnosticsOptions
{
    /// <summary>The largest <see cref="HistorySize"/> accepted.</summary>
    public const int MaxHistorySize = 1000;

    /// <summary>
    /// How many ended navigations <see cref="RouterDiagnostics"/> keeps (the debug panel's timeline), from 0 to
    /// <see cref="MaxHistorySize"/>. 0, the default, records and allocates nothing.
    /// </summary>
    public int HistorySize { get; set; } = DefaultHistorySize;

    /// <summary>
    /// The initial <see cref="HistorySize"/> of new options: always 0, except in the router test suite's "history" mode
    /// (<c>MODROUTER_TEST_TELEMETRY=history</c>), which sets it so every coordinator records every navigation.
    /// </summary>
    internal static int DefaultHistorySize { get; set; }

    /// <summary>Fails with a message that names the option and its range.</summary>
    internal void Validate()
    {
        if (HistorySize is < 0 or > MaxHistorySize)
        {
            throw new ArgumentException($"ModRouterOptions.Diagnostics.HistorySize must be between 0 and {MaxHistorySize} (it is {HistorySize}). 0 turns the navigation history off.");
        }
    }
}
