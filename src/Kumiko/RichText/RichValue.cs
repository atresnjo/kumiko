using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json.Serialization;

namespace Kumiko.RichText;

public enum RichList
{
    None,
    Bullet,
    Number,
    Task
}
public enum RichAlignment
{
    Start,
    Center,
    End
}
public enum RichAffinity
{
    Downstream,
    Upstream
}
public sealed record RichParagraphStyle(int Heading = 0, RichList List = RichList.None, bool Checked = false,
    int Indent = 0, bool Quote = false, bool Code = false, RichAlignment Alignment = RichAlignment.Start);
public sealed partial record RichAttachment(string Id, string Name, string Mime, ImmutableArray<byte> Data, float Width = 120, float Height = 80);
public sealed record RichAttributes(bool Bold = false, bool Italic = false, bool Underline = false, bool Strike = false,
    bool Code = false, string? Link = null, RichAttachment? Attachment = null);
public sealed record RichRun(string Text, RichAttributes Attributes);
public sealed record RichParagraph(ImmutableList<RichRun> Runs, RichParagraphStyle Style)
{
    [JsonIgnore]
    public string Text => string.Concat(Runs.Select(r => r.Text));
}
public readonly record struct RichSelection(int Anchor, int Cursor)
{
    public RichAffinity Affinity
    {
        get; init;
    }
    public int Start => Math.Min(Anchor, Cursor);
    public int End => Math.Max(Anchor, Cursor);
    public bool Empty => Anchor == Cursor;
}

/// <summary>Immutable attributed paragraphs. All public offsets are UTF-16 grapheme boundaries.</summary>
public sealed partial record RichValue
{
    public ImmutableList<RichParagraph> Paragraphs
    {
        get;
    }
    [JsonIgnore]
    public string PlainText
    {
        get;
    }
    [JsonIgnore]
    public int Length => PlainText.Length;
    [JsonIgnore]
    public ImmutableArray<int> ParagraphStarts
    {
        get;
    }

