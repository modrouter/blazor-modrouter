using Microsoft.AspNetCore.Components;
using ModRouter.Crm.Ui;

namespace ModRouter.Crm.Crm;

public enum Notice { Lost, Denied, Failed }

/// <summary>A full-area notice for a state the router reports instead of a page: no route, not allowed, or a failure.</summary>
public partial class CrmNotice
{
    /// <summary>Which state this is.</summary>
    [Parameter]
    public Notice Tone { get; set; }

    /// <summary>The heading.</summary>
    [Parameter]
    public string Title { get; set; } = string.Empty;

    /// <summary>The sentence under the heading.</summary>
    [Parameter]
    public string Hint { get; set; } = string.Empty;

    /// <summary>Called by the Try again button.</summary>
    [Parameter]
    public EventCallback Retry { get; set; }

    /// <summary>True when the failed level can be tried again.</summary>
    [Parameter]
    public bool CanRetry { get; set; }

    EmptyStateKind Kind => Tone switch { Notice.Lost => EmptyStateKind.NoResults, Notice.Failed => EmptyStateKind.Error, _ => EmptyStateKind.Empty };

    string? IconFor => Tone == Notice.Denied ? Icons.Outlined.LockClosed : null;
}
