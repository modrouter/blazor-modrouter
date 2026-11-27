using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace ModRouter.Crm.Ui;

/// <summary>A button in the CRM's look.</summary>
public partial class UiButton
{
    /// <summary>How the button looks.</summary>
    [Parameter]
    public ButtonStyle ButtonStyle { get; set; } = ButtonStyle.Outline;

    /// <summary>True to stretch to the width of the parent.</summary>
    [Parameter]
    public bool FullWidth { get; set; }

    /// <summary>True to stop clicks and grey the button out.</summary>
    [Parameter]
    public bool Disabled { get; set; }

    /// <summary>The HTML button type.</summary>
    [Parameter]
    public string Type { get; set; } = "button";

    /// <summary>Called on a click.</summary>
    [Parameter]
    public EventCallback<MouseEventArgs> OnClick { get; set; }

    /// <summary>The label.</summary>
    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    /// <summary>Any other attribute, such as autofocus or aria-label, goes on the button.</summary>
    [Parameter(CaptureUnmatchedValues = true)]
    public Dictionary<string, object>? Attributes { get; set; }

    string Classes => "inline-flex items-center justify-center gap-2 rounded-medium px-4 py-1.5 text-sm font-semibold transition-colors duration-100 disabled:cursor-not-allowed disabled:border-transparent disabled:bg-backgroundDisabled disabled:text-foregroundDisabled "
        + (FullWidth ? "w-full " : string.Empty)
        + ButtonStyle switch
        {
            ButtonStyle.Primary => "bg-brandBackground text-foregroundOnBrand hover:bg-brandBackgroundHover active:bg-brandBackgroundPressed",
            ButtonStyle.Danger => "bg-statusRed text-white hover:brightness-90",
            _ => "border border-stroke bg-background text-foreground hover:bg-subtleBackgroundHover",
        };
}
