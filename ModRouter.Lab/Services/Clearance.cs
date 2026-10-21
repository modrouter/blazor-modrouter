namespace ModRouter.Lab.Services;

public enum Level
{
    Visitor,
    Crew,
    Captain,
}

/// <summary>The pretend sign-in of lesson 4: who is standing at the gates.</summary>
public sealed class Clearance
{
    public Level Current { get; private set; } = Level.Visitor;

    public event Action? Changed;

    public void Set(Level level)
    {
        if (level == Current)
        {
            return;
        }

        Current = level;
        Changed?.Invoke();
    }
}
