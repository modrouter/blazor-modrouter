namespace ModRouter.Lab.Services;

/// <summary>The mission log being edited in lesson 6. It lives in a service, so the leave guard can see whether it has unsaved changes.</summary>
public sealed class Draft
{
    const string Original = "Day 41. The signal from Kepler Station is still faint.";

    string _saved = Original;

    public string Text { get; private set; } = Original;

    public bool IsDirty => !string.Equals(Text, _saved, StringComparison.Ordinal);

    public event Action? Changed;

    public void Edit(string text)
    {
        Text = text;
        Changed?.Invoke();
    }

    public void Save()
    {
        _saved = Text;
        Changed?.Invoke();
    }

    public void Discard()
    {
        Text = _saved;
        Changed?.Invoke();
    }
}
