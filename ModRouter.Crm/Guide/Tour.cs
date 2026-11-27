namespace ModRouter.Crm.Guide;

/// <summary>Which chapter the guide is on. The CRM reads it to decide which features to show, so each chapter unlocks a little more.</summary>
public sealed class Tour
{
    public int Current { get; private set; } = 1;

    public int Total => Chapters.All.Count;

    public Chapter Chapter => Chapters.All[Current - 1];

    public event Action? Changed;

    /// <summary>True when the guide has reached the given chapter, so the feature it introduces is on.</summary>
    public bool Has(int chapter) => Current >= chapter;

    public void Go(int chapter)
    {
        var next = Math.Clamp(chapter, 1, Total);

        if (next == Current)
        {
            return;
        }

        Current = next;
        Changed?.Invoke();
    }
}