    [JsonConstructor]
    public RichValue(ImmutableList<RichParagraph> paragraphs)
    {
        ArgumentNullException.ThrowIfNull(paragraphs);
        if (paragraphs.Count is < 1 or > 100000)
            throw new ArgumentException("Expected 1–100,000 paragraphs.", nameof(paragraphs));
        long length = 0;
        var starts = ImmutableArray.CreateBuilder<int>(paragraphs.Count);
        foreach (var paragraph in paragraphs)
        {
            if (paragraph is null || paragraph.Style is null || paragraph.Runs is null)
                throw new ArgumentException("Missing rich paragraph structure.", nameof(paragraphs));
            starts.Add(checked((int)length));
            if (paragraph.Style.Heading is < 0 or > 6 || paragraph.Style.Indent is < 0 or > 32 ||
                !Enum.IsDefined(paragraph.Style.List) || !Enum.IsDefined(paragraph.Style.Alignment))
                throw new ArgumentException("Invalid paragraph style.", nameof(paragraphs));
            foreach (var run in paragraph.Runs)
            {
                if (run is null || run.Text is null || run.Attributes is null)
                    throw new ArgumentException("Missing attributed run structure.", nameof(paragraphs));
                if (run.Text.Contains('\n') || run.Text.Contains('\r') || !ValidUnicode(run.Text))
                    throw new ArgumentException("Runs require valid Unicode without paragraph separators.", nameof(paragraphs));
                if (run.Attributes.Link is { } link && !RichExchange.SafeLink(link))
                    throw new ArgumentException("Unsafe link.", nameof(paragraphs));
                if (run.Attributes.Attachment is { } attachment &&
                    (!run.Text.StartsWith('\ufffc') || StringInfo.ParseCombiningCharacters(run.Text).Length != 1 || !float.IsFinite(attachment.Width) || !float.IsFinite(attachment.Height) ||
                    attachment.Width is <= 0 or > 4096 || attachment.Height is <= 0 or > 4096 || attachment.Data.IsDefault ||
                    string.IsNullOrWhiteSpace(attachment.Id) || attachment.Id.Length > 8192 || attachment.Name is null ||
                    attachment.Name.Length > 8192 || string.IsNullOrWhiteSpace(attachment.Mime) || attachment.Mime.Length > 256 ||
                    attachment.Data.Length > 16 * 1024 * 1024))
                    throw new ArgumentException("Invalid attachment.", nameof(paragraphs));
                length += run.Text.Length;
            }
            var text = paragraph.Text;
            var boundaries = StringInfo.ParseCombiningCharacters(text).ToHashSet();
            int runEnd = 0;
            foreach (var run in paragraph.Runs)
            {
                if (runEnd < text.Length && !boundaries.Contains(runEnd))
                    throw new ArgumentException("Attributed runs must use complete grapheme boundaries.", nameof(paragraphs));
                runEnd += run.Text.Length;
            }
            length++;
            if (length > 16 * 1024 * 1024)
                throw new ArgumentException("Rich text exceeds the content limit.", nameof(paragraphs));
        }
        Paragraphs = paragraphs;
        PlainText = string.Join('\n', paragraphs.Select(p => p.Text));
        ParagraphStarts = starts.MoveToImmutable();
    }
    static bool ValidUnicode(string text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]))
            {
                if (++i >= text.Length || !char.IsLowSurrogate(text[i]))
                    return false;
            }
            else if (char.IsLowSurrogate(text[i]))
                return false;
        }
        return true;
    }
    public static RichValue Text(string text, RichAttributes? attributes = null, RichParagraphStyle? style = null) =>
        new(text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n')
            .Select(line => new RichParagraph(ImmutableList.Create(new RichRun(line, attributes ?? new())), style ?? new())).ToImmutableList());

    public int ParagraphAt(int position)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(position);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(position, Length);
        int low = 0, high = ParagraphStarts.Length - 1;
        while (low < high)
        {
            int mid = (low + high + 1) / 2;
            if (ParagraphStarts[mid] <= position)
                low = mid;
            else
                high = mid - 1;
        }
        return low;
    }
    public int Snap(int position, bool forward = false)
    {
        position = Math.Clamp(position, 0, Length);
        int paragraph = ParagraphAt(position), start = ParagraphStarts[paragraph];
        int local = position - start;
        int previous = 0;
        foreach (int boundary in StringInfo.ParseCombiningCharacters(Paragraphs[paragraph].Text).Append(Paragraphs[paragraph].Text.Length))
        {
            if (boundary == local)
                return position;
            if (boundary > local)
                return start + (forward ? boundary : previous);
            previous = boundary;
        }
        return position;
    }
    public RichAttributes AttributesAt(int position)
    {
        position = Math.Clamp(position, 0, Length);
        int paragraph = ParagraphAt(position), offset = ParagraphStarts[paragraph];
        foreach (var run in Paragraphs[paragraph].Runs)
        {
            offset += run.Text.Length;
            if (position < offset)
                return run.Attributes;
        }
        return Paragraphs[paragraph].Runs.LastOrDefault()?.Attributes ?? new();
    }
    internal static ImmutableList<RichRun> Normalize(IEnumerable<RichRun> runs)
    {
        var result = ImmutableList.CreateBuilder<RichRun>();
        foreach (var run in runs)
        {
            if (run.Text.Length == 0)
                continue;
            if (result.Count > 0 && result[^1].Attributes == run.Attributes && run.Attributes.Attachment is null)
                result[^1] = result[^1] with
                {
                    Text = result[^1].Text + run.Text
                };
            else
                result.Add(run);
        }
        return result.Count > 0 ? result.ToImmutable() : ImmutableList.Create(new RichRun("", new()));
    }
    internal void Range(int start, int end)
    {
        if (start < 0 || end < start || end > Length || Snap(start) != start || Snap(end) != end)
            throw new ArgumentOutOfRangeException(nameof(start), "Range must use complete grapheme boundaries.");
    }
    public RichValue Slice(int start, int end)
    {
        Range(start, end);
        var paragraphs = ImmutableList.CreateBuilder<RichParagraph>();
        int first = ParagraphAt(start), last = ParagraphAt(end);
        for (int p = first; p <= last; p++)
        {
            int offset = ParagraphStarts[p];
            var runs = new List<RichRun>();
            foreach (var run in Paragraphs[p].Runs)
            {
                int a = Math.Max(start - offset, 0), b = Math.Min(end - offset, run.Text.Length);
                if (a < b)
                    runs.Add(run with
                    {
                        Text = run.Text[a..b]
                    });
                offset += run.Text.Length;
            }
            paragraphs.Add(new(Normalize(runs), Paragraphs[p].Style));
        }
        return new(paragraphs.ToImmutable());
    }
    static RichValue Join(RichValue left, RichValue right)
    {
        var last = left.Paragraphs[^1];
        var first = right.Paragraphs[0];
        var merged = new RichParagraph(JoinedRuns(last.Runs.Concat(first.Runs)), last.Text.Length == 0 ? first.Style : last.Style);
        return new(left.Paragraphs.RemoveAt(left.Paragraphs.Count - 1).Add(merged).AddRange(right.Paragraphs.Skip(1)));
    }
    public RichValue Replace(int start, int end, RichValue inserted)
    {
        Range(start, end);
        return Join(Join(Slice(0, start), inserted), Slice(end, Length));
    }
    public RichValue Format(int start, int end, Func<RichAttributes, RichAttributes> format)
    {
        Range(start, end);
        if (start == end)
            return this;
        var paragraphs = Paragraphs.ToBuilder();
        for (int p = ParagraphAt(start); p <= ParagraphAt(Math.Max(start, end - 1)); p++)
        {
            int offset = ParagraphStarts[p];
            var runs = new List<RichRun>();
            foreach (var run in Paragraphs[p].Runs)
            {
                int a = Math.Clamp(start - offset, 0, run.Text.Length), b = Math.Clamp(end - offset, 0, run.Text.Length);
                if (a > 0)
                    runs.Add(run with
                    {
                        Text = run.Text[..a]
                    });
                if (b > a)
                    runs.Add(new(run.Text[a..b], format(run.Attributes)));
                if (b < run.Text.Length)
                    runs.Add(run with
                    {
                        Text = run.Text[b..]
                    });
                offset += run.Text.Length;
            }
            paragraphs[p] = paragraphs[p] with
            {
                Runs = Normalize(runs)
            };
        }
        return new(paragraphs.ToImmutable());
    }
    public RichValue FormatParagraphs(int start, int end, RichParagraphStyle style)
    {
        Range(start, end);
        var paragraphs = Paragraphs.ToBuilder();
        for (int p = ParagraphAt(start); p <= ParagraphAt(Math.Max(start, end - 1)); p++)
            paragraphs[p] = paragraphs[p] with
            {
                Style = style
            };
        return new(paragraphs.ToImmutable());
    }
}
