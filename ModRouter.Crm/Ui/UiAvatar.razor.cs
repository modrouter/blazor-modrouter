using Microsoft.AspNetCore.Components;

namespace ModRouter.Crm.Ui;

/// <summary>Initials on a coloured tile.</summary>
public partial class UiAvatar
{
    /// <summary>The name the initials and the colour come from.</summary>
    [Parameter]
    public string Name { get; set; } = string.Empty;

    /// <summary>How big the avatar is.</summary>
    [Parameter]
    public Size Size { get; set; } = Size.Medium;

    /// <summary>Round or rounded square.</summary>
    [Parameter]
    public Shape Shape { get; set; } = Shape.Circular;

    int Px => Size switch { Size.Small => 24, Size.Large => 40, Size.ExtraLarge => 64, _ => 32 };

    double FontPx => Px * 0.4;

    string Initials
    {
        get
        {
            var words = Name.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            return words.Length switch
            {
                0 => "?",
                1 => words[0][..1].ToUpperInvariant(),
                _ => (words[0][..1] + words[^1][..1]).ToUpperInvariant(),
            };
        }
    }

    // string.GetHashCode changes between runs, so the hue is worked out by hand.
    int Hue => Name.Aggregate(7, (sum, c) => (sum * 31 + c) % 360);
}
