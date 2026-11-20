using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace ChitChat.Platform.Windows;

internal sealed class EditorAnchor
{
    private readonly int _start;
    private readonly int _end;
    private readonly byte[] _key;
    private readonly byte[] _digest;

    internal EditorAnchor(ReadOnlySpan<char> document, int start, int end)
    {
        _start = start;
        _end = end;
        _key = RandomNumberGenerator.GetBytes(32);
        _digest = HMACSHA256.HashData(_key, MemoryMarshal.AsBytes(document));
    }

    internal bool Matches(ReadOnlySpan<char> document, int start, int end)
    {
        if (_start != start || _end != end) return false;
        Span<byte> digest = stackalloc byte[32];
        _ = HMACSHA256.HashData(_key, MemoryMarshal.AsBytes(document), digest);
        return CryptographicOperations.FixedTimeEquals(_digest, digest);
    }
}

internal sealed class InsertedSpanEvidence
{
    private readonly string _inserted;
    private readonly string _normalized;
    private readonly int _prefixLength;
    private readonly int _suffixLength;
    private readonly byte[] _digestKey;
    private readonly byte[] _prefixDigest;
    private readonly byte[] _suffixDigest;

    private InsertedSpanEvidence(ReadOnlySpan<char> document, int start, int end, string inserted)
    {
        _inserted = inserted;
        _normalized = inserted.ReplaceLineEndings("\n");
        _prefixLength = start;
        _suffixLength = document.Length - end;
        _digestKey = RandomNumberGenerator.GetBytes(32);
        _prefixDigest = HMACSHA256.HashData(_digestKey, MemoryMarshal.AsBytes(document[..start]));
        _suffixDigest = HMACSHA256.HashData(_digestKey, MemoryMarshal.AsBytes(document[end..]));
    }

    internal bool IsConfirmed { get; private set; }

    internal static InsertedSpanEvidence? Capture(ReadOnlySpan<char> document, int start, int end, string inserted)
    {
        if (start < 0 || end < start || end > document.Length) return null;

        // A selection that already contains this text cannot prove that insertion occurred.
        if (EqualAfterLineEndings(document[start..end], inserted.ReplaceLineEndings("\n"))) return null;

        return new InsertedSpanEvidence(document, start, end, inserted);
    }

    internal bool Read(ReadOnlySpan<char> document, int start, int end, string inserted, out string? edited)
    {
        edited = null;
        if (!string.Equals(inserted, _inserted, StringComparison.Ordinal)) return false;
        var spanEnd = document.Length - _suffixLength;
        if (spanEnd < _prefixLength) return false;
        if (!Matches(document[.._prefixLength], _prefixDigest) || !Matches(document[spanEnd..], _suffixDigest))
            return false;
        if (start < _prefixLength || end < start || end > spanEnd) return false;

        var span = document[_prefixLength..spanEnd];
        if (!IsConfirmed)
        {
            if (start != spanEnd || end != spanEnd || !EqualAfterLineEndings(span, _normalized)) return true;
            IsConfirmed = true;
        }

        edited = new string(span).ReplaceLineEndings("\n");
        return true;
    }

    private bool Matches(ReadOnlySpan<char> text, byte[] digest)
    {
        Span<byte> current = stackalloc byte[32];
        _ = HMACSHA256.HashData(_digestKey, MemoryMarshal.AsBytes(text), current);
        return CryptographicOperations.FixedTimeEquals(digest, current);
    }

    private static bool EqualAfterLineEndings(ReadOnlySpan<char> text, ReadOnlySpan<char> normalized)
    {
        var index = 0;
        for (var cursor = 0; cursor < text.Length; cursor++)
        {
            var character = text[cursor];
            if (character == '\r')
            {
                if (cursor + 1 < text.Length && text[cursor + 1] == '\n') cursor++;
                character = '\n';
            }
            if (index >= normalized.Length || normalized[index] != character) return false;
            index++;
        }

        return index == normalized.Length;
    }
}
