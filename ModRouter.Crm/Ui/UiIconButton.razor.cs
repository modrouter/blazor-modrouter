using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace ModRouter.Crm.Ui;

/// <summary>A round button that holds one icon. Give it a title and an aria-label: they go on the button.</summary>
public partial class UiIconButton
{
    /// <summary>The icon name, one of the constants in <see cref="Icons.Outlined"/>.</summary>
    [Parameter]
    public string? Icon { get; set; }

    /// <summary>Called on a click.</summary>
    [Parameter]
    public EventCallback<MouseEventArgs> OnClick { get; set; }

    /// <summary>Any other attribute goes on the button.</summary>
    [Parameter(CaptureUnmatchedValues = true)]
    public Dictionary<string, object>? Attributes { get; set; }
}
