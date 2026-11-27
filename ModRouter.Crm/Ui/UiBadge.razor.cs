using Microsoft.AspNetCore.Components;

namespace ModRouter.Crm.Ui;

/// <summary>A small coloured label.</summary>
public partial class UiBadge
{
    /// <summary>The colour.</summary>
    [Parameter]
    public Color Color { get; set; } = Color.Info;

    /// <summary>The text.</summary>
    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    string Classes => Color switch
    {
        Color.Success => "border-statusSuccessForeground/30 bg-statusSuccessBackground2 text-statusSuccessForeground2",
        Color.Warning => "border-statusWarningForeground/30 bg-statusWarningBackground2 text-statusWarningForeground2",
        Color.Brand => "border-brandStroke/30 bg-brandBackground/10 text-brandForeground",
        _ => "border-stroke bg-background3 text-foreground2",
    };
}
