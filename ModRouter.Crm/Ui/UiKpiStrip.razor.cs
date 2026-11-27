using Microsoft.AspNetCore.Components;

namespace ModRouter.Crm.Ui;

/// <summary>One number on a dashboard.</summary>
public sealed class KpiItem
{
    /// <summary>The figure, already formatted.</summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>What the figure counts.</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>An icon name from <see cref="Icons.Outlined"/>.</summary>
    public string? Icon { get; set; }

    /// <summary>How much the card stands out.</summary>
    public KpiState State { get; set; }
}

/// <summary>A row of KPI cards.</summary>
public partial class UiKpiStrip
{
    /// <summary>The cards.</summary>
    [Parameter]
    public IReadOnlyList<KpiItem> Items { get; set; } = [];

    static string Chip(KpiState state) => state switch
    {
        KpiState.Highlight => "bg-brandBackground/10 text-brandForeground",
        KpiState.Positive => "bg-statusSuccessBackground2 text-statusSuccessForeground",
        _ => "bg-background3 text-foreground2",
    };
}
