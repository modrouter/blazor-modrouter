using System.Text;

namespace ModRouter.Test;

/// <summary>A frozen copy of the slice 1-4c path normalizer: the oracle that <c>PathNormalizerEquivalenceTests</c> compares the production one with. Do not optimize it.</summary>
internal static class ReferencePathNormalizer
{
    static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static bool TrySplit(string? path, out string[] segments)
    {
        segments = [];

        if (string.IsNullOrEmpty(path))
        {
            return true;
        }

        var result = new List<string>();
        var start = 0;

        for (var i = 0; i <= path.Length; i++)
        {
            if (i != path.Length && path[i] != '/')
            {
                continue;
            }

            if (i > start)
            {
                if (!TryDecode(path.Substring(start, i - start), out var decoded))
                {
                    return false;
                }

                result.Add(decoded);
            }

            start = i + 1;
        }

        segments = [.. result];

        return true;
    }

    static bool TryDecode(string raw, out string decoded)
    {
        decoded = raw;

        if (raw.IndexOf('%') < 0)
        {
            return true;
        }

        for (var i = 0; i < raw.Length; i++)
        {
            if (raw[i] == '%' && (i + 2 >= raw.Length || !Uri.IsHexDigit(raw[i + 1]) || !Uri.IsHexDigit(raw[i + 2])))
            {
                return false;
            }
        }

        return TryDecodeStrict(raw, out decoded);
    }

    // Builds the segment's bytes (a %XX pair is that byte, any other run of text is its UTF-8 bytes; runs are
    // encoded whole so surrogate pairs stay together) and decodes them strictly, so invalid UTF-8 is malformed.
    static bool TryDecodeStrict(string raw, out string decoded)
    {
        decoded = raw;

        var bytes = new List<byte>(raw.Length);
        var i = 0;

        try
        {
            while (i < raw.Length)
            {
                if (raw[i] == '%')
                {
                    bytes.Add(Convert.ToByte(raw.Substring(i + 1, 2), 16));
                    i += 3;

                    continue;
                }

                var runEnd = raw.IndexOf('%', i);

                if (runEnd < 0)
                {
                    runEnd = raw.Length;
                }

                bytes.AddRange(StrictUtf8.GetBytes(raw.Substring(i, runEnd - i)));
                i = runEnd;
            }

            decoded = StrictUtf8.GetString([.. bytes]);

            return true;
        }
        catch (ArgumentException)
        {
            // DecoderFallbackException and EncoderFallbackException both derive from ArgumentException.
            return false;
        }
    }
}
