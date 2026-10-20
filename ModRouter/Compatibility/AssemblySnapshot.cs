using System.Reflection;

namespace ModRouter.Compatibility;

/// <summary>
/// The assemblies the compatibility Router reads once, after the first <c>OnNavigateAsync</c> (where an application adds
/// its "lazy" assemblies): <c>AppAssembly</c> first, then <c>AdditionalAssemblies</c> without duplicates. A later change
/// to either parameter is detected by <see cref="Describe"/> comparisons so the Router can warn once and ignore it.
/// </summary>
internal sealed class AssemblySnapshot
{
    AssemblySnapshot(IReadOnlyList<Assembly> assemblies)
    {
        Assemblies = assemblies;
    }

    /// <summary>The application assembly followed by the additional ones, each once.</summary>
    public IReadOnlyList<Assembly> Assemblies { get; }

    public static AssemblySnapshot Take(Assembly app, IEnumerable<Assembly>? additional)
    {
        ArgumentNullException.ThrowIfNull(app);

        return new AssemblySnapshot([app, .. (additional ?? []).Where(a => a != app).Distinct()]);
    }

    /// <summary>True when the parameters, read now, would give the same list.</summary>
    public bool Matches(Assembly app, IEnumerable<Assembly>? additional) => Assemblies.SequenceEqual(Take(app, additional).Assemblies);

    /// <summary>The assembly names, comma-separated, for the warning that reports a change.</summary>
    public string Describe() => string.Join(", ", Assemblies.Select(a => a.GetName().Name));
}
