using System.Text.RegularExpressions;

namespace ModRouter.Test;

/// <summary>The router's README and topic pages link to each other: every relative link and anchor resolves, the README lists every page, and no page keeps a slice label.</summary>
[TestFixture]
public sealed partial class DocumentationLinkTests
{
    [GeneratedRegex(@"\[[^\]]*\]\(([^)\s]+)(?:\s+""[^""]*"")?\)")]
    private static partial Regex LinkPattern();

    [GeneratedRegex(@"^(#{1,6})\s+(.*?)\s*#*\s*$")]
    private static partial Regex HeadingPattern();

    [GeneratedRegex(@"\(slice \d[a-d]?\)", RegexOptions.IgnoreCase)]
    private static partial Regex SliceLabelPattern();

    static IEnumerable<string> Files()
    {
        yield return "README.md";

        foreach (var page in Directory.GetFiles(Path.Combine(RouterDocs.Root, "docs"), "*.md").Order(StringComparer.Ordinal))
        {
            yield return "docs/" + Path.GetFileName(page);
        }
    }

    [TestCaseSource(nameof(Files))]
    public void Every_relative_link_and_anchor_resolves(string file)
    {
        var broken = new List<string>();

        foreach (var link in Links(RouterDocs.Read(file)))
        {
            if (link.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || link.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || link.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var hash = link.IndexOf('#');
            var target = hash < 0 ? link : link[..hash];
            var anchor = hash < 0 ? null : link[(hash + 1)..];
            var path = target.Length == 0
                ? Path.Combine(RouterDocs.Root, file)
                : Path.GetFullPath(Path.Combine(RouterDocs.Root, Path.GetDirectoryName(file) ?? string.Empty, target));

            if (!File.Exists(path))
            {
                broken.Add($"{link} (no such file)");

                continue;
            }

            if (anchor is not null && path.EndsWith(".md", StringComparison.OrdinalIgnoreCase) && !Anchors(File.ReadAllText(path)).Contains(anchor))
            {
                broken.Add($"{link} (no such heading)");
            }
        }

        Assert.That(broken, Is.Empty, $"{file}: {string.Join("; ", broken)}");
    }

    [Test]
    public void The_README_lists_every_topic_page()
    {
        var readme = RouterDocs.Read("README.md");

        foreach (var page in Files().Where(f => f.StartsWith("docs/", StringComparison.Ordinal)))
        {
            Assert.That(readme, Does.Contain($"]({page})"), $"README.md does not link {page}");
        }
    }

    [TestCaseSource(nameof(Files))]
    public void No_heading_carries_a_slice_label(string file)
    {
        var labelled = RouterDocs.Read(file).Split('\n').Where(l => l.StartsWith('#') && SliceLabelPattern().IsMatch(l)).ToList();

        Assert.That(labelled, Is.Empty);
    }

    // Links outside fenced code blocks: a link in an example is not a link.
    static IEnumerable<string> Links(string text)
    {
        var inFence = false;

        foreach (var line in text.Split('\n'))
        {
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                inFence = !inFence;

                continue;
            }

            if (inFence)
            {
                continue;
            }

            foreach (Match match in LinkPattern().Matches(line))
            {
                yield return match.Groups[1].Value;
            }
        }
    }

    // GitHub-style heading slugs: lower case, punctuation removed, spaces to hyphens, a numeric suffix for a repeat.
    static HashSet<string> Anchors(string text)
    {
        var anchors = new HashSet<string>(StringComparer.Ordinal);
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        var inFence = false;

        foreach (var line in text.Split('\n'))
        {
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                inFence = !inFence;

                continue;
            }

            var heading = inFence ? Match.Empty : HeadingPattern().Match(line.TrimEnd('\r'));

            if (!heading.Success)
            {
                continue;
            }

            var slug = new string(heading.Groups[2].Value.ToLowerInvariant().Where(c => char.IsLetterOrDigit(c) || c is ' ' or '-' or '_').ToArray()).Replace(' ', '-');
            var count = seen.GetValueOrDefault(slug);
            seen[slug] = count + 1;
            anchors.Add(count == 0 ? slug : $"{slug}-{count}");
        }

        return anchors;
    }
}
