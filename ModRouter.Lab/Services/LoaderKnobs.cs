namespace ModRouter.Lab.Services;

/// <summary>The dials of lesson 5: how slow the loader is and whether it fails, so the router's behavior can be provoked on purpose.</summary>
public sealed class LoaderKnobs
{
    public int CriticalMs { get; private set; } = 700;

    public int MoonsMs { get; private set; } = 1500;

    public bool Fail { get; private set; }

    public event Action? Changed;

    public void SetCritical(int ms) => Update(() => CriticalMs = ms);

    public void SetMoons(int ms) => Update(() => MoonsMs = ms);

    public void SetFail(bool fail) => Update(() => Fail = fail);

    void Update(Action change)
    {
        change();
        Changed?.Invoke();
    }
}
