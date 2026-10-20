using ModRouter.Core.Matching;

namespace ModRouter.Test;

/// <summary>
/// <c>Match</c> and <c>Explain</c> share <c>PathNormalizer</c>, so the differential test cannot see a splitter or decoder
/// bug. These tests compare it with a frozen copy of the original implementation: same segments, same malformed verdict.
/// </summary>
[TestFixture]
public sealed class PathNormalizerEquivalenceTests
{
    static readonly string[] Edges =
    [
        "", "/", "//", "///a//b///", "a", "/a/b", "/a/b/", "/%", "/%4", "/%41", "/%4G", "/%zz", "/100%", "/100%25", "/%2541",
        "/%2F", "/a%2Fb/c", "/%C3", "/%C3%28", "/%C3%A9", "/%c3%a9", "/caf%C3%A9-😀", "/%F0%9F%98%80", "/%E2%82%AC",
        "/%FF", "/%ED%A0%80", "/%C0%80", "/\ud83d", "/a\ud83dz", "/😀", "/%20", "/a b", "/é", "/a%", "/a%4", "/a%41",
    ];

    static readonly string[] Pieces =
    [
        "a", "b", "/", "/", "%", "0", "9", "A", "f", "%C3", "%B6", "%E2%82%AC", "%ZZ", "%2F", " ", "é", "😀", "\ud83d",
    ];

    // Display names are escaped: the test adapter silently drops a case whose name holds a lone surrogate.
    static IEnumerable<TestCaseData> EdgeCases => Edges.Select(path => new TestCaseData(path).SetArgDisplayNames(Escape(path)));

    [TestCaseSource(nameof(EdgeCases))]
    public void The_edge_cases_split_as_the_original_does(string path) => AssertSameAsReference(path);

    [Test]
    public void Random_paths_split_as_the_original_does()
    {
        var random = new Random(4242);

        for (var i = 0; i < 20000; i++)
        {
            var length = random.Next(0, 10);
            var path = string.Concat(Enumerable.Range(0, length).Select(_ => Pieces[random.Next(Pieces.Length)]));

            AssertSameAsReference(path);
        }
    }

    [Test]
    public void Long_segments_and_many_segments_split_as_the_original_does()
    {
        AssertSameAsReference("/" + new string('a', 200) + "%C3%A9" + new string('b', 200));
        AssertSameAsReference("/" + string.Concat(Enumerable.Repeat("%41", 300)));
        AssertSameAsReference("/" + string.Concat(Enumerable.Repeat("caf%C3%A9", 60)) + "%FF");
        AssertSameAsReference("/" + string.Join('/', Enumerable.Range(0, 500).Select(i => "s" + i)));
        AssertSameAsReference(string.Concat(Enumerable.Repeat("/", 400)));
    }

    // The decoder uses a stack buffer up to 128 characters and a pooled array above, sized at three bytes per character.
    // Each filler is padded to the exact length and ends in an escape so the segment takes the decoding path.
    static readonly string[] BoundaryFillers = ["a", "\u20ac", "\ud83d\ude00", "%E2%82%AC", "%41"];

    static IEnumerable<TestCaseData> BoundaryCases =>
        from length in new[] { 1, 2, 3, 4, 126, 127, 128, 129, 130, 255, 256, 257 }
        from filler in BoundaryFillers
        from tail in new[] { "%41", "%C3%A9", "%F0%9F%98%80", "%FF", "%C3", "%4", "%", "\ud83d%41", "%41\ude00" }
        where length >= tail.Length
        let segment = Fill(filler, length - tail.Length) + tail
        select new TestCaseData("/" + segment + "/x").SetArgDisplayNames($"{Escape(filler)} to {length} then {Escape(tail)}");

    [TestCaseSource(nameof(BoundaryCases))]
    public void Segments_at_the_stack_and_pool_boundary_split_as_the_original_does(string path) => AssertSameAsReference(path);

    [Test]
    public void The_three_bytes_per_character_worst_case_fits_at_every_length()
    {
        for (var length = 1; length <= 400; length++)
        {
            // Every character but the escape becomes three bytes: the largest buffer a segment of this length can need.
            AssertSameAsReference("/" + new string('\u20ac', length) + "%41");
            AssertSameAsReference("/%41" + new string('\u20ac', length));
            AssertSameAsReference("/" + string.Concat(Enumerable.Repeat("\ud83d\ude00", length)) + "%41");
        }
    }

