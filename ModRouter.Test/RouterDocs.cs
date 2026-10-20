namespace ModRouter.Test;

/// <summary>Finds the router's documentation folder (the one holding <c>README.md</c> and <c>docs/</c>) above the test output directory.</summary>
internal static class RouterDocs
{
    public static string Root
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);

            while (directory is not null)
            {
                var candidate = Path.Combine(directory.FullName, "ModRouter", "README.md");

                if (File.Exists(candidate))
                {
                    return Path.GetDirectoryName(candidate)!;
                }

                directory = directory.Parent;
            }

            throw new FileNotFoundException("ModRouter/README.md was not found above the test output directory.");
        }
    }

    /// <summary>The text of a markdown file relative to the router folder (<c>docs/observability.md</c>, <c>README.md</c>).</summary>
    public static string Read(string relativePath) => File.ReadAllText(Path.Combine(Root, relativePath));

    /// <summary>The lines of a markdown file relative to the router folder, without line terminators.</summary>
    public static string[] ReadLines(string relativePath) => File.ReadAllLines(Path.Combine(Root, relativePath));
}
