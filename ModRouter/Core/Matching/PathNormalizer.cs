using System.Buffers;
using System.Text;

namespace ModRouter.Core.Matching;

/// <summary>
/// Turns an app-relative path into decoded segments. Adds no leading slash requirement, drops empty segments (so
/// repeated and trailing slashes vanish) and percent-decodes each segment once as UTF-8. A segment containing an
/// encoded <c>%2F</c> keeps the slash inside the segment. The caller strips the base path, query and fragment.
/// </summary>
internal static class PathNormalizer
{
    // A segment this short decodes in a stack buffer; a longer one rents from the shared pool.
    const int StackLimit = 128;

    // A character becomes at most three UTF-8 bytes and a %XX escape is one byte, so three bytes per character always fit.
    const int MaxBytesPerChar = 3;

    static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static bool TrySplit(string? path, out string[] segments)
    {
        segments = [];

        if (string.IsNullOrEmpty(path))
        {
            return true;
        }

        var count = CountSegments(path);

        if (count == 0)
        {
            return true;
        }

        var result = new string[count];
        var index = 0;
        var start = 0;

        for (var i = 0; i <= path.Length; i++)
        {
            if (i != path.Length && path[i] != '/')
            {
                continue;
            }

            if (i > start)
            {
                if (!TryDecode(path.AsSpan(start, i - start), out var decoded))
                {
                    return false;
                }

                result[index++] = decoded;
            }

            start = i + 1;
        }

        segments = result;

        return true;
    }

    static int CountSegments(string path)
    {
        var count = 0;
        var inSegment = false;

        foreach (var c in path)
        {
            if (c == '/')
            {
                inSegment = false;
            }
            else if (!inSegment)
            {
                inSegment = true;
                count++;
            }
        }

        return count;
    }

    static bool TryDecode(ReadOnlySpan<char> raw, out string decoded)
    {
        if (raw.IndexOf('%') < 0)
        {
            decoded = new string(raw);

            return true;
        }

        byte[]? rented = null;
        Span<byte> bytes = raw.Length <= StackLimit
            ? stackalloc byte[raw.Length * MaxBytesPerChar]
            : (rented = ArrayPool<byte>.Shared.Rent(raw.Length * MaxBytesPerChar));

        try
        {
            return TryDecodeInto(raw, bytes, out decoded);
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }
    }

    // A %XX pair is that byte; any other run of text is its UTF-8 bytes (a run is encoded whole so surrogate pairs stay
    // together); the bytes are then decoded strictly, so invalid UTF-8 (or a lone surrogate) is malformed.
    static bool TryDecodeInto(ReadOnlySpan<char> raw, Span<byte> bytes, out string decoded)
    {
        decoded = string.Empty;

        var written = 0;
        var i = 0;

        while (i < raw.Length)
        {
            if (raw[i] == '%')
            {
                if (i + 2 >= raw.Length || !Uri.IsHexDigit(raw[i + 1]) || !Uri.IsHexDigit(raw[i + 2]))
                {
                    return false;
                }

                bytes[written++] = (byte)((HexValue(raw[i + 1]) << 4) | HexValue(raw[i + 2]));
                i += 3;

                continue;
            }

            var run = raw[i..];
            var next = run.IndexOf('%');

            if (next >= 0)
            {
                run = run[..next];
            }

            try
            {
                written += StrictUtf8.GetBytes(run, bytes[written..]);
            }
            catch (EncoderFallbackException)
            {
                // A lone surrogate. Only this fallback means "malformed": an undersized buffer is a plain ArgumentException
                // (a sizing bug) and must escape instead of becoming a verdict.
                return false;
            }

            i += run.Length;
        }

        try
        {
            decoded = StrictUtf8.GetString(bytes[..written]);

            return true;
        }
        catch (DecoderFallbackException)
        {
            // Invalid UTF-8 bytes.
            return false;
        }
    }

    static int HexValue(char c) => c <= '9' ? c - '0' : (c | 0x20) - 'a' + 10;
}
