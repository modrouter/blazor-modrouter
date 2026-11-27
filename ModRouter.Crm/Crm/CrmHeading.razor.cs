using Microsoft.AspNetCore.Components;

namespace ModRouter.Crm.Crm;

/// <summary>The heading block every CRM page starts with.</summary>
public partial class CrmHeading
{
    /// <summary>The page title.</summary>
    [Parameter]
    public string Title { get; set; } = string.Empty;

    /// <summary>A line of context under the title.</summary>
    [Parameter]
    public string? Subtitle { get; set; }

    /// <summary>Buttons or links on the right.</summary>
    [Parameter]
    public RenderFragment? Actions { get; set; }
}
