using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace Kumiko.RichText;

public enum RichFormat
{
    Bold,
    Italic,
    Underline,
    Strike,
    Code
}
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$command")]
[JsonDerivedType(typeof(RichCommand.Select), "select")]
[JsonDerivedType(typeof(RichCommand.Insert), "insert")]
[JsonDerivedType(typeof(RichCommand.Paste), "paste")]
[JsonDerivedType(typeof(RichCommand.Toggle), "toggle")]
[JsonDerivedType(typeof(RichCommand.Link), "link")]
[JsonDerivedType(typeof(RichCommand.Paragraph), "paragraph")]
[JsonDerivedType(typeof(RichCommand.Attachment), "attachment")]
[JsonDerivedType(typeof(RichCommand.Undo), "undo")]
[JsonDerivedType(typeof(RichCommand.Redo), "redo")]
[JsonDerivedType(typeof(RichCommand.CheckTask), "task")]
[JsonDerivedType(typeof(RichCommand.ActivateLink), "activate-link")]
[JsonDerivedType(typeof(RichCommand.RequestLink), "request-link")]
public abstract record RichCommand
{
    public sealed record Select(int Anchor, int Cursor) : RichCommand
    {
        public RichAffinity Affinity
        {
            get; init;
        }
    }
    public sealed record Insert(string Text) : RichCommand;
    public sealed record Paste(RichValue Value) : RichCommand;
    public sealed record Toggle(RichFormat Format) : RichCommand;
    public sealed record Link(string? Url) : RichCommand;
    public sealed record Paragraph(RichParagraphStyle Style) : RichCommand;
    public sealed record Attachment(RichAttachment Value) : RichCommand;
    public sealed record Undo : RichCommand;
    public sealed record Redo : RichCommand;
    public sealed record CheckTask(int ParagraphIndex) : RichCommand;
    public sealed record ActivateLink(string Url) : RichCommand;
    public sealed record RequestLink : RichCommand;
}
public sealed record RichSnapshot(RichValue Value, RichSelection Selection, RichAttributes Typing);

