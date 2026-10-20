namespace ModRouter.Components;

/// <summary>
/// The route components currently on screen, by depth: what the outlets last rendered. The leave-guard stage asks the
/// component of a level that is about to be left (or whose parameters are about to change) whether it allows it.
/// </summary>
internal sealed class RouteComponentRegistry
{
    readonly Dictionary<int, (object Owner, object Instance)> _shown = [];

    /// <summary>Records the component <paramref name="owner"/> (an outlet) rendered at <paramref name="depth"/>.</summary>
    public void Set(int depth, object owner, object instance) => _shown[depth] = (owner, instance);

    /// <summary>Forgets the depth, unless a newer outlet has already taken it over.</summary>
    public void Clear(int depth, object owner)
    {
        if (_shown.TryGetValue(depth, out var entry) && ReferenceEquals(entry.Owner, owner))
        {
            _shown.Remove(depth);
        }
    }

    /// <summary>The component shown at <paramref name="depth"/>, or null.</summary>
    public object? Get(int depth) => _shown.TryGetValue(depth, out var entry) ? entry.Instance : null;
}
