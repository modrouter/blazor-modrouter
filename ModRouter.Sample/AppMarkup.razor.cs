using Microsoft.AspNetCore.Components;

namespace ModRouter.Sample;

/// <summary>The markup-mode root: the routes the fluent map does not declare come from <c>&lt;Routes&gt;</c>.</summary>
public partial class AppMarkup
{
    bool _lateEntry;
    string _aboutPath = "/about";

    /// <summary>?mode=markup-dup: gives the about entry the startup route's name "home", so the markup fails to compile.</summary>
    [Parameter]
    public bool Duplicate { get; set; }

    // Both buttons break the "static content" rule on purpose: the first renders a new entry after the freeze (it throws
    // and the renderer logs it), the second changes a registered entry's Path (ignored, with a debug log).
    void AddLateEntry() => _lateEntry = true;

    void MoveAboutEntry() => _aboutPath = "/about-moved";
}
