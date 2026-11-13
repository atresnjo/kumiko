using Kumiko.RichText;

namespace Kumiko.UI;

public sealed partial class Context
{
    void RichInput(RichDocument document, EditState edit, RichEditorLayout layout, RichEditorOptions options, Action<RichCommand> emit)
    {
        bool primary = Input.Primary, shift = (Input.Modifiers & Modifiers.Shift) != 0;
        void Select(int position, RichAffinity affinity = RichAffinity.Downstream) =>
            emit(new RichCommand.Select(shift ? document.Selection.Anchor : position, position) { Affinity = affinity });
        void SelectHit(RichSelection target) => Select(target.Cursor, target.Affinity);
        void Replace(string text)
        {
            if (!options.ReadOnly)
                emit(new RichCommand.Insert(text));
        }
        if (Input.Composition is { } composition)
        {
            if (Input.CommitComposition)
            {
                Replace(composition);
                edit.Composition = "";
            }
            else
                edit.Composition = options.ReadOnly ? "" : composition;
            Input.Composition = null;
            Input.CommitComposition = false;
            Input.Text = "";
            Input.Key = UI.Key.None;
            return;
        }
        if (edit.Composition.Length > 0)
        {
            if (Input.Key == UI.Key.Escape)
                edit.Composition = "";
            Input.Key = UI.Key.None;
            return;
        }
        var selection = document.Selection;
        int cursor = selection.Cursor;
        var paragraph = document.Value.Paragraphs[document.Value.ParagraphAt(cursor)];
        var style = paragraph.Style;
        bool handled = true;
        switch (Input.Key)
        {
            case UI.Key.A when primary:
                emit(new RichCommand.Select(0, document.Value.Length));
                break;
            case UI.Key.C or UI.Key.X when primary && !shift:
                if (!selection.Empty)
                {
                    CopyRich(document.Value.Slice(selection.Start, selection.End));
                    if (Input.Key == UI.Key.X)
                        Replace("");
                }
                break;
            case UI.Key.V when primary:
                if (!options.ReadOnly)
                {
                    var value = PasteRich(shift);
                    if (shift)
                        Replace(value.PlainText);
                    else
                        emit(new RichCommand.Paste(value));
                }
                break;
            case UI.Key.D7 or UI.Key.D8 when primary && shift:
                if (!options.ReadOnly)
                {
                    var kind = Input.Key == UI.Key.D7 ? RichList.Number : RichList.Bullet;
                    emit(new RichCommand.Paragraph(style with
                    {
                        List = style.List == kind ? RichList.None : kind
                    }));
                }
                break;
            case >= UI.Key.D1 and <= UI.Key.D6 when primary && (Input.Modifiers & Modifiers.Alt) != 0:
                if (!options.ReadOnly)
                    emit(new RichCommand.Paragraph(style with
                    {
                        Heading = Input.Key - UI.Key.D1 + 1,
                        List = RichList.None
                    }));
                break;
            case UI.Key.K when primary:
                if (!options.ReadOnly)
                    emit(new RichCommand.RequestLink());
                break;
            case UI.Key.L when primary && shift:
                if (!options.ReadOnly)
                    emit(new RichCommand.Paragraph(style with
                    {
                        List = style.List == RichList.Task ? RichList.None : RichList.Task
                    }));
                break;
            case UI.Key.Z or UI.Key.Y when primary:
                if (!options.ReadOnly)
                    emit(Input.Key == UI.Key.Y || shift ? new RichCommand.Redo() : new RichCommand.Undo());
                break;
            case UI.Key.B or UI.Key.I or UI.Key.U or UI.Key.E when primary:
                if (!options.ReadOnly)
                    emit(new RichCommand.Toggle(Input.Key switch
                    {
                        UI.Key.B => RichFormat.Bold,
                        UI.Key.I => RichFormat.Italic,
                        UI.Key.U => RichFormat.Underline,
                        _ => RichFormat.Code
                    }));
                break;
            case UI.Key.X when primary && shift:
                if (!options.ReadOnly)
                    emit(new RichCommand.Toggle(RichFormat.Strike));
                break;
            case UI.Key.Left or UI.Key.Right:
                int direction = Input.Key == UI.Key.Left ? -1 : 1;
                if (!shift && !selection.Empty)
                    SelectHit(layout.Collapse(this, selection, direction));
                else if (primary && OperatingSystem.IsMacOS())
                    Select(direction < 0 ? document.Value.ParagraphStarts[document.Value.ParagraphAt(cursor)] :
                        document.Value.ParagraphStarts[document.Value.ParagraphAt(cursor)] + paragraph.Text.Length);
                else
                    SelectHit(layout.Horizontal(this, cursor, selection.Affinity, direction));
                break;
            case UI.Key.Home or UI.Key.End:
                if (primary)
                    Select(Input.Key == UI.Key.Home ? 0 : document.Value.Length);
                else
                    SelectHit(layout.LineEdge(this, cursor, selection.Affinity, Input.Key == UI.Key.End));
                break;
            case UI.Key.Up or UI.Key.Down or UI.Key.PageUp or UI.Key.PageDown:
                if (primary && OperatingSystem.IsMacOS())
                    Select(Input.Key == UI.Key.Up ? 0 : document.Value.Length);
                else
                {
                    var caret = layout.Caret(this, cursor, selection.Affinity);
                    float delta = Input.Key is UI.Key.PageUp or UI.Key.PageDown ? Math.Max(24, options.Height - 30) : caret.Height;
                    float y = caret.Y + (Input.Key is UI.Key.Up or UI.Key.PageUp ? -delta : delta);
                    SelectHit(layout.Hit(this, new(caret.X, Math.Max(0, y + caret.Height / 2))));
                }
                break;
            case UI.Key.Backspace or UI.Key.Delete:
                if (options.ReadOnly)
                    break;
                if (selection.Empty)
                {
                    int start = document.Value.ParagraphStarts[document.Value.ParagraphAt(cursor)];
                    if (Input.Key == UI.Key.Backspace && cursor == start && style != new RichParagraphStyle())
                    {
                        emit(new RichCommand.Paragraph(new()));
                        break;
                    }
                    int other = Input.Key == UI.Key.Backspace ? document.Value.Snap(cursor - 1) : document.Value.Snap(cursor + 1, true);
                    emit(new RichCommand.Select(other, cursor));
                }
                Replace("");
                break;
            case UI.Key.Enter:
                if (options.ReadOnly)
                    break;
                if (paragraph.Text.Length == 0 && style.List != RichList.None)
                    emit(new RichCommand.Paragraph(style with
                    {
                        List = RichList.None,
                        Checked = false
                    }));
                else
                {
                    Replace("\n");
                    if (style.Heading > 0 || style.Checked)
                        emit(new RichCommand.Paragraph(style with
                        {
                            Heading = 0,
                            Checked = false
                        }));
                }
                break;
            case UI.Key.Tab when style.List != RichList.None:
                if (!options.ReadOnly)
                    emit(new RichCommand.Paragraph(style with
                    {
                        Indent = Math.Clamp(style.Indent + (shift ? -1 : 1), 0, 32)
                    }));
                break;
            case UI.Key.Escape:
                FocusNode(0);
                break;
            default:
                handled = false;
                break;
        }
        if (handled)
            Input.Key = UI.Key.None;
        if (Input.Text.Length > 0 && !primary)
        {
            Replace(Input.Text);
            Input.Text = "";
        }
    }
}