    [Test]
    public void A_pooled_buffer_reused_with_stale_bytes_decodes_as_the_original_does()
    {
        var first = string.Concat(Enumerable.Repeat("\u20ac%41", 200));
        var second = new string('z', 150) + "%42";

        AssertSameAsReference("/" + first + "/" + second + "/" + first[..300] + "%C3%A9");
        AssertSameAsReference("/" + first + "/" + second);
    }

    // Hex lookalikes (fullwidth and Arabic-Indic digits), escapes next to escapes, overlong and out-of-range UTF-8, and
    // surrogates split by an escape, by a raw character or by a slash.
    static readonly string[] OddPaths =
    [
        "/%\uff10\uff10", "/%\u0663\u0663", "/%aG", "/%Ga", "/%%41", "/%25%", "/%2525", "/%00", "/%EF%BB%BFa", "/%C1%81",
        "/%E0%80%80", "/%F4%90%80%80", "/%F8%88%80%80%80", "/%C3\u00e9", "/%ED%A0%BD\ude00", "/\ud83d%41\ude00", "/\ude00%41",
        "/%F0%9F\ud83d\ude00", "/%E2%82%AC/\ud83d", "/\ud83d/%41",
    ];

    static IEnumerable<TestCaseData> OddCases => OddPaths.Select(path => new TestCaseData(path).SetArgDisplayNames(Escape(path)));

    [TestCaseSource(nameof(OddCases))]
    public void Odd_escapes_and_surrogates_split_as_the_original_does(string path) => AssertSameAsReference(path);

    [Test]
    public void Random_segments_near_the_buffer_boundary_split_as_the_original_does()
    {
        string[] pieces = ["a", "\u20ac", "\ud83d\ude00", "%41", "%E2%82%AC", "%C3", "%", "\ud83d", "%F0%9F%98%80", "\u00e9", "/", "%2F"];
        var random = new Random(128);

        for (var i = 0; i < 5000; i++)
        {
            var target = random.Next(118, 140);
            var builder = new System.Text.StringBuilder("/");

            while (builder.Length < target)
            {
                builder.Append(pieces[random.Next(pieces.Length)]);
            }

            AssertSameAsReference(builder.ToString());
        }
    }

    // PathNormalizer reports only the two fallback exceptions as "malformed". These pin what the framework throws, so the
    // narrowed catches stay right: a buffer-sizing bug is a plain ArgumentException and must escape instead of becoming a verdict.
    [Test]
    public void The_framework_throws_distinct_exceptions_for_a_small_buffer_a_lone_surrogate_and_invalid_bytes()
    {
        var strict = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        var small = new byte[2];
        var large = new byte[16];
        var loneSurrogate = "a" + (char)0xD800 + "b";

        var tooSmall = Assert.Catch(() => strict.GetBytes("abcdef".AsSpan(), small.AsSpan()));
        var lone = Assert.Catch(() => strict.GetBytes(loneSurrogate.AsSpan(), large.AsSpan()));
        var invalid = Assert.Catch(() => strict.GetString(new byte[] { 0xE0, 0xA4 }));

        Assert.That(tooSmall, Is.TypeOf<ArgumentException>(), "an undersized destination is a plain ArgumentException, not a fallback exception");
        Assert.That(tooSmall, Is.Not.InstanceOf<System.Text.EncoderFallbackException>());
        Assert.That(lone, Is.InstanceOf<System.Text.EncoderFallbackException>());
        Assert.That(invalid, Is.InstanceOf<System.Text.DecoderFallbackException>());
    }

    static string Fill(string filler, int length)
    {
        var builder = new System.Text.StringBuilder();

        while (builder.Length + filler.Length <= length)
        {
            builder.Append(filler);
        }

        return builder.Append('a', length - builder.Length).ToString();
    }

    static void AssertSameAsReference(string path)
    {
        var expectedOk = ReferencePathNormalizer.TrySplit(path, out var expected);
        var actualOk = PathNormalizer.TrySplit(path, out var actual);

        Assert.That(actualOk, Is.EqualTo(expectedOk), () => $"malformed verdict for '{Escape(path)}'");

        if (expectedOk)
        {
            Assert.That(actual, Is.EqualTo(expected), () => $"segments for '{Escape(path)}'");
        }
    }

    static string Escape(string path) => string.Concat(path.Select(c => c < ' ' || c > '~' ? $@"\u{(int)c:x4}" : c.ToString()));
}
