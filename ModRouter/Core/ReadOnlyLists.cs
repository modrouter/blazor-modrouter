using System.Collections.ObjectModel;

namespace ModRouter.Core;

/// <summary>Hides a builder's list behind a <see cref="ReadOnlyCollection{T}"/> so callers cannot cast it back and mutate it.</summary>
internal static class ReadOnlyLists
{
    /// <summary>Wraps without copying when the source is an <see cref="IList{T}"/>; the owner must not change it afterwards.</summary>
    public static IReadOnlyList<T> Seal<T>(IReadOnlyList<T> source)
        => source is ReadOnlyCollection<T> sealedAlready
            ? sealedAlready
            : new ReadOnlyCollection<T>(source as IList<T> ?? [.. source]);
}
