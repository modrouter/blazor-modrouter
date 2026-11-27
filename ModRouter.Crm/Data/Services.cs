namespace ModRouter.Crm.Data;

public enum Role { SignedOut, Rep, Manager, Admin }

/// <summary>Who is signed in. The guards of chapter 5 read it; the header changes it.</summary>
public sealed class Session
{
    public Role Role { get; private set; } = Role.Manager;

    public string Name => Role switch { Role.Admin => "Jordan Ellis", Role.Manager => "Ava Chen", Role.Rep => "Sam Patel", _ => "Guest" };

    public string Label => Role switch { Role.Admin => "Admin", Role.Manager => "Manager", Role.Rep => "Sales rep", _ => "Signed out" };

    public event Action? Changed;

    public void SignInAs(Role role)
    {
        Role = role;
        Changed?.Invoke();
    }
}

/// <summary>The pretend network: how long a loader takes, and whether it fails. Chapter 6 shows these.</summary>
public sealed class Network
{
    public int LatencyMs { get; set; } = 700;

    public bool Offline { get; set; }

    public int Calls { get; private set; }

    public event Action? Changed;

    public async Task WaitAsync(CancellationToken cancellationToken)
    {
        Calls++;
        Changed?.Invoke();
        await Task.Delay(LatencyMs, cancellationToken);

        if (Offline)
        {
            throw new InvalidOperationException("The CRM service did not answer. Check the connection and try again.");
        }
    }

    public void Notify() => Changed?.Invoke();
}

/// <summary>The form the edit page fills in. The leave guard of chapter 7 asks it whether anything is unsaved.</summary>
public sealed class EditDraft
{
    public int AccountId { get; private set; }

    public string Name { get; set; } = string.Empty;

    string _saved = string.Empty;

    public bool IsDirty => AccountId != 0 && Name != _saved;

    public void Begin(int accountId, string name)
    {
        if (AccountId == accountId)
        {
            return;
        }

        AccountId = accountId;
        Name = name;
        _saved = name;
    }

    public void Save() => _saved = Name;

    public void Discard()
    {
        AccountId = 0;
        Name = string.Empty;
        _saved = string.Empty;
    }
}
