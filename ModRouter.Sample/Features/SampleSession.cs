namespace ModRouter.Sample.Features;

/// <summary>A stand-in for real authentication: the sample's guards read it, the layout and login page change it.</summary>
public sealed class SampleSession
{
    public bool IsSignedIn { get; private set; }

    public bool IsAdmin { get; private set; }

    public event Action? Changed;

    public void SignIn(bool admin)
    {
        IsSignedIn = true;
        IsAdmin = admin;
        Changed?.Invoke();
    }

    public void SignOut()
    {
        IsSignedIn = false;
        IsAdmin = false;
        Changed?.Invoke();
    }

    public override string ToString() => !IsSignedIn ? "signed out" : IsAdmin ? "admin" : "user";
}
