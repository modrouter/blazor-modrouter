using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace ModRouter.Discovery;

/// <summary>
/// The assemblies whose pages <c>AddModRouter</c> discovers at startup, after the fluent routes
/// (<c>options.Pages.Add(typeof(App).Assembly)</c>); the same as <c>routes.AddPages(assembly, filter)</c>, with the same
/// trimming requirement.
/// </summary>
public sealed class PageAssemblyCollection : IReadOnlyCollection<PageAssembly>
{
    readonly List<PageAssembly> _assemblies = [];

    /// <summary>The number of assemblies added so far.</summary>
    public int Count => _assemblies.Count;

    /// <summary>Adds an assembly; <paramref name="filter"/> (when given) keeps only the component types it returns true for.</summary>
    [RequiresUnreferencedCode(RouteMapPagesExtensions.TrimmingMessage)]
    public PageAssemblyCollection Add(Assembly assembly, Func<Type, bool>? filter = null)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        _assemblies.Add(new PageAssembly(assembly, filter));

        return this;
    }

    /// <summary>Enumerates the entries in the order they were added.</summary>
    public IEnumerator<PageAssembly> GetEnumerator() => _assemblies.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>One entry of <see cref="PageAssemblyCollection"/>.</summary>
public sealed record PageAssembly(Assembly Assembly, Func<Type, bool>? Filter);