/// <summary>Model-owned selection, typing attributes and bounded rich undo/redo. No native handles or mutable view state.</summary>
public sealed record RichDocument(RichValue Value, RichSelection Selection, RichAttributes Typing,
    ImmutableList<RichSnapshot> Undo, ImmutableList<RichSnapshot> Redo)
{
    public static RichDocument Create(string text) => FromValue(RichValue.Text(text));
    public static RichDocument FromValue(RichValue value) => new(value, new(0, 0), value.AttributesAt(0), [], []);
    public RichDocument Select(int anchor, int cursor) => Select(anchor, cursor, RichAffinity.Downstream);
    public RichDocument Select(int anchor, int cursor, RichAffinity affinity)
    {
        bool reverse = cursor < anchor;
        int a = Value.Snap(anchor, forward: reverse), c = Value.Snap(cursor, forward: !reverse && cursor != anchor);
        var attributes = Value.AttributesAt(a == c && c > 0 && Value.PlainText[c - 1] != '\n' ? Value.Snap(c - 1) : Math.Min(a, c));
        if (a == c)
        {
            var after = Value.AttributesAt(c);
            if (after.Link != attributes.Link || c == Value.Length)
                attributes = attributes with
                {
                    Link = null
                };
            if (after.Code != attributes.Code || c == Value.Length)
                attributes = attributes with
                {
                    Code = false
                };
        }
        return this with
        {
            Selection = new(a, c)
            {
                Affinity = affinity
            },
            Typing = attributes with
            {
                Attachment = null
            }
        };
    }
    RichSnapshot Snapshot() => new(Value, Selection, Typing);
    RichDocument Commit(RichValue value, RichSelection selection, RichAttributes? typing = null) => this with
    {
        Value = value,
        Selection = selection,
        Typing = typing ?? Typing,
        Undo = Undo.Add(Snapshot()).TakeLast(100).ToImmutableList(),
        Redo = []
    };
    static bool On(RichAttributes attributes, RichFormat format) => format switch
    {
        RichFormat.Bold => attributes.Bold,
        RichFormat.Italic => attributes.Italic,
        RichFormat.Underline => attributes.Underline,
        RichFormat.Strike => attributes.Strike,
        RichFormat.Code => attributes.Code,
        _ => throw new ArgumentOutOfRangeException(nameof(format))
    };
    static RichAttributes Set(RichAttributes attributes, RichFormat format, bool value) => format switch
    {
        RichFormat.Bold => attributes with { Bold = value },
        RichFormat.Italic => attributes with { Italic = value },
        RichFormat.Underline => attributes with { Underline = value },
        RichFormat.Strike => attributes with { Strike = value },
        RichFormat.Code => attributes with { Code = value },
        _ => throw new ArgumentOutOfRangeException(nameof(format))
    };
    RichDocument InsertValue(RichValue value)
    {
        int position = Selection.Start + value.Length;
        var edited = Value.Replace(Selection.Start, Selection.End, value);
        position = edited.Snap(position, forward: true);
        return Commit(edited, new(position, position));
    }
    RichDocument History(bool redo)
    {
        var source = redo ? Redo : Undo;
        if (source.Count == 0)
            return this;
        var snapshot = source[^1];
        return this with
        {
            Value = snapshot.Value,
            Selection = snapshot.Selection,
            Typing = snapshot.Typing,
            Undo = redo ? Undo.Add(Snapshot()) : Undo.RemoveAt(Undo.Count - 1),
            Redo = redo ? Redo.RemoveAt(Redo.Count - 1) : Redo.Add(Snapshot())
        };
    }
    public RichDocument Apply(RichCommand command)
    {
        switch (command)
        {
            case RichCommand.Select select:
                return Select(select.Anchor, select.Cursor, select.Affinity);
            case RichCommand.Insert insert:
                return InsertValue(RichValue.Text(insert.Text, Typing, Value.Paragraphs[Value.ParagraphAt(Selection.Start)].Style));
            case RichCommand.Paste paste:
                return InsertValue(paste.Value);
            case RichCommand.Attachment attachment:
                return InsertValue(RichValue.Text("\ufffc", Typing with
                {
                    Attachment = attachment.Value
                }));
            case RichCommand.Toggle toggle:
                bool enable = Selection.Empty ? !On(Typing, toggle.Format) : Value.Slice(Selection.Start, Selection.End)
                    .Paragraphs.SelectMany(p => p.Runs).Where(r => r.Text.Length > 0).Any(r => !On(r.Attributes, toggle.Format));
                return Selection.Empty ? this with
                {
                    Typing = Set(Typing, toggle.Format, enable)
                } :
                    Commit(Value.Format(Selection.Start, Selection.End, a => Set(a, toggle.Format, enable)), Selection, Set(Typing, toggle.Format, enable));
            case RichCommand.Link link:
                if (link.Url is { Length: > 0 } url && !RichExchange.SafeLink(url))
                    throw new ArgumentException("Only http, https and mailto links are allowed.", nameof(command));
                string? target = string.IsNullOrEmpty(link.Url) ? null : link.Url;
                return Selection.Empty ? this with
                {
                    Typing = Typing with
                    {
                        Link = target
                    }
                } :
                    Commit(Value.Format(Selection.Start, Selection.End, a => a with { Link = target }), Selection, Typing with
                    {
                        Link = target
                    });
            case RichCommand.Paragraph paragraph:
                return Commit(Value.FormatParagraphs(Selection.Start, Selection.End, paragraph.Style), Selection);
            case RichCommand.Undo:
                return History(false);
            case RichCommand.Redo:
                return History(true);
            case RichCommand.CheckTask task:
                if (task.ParagraphIndex < 0 || task.ParagraphIndex >= Value.Paragraphs.Count)
                    throw new ArgumentOutOfRangeException(nameof(command));
                var taskParagraph = Value.Paragraphs[task.ParagraphIndex];
                if (taskParagraph.Style.List != RichList.Task)
                    return this;
                int start = Value.ParagraphStarts[task.ParagraphIndex];
                return Commit(Value.FormatParagraphs(start, start + taskParagraph.Text.Length, taskParagraph.Style with
                {
                    Checked = !taskParagraph.Style.Checked
                }), Selection);
            case RichCommand.ActivateLink activate:
                if (!RichExchange.SafeLink(activate.Url))
                    throw new ArgumentException("Unsafe link activation.", nameof(command));
                return this;
            case RichCommand.RequestLink:
                return this;
            default:
                throw new ArgumentOutOfRangeException(nameof(command));
        }
    }
}
