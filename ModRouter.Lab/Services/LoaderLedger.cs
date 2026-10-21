namespace ModRouter.Lab.Services;

public sealed record LedgerEntry(int Sequence, string What, DateTime At);

/// <summary>
/// A tally of how often the planet loader really ran. A navigation that is answered from the loader cache adds nothing here,
/// which is how lesson 5 makes caching visible.
/// </summary>
public sealed class LoaderLedger
{
    readonly List<LedgerEntry> _entries = [];

    public IReadOnlyList<LedgerEntry> Entries => _entries;

    public int Runs { get; private set; }

    public event Action? Changed;

    public void Record(string what)
    {
        Runs++;
        _entries.Insert(0, new LedgerEntry(Runs, what, DateTime.Now));

        if (_entries.Count > 8)
        {
            _entries.RemoveAt(_entries.Count - 1);
        }

        Changed?.Invoke();
    }

    public void Clear()
    {
        Runs = 0;
        _entries.Clear();
        Changed?.Invoke();
    }
}
