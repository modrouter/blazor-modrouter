using Microsoft.AspNetCore.Components;

namespace ModRouter.Crm.Ui;

/// <summary>An icon from <see cref="Icons.Outlined"/>, drawn as inline SVG.</summary>
public partial class UiIcon
{
    /// <summary>The icon name, one of the constants in <see cref="Icons.Outlined"/>.</summary>
    [Parameter]
    public string? Icon { get; set; }

    /// <summary>How big the icon is.</summary>
    [Parameter]
    public Size Size { get; set; } = Size.Medium;

    /// <summary>Extra CSS classes.</summary>
    [Parameter]
    public string? Class { get; set; }

    int Px => Size switch { Size.Small => 16, Size.Large => 24, Size.ExtraLarge => 32, _ => 20 };

    string Data => Icons.PathOf(Icon);
}
