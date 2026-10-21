using System.Text.RegularExpressions;

namespace ModRouter.Test;

/// <summary>
/// The guided lessons quote real code from <c>ModRouter.Lab</c>. A code block whose first line names a lab file
/// (<c>// ModRouter.Lab/LabRoutes.cs</c> or <c>@* ModRouter.Lab/Lessons/L1/ShipPage.razor *@</c>) must consist of lines that exist in
/// that file, so a lesson cannot drift away from the code it teaches. A line that is only an ellipsis is skipped.
/// </summary>
[TestFixture]
public sealed partial class LabSnippetTests
{
    [GeneratedRegex(@"ModRouter\.Lab/(?<path>[^\s*]+)")]
    private static partial Regex LabPath();

    static string LabRoot => Path.GetFullPath(Path.Combine(RouterDocs.Root, "..", "ModRouter.Lab"));

    static IEnumerable<string> Lessons()
    {
        var folder = Path.Combine(RouterDocs.Root, "docs", "lessons");

        foreach (var page in Directory.GetFiles(folder, "*.md").Order(StringComparer.Ordinal))
        {
            yield return "docs/lessons/" + Path.GetFileName(page);
        }
    }

    [TestCaseSource(nameof(Lessons))]
    public void The_code_of_a_lesson_exists_in_the_lab(string lesson)
    {
        var problems = new List<string>();
        var quoted = 0;

        foreach (var (file, lines, number) in Snippets(RouterDocs.ReadLines(lesson)))
        {
            quoted++;
            var path = Path.Combine(LabRoot, file);

            if (!File.Exists(path))
            {
                problems.Add($"block {number}: {file} does not exist");

                continue;
            }

            var known = File.ReadAllLines(path).Select(l => l.Trim()).ToHashSet(StringComparer.Ordinal);

            foreach (var line in lines)
            {
                if (!known.Contains(line))
                {
                    problems.Add($"block {number} ({file}): not in the file: {line}");
                }
            }
        }

        Assert.That(problems, Is.Empty, string.Join(Environment.NewLine, problems));
        Assert.That(quoted, Is.GreaterThan(0), $"{lesson} quotes no lab code; mark a block with its file");
    }

    // Yields the code blocks whose first line names a lab file, with their other lines trimmed and without blanks or ellipses.
    static IEnumerable<(string File, List<string> Lines, int Number)> Snippets(string[] lines)
    {
        var block = -1;

        for (var i = 0; i < lines.Length; i++)
        {
            if (!lines[i].TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                continue;
            }

            var end = Array.FindIndex(lines, i + 1, l => l.TrimStart().StartsWith("```", StringComparison.Ordinal));
            block++;

            if (end < 0)
            {
                yield break;
            }

            var body = lines[(i + 1)..end];
            var match = body.Length > 0 ? LabPath().Match(body[0]) : Match.Empty;

            if (match.Success && (body[0].TrimStart().StartsWith("//", StringComparison.Ordinal) || body[0].TrimStart().StartsWith("@*", StringComparison.Ordinal)))
            {
                var code = body.Skip(1).Select(l => l.Trim()).Where(l => l.Length > 0 && l is not ("..." or "// ...")).ToList();

                yield return (match.Groups["path"].Value, code, block + 1);
            }

            i = end;
        }
    }
}
