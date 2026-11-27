using Microsoft.AspNetCore.Components;

namespace ModRouter.Crm.Ui;

/// <summary>A centred message for a page or list with nothing to show.</summary>
public partial class UiEmptyState
{
    /// <summary>What happened, which picks the default icon and colour.</summary>
    [Parameter]
    public EmptyStateKind Kind { get; set; }

    /// <summary>The heading.</summary>
    [Parameter]
    public string Title { get; set; } = string.Empty;

    /// <summary>The sentence under the heading.</summary>
    [Parameter]
    public string? Description { get; set; }

    /// <summary>An icon name that replaces the default one.</summary>
    [Parameter]
    public string? Icon { get; set; }

    /// <summary>Links or buttons shown under the text.</summary>
    [Parameter]
    public RenderFragment? Actions { get; set; }

    string DefaultIcon => Kind == EmptyStateKind.NoResults ? Icons.Outlined.Search : Icons.Outlined.Info;
}
