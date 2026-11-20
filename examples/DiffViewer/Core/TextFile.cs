using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Text;

namespace DiffViewer.Core;

/// <summary>A side of a file pair decoded for display: lines without terminators, tabs expanded.</summary>
public sealed record TextFile(ImmutableArray<string> Lines, bool Binary, bool MissingFinalNewline)
{
    public const int TabWidth = 4;
    /// <summary>Longer lines are cut for display; minified files would otherwise shape megabyte strings.</summary>
    public const int MaxColumns = 4000;
    public static TextFile Empty { get; } = new([], false, false);
    /// <summary>The size of a side that was not read (too large to carry over SSH); 0 when it was read.</summary>
    public long Omitted
    {
        get; init;
    }

    /// <summary>A side too large to read, shown like a binary file with its size.</summary>
    public static TextFile TooLarge(long size) => new([], true, false) { Omitted = size };

    /// <summary>
    /// Decodes bytes as UTF-8 (a BOM is dropped and invalid sequences become U+FFFD). A NUL byte in the first
    /// 8000 bytes marks the file binary, as Git does.
    /// </summary>
    public static TextFile Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty)
            return Empty;
        if (bytes[..Math.Min(bytes.Length, 8000)].Contains((byte)0))
            return new([], true, false);
        if (bytes.StartsWith("\xEF\xBB\xBF"u8))
            bytes = bytes[3..];
        var text = Encoding.UTF8.GetString(bytes);
        return Split(text);
    }

    public static TextFile Split(string text)
    {
        var lines = new List<string>(Math.Max(16, text.Length / 32));
        var span = text.AsSpan();
        int start = 0;
        while (start < span.Length)
        {
            int end = span[start..].IndexOf('\n');
            if (end < 0)
            {
                lines.Add(Clean(span[start..]));
                break;
            }
            var line = span.Slice(start, end);
            if (line.Length > 0 && line[^1] == '\r')
                line = line[..^1];
            lines.Add(Clean(line));
            start += end + 1;
        }
        bool missing = text.Length > 0 && text[^1] != '\n';
        var array = lines.ToArray();
        return new(ImmutableCollectionsMarshal.AsImmutableArray(array), false, missing);
    }

    /// <summary>Expands tabs to the next multiple of <see cref="TabWidth"/> and cuts very long lines.</summary>
    static string Clean(ReadOnlySpan<char> line)
    {
        if (line.IndexOfAny('\t', '\r') < 0 && line.Length <= MaxColumns)
            return line.ToString();
        var builder = new StringBuilder(line.Length + 16);
        foreach (var ch in line)
        {
            if (builder.Length >= MaxColumns)
            {
                builder.Append('…');
                break;
            }
            if (ch == '\t')
                builder.Append(' ', TabWidth - builder.Length % TabWidth);
            else if (ch != '\r')
                builder.Append(ch);
        }
        return builder.ToString();
    }
}
