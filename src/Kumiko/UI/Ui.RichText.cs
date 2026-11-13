using System.Runtime.CompilerServices;
using Kumiko.RichText;

namespace Kumiko.UI;

public readonly record struct RichEditorOptions(float Width = 500, float Height = 320, bool ReadOnly = false, bool Disabled = false, string Placeholder = "");

public sealed partial class Ui<TMessage>
{
    /// <summary>A native-painted, controlled rich editor. Commands return through the application's pure Update.
    /// IME preview and viewport caches are node-owned; committed text, selection and undo are model-owned.</summary>
    public void RichTextEditor(NodeKey key, string label, RichDocument document, Func<RichCommand, TMessage> message,
        RichEditorOptions options = default, [CallerFilePath] string source = "", [CallerLineNumber] int line = 0)
    {
        Check();
        var dispatch = send;
        context.RichEditor(key, label, document, command => dispatch(message(command)), options, source, line);
    }
}

public sealed partial class Context
{
    internal void RichEditor(NodeKey key, string label, RichDocument document, Action<RichCommand> send,
        RichEditorOptions options, string source, int line)
    {
        float width = options.Width > 0 ? options.Width : 500, height = options.Height > 0 ? options.Height : 320;
        if (!float.IsFinite(width) || !float.IsFinite(height))
            throw new ArgumentOutOfRangeException(nameof(options));
        const float padding = 12;
        using var box = Div(key, source, line).Width(width).Height(height).Padding(padding).Scroll().Clip()
            .Background(Theme.Surface).Rounded(Theme.Radius.Md).Flow(FlowDirection.LeftToRight)
            .Semantics(Role.TextField, label, document.Value.PlainText);
        var hit = box.Control(!options.Disabled);
        box.Border(hit.Focused ? Theme.FocusRing : Theme.InputBorder);
        if (!Edits.TryGetValue(hit.Id, out var edit))
            Edits[hit.Id] = edit = new();
        if (!options.ReadOnly && !options.Disabled)
            PreviewTab(hit.Id, c =>
            {
                var style = document.Value.Paragraphs[document.Value.ParagraphAt(document.Selection.Cursor)].Style;
                if (style.List == RichList.None)
                    return false;
                int delta = (c.Input.Modifiers & Modifiers.Shift) != 0 ? -1 : 1;
                c.WithSource(hit.Id, () => send(new RichCommand.Paragraph(style with { Indent = Math.Clamp(style.Indent + delta, 0, 32) })));
                return true;
            });
        edit.Buffer ??= new();
        edit.Buffer.Text = document.Value.PlainText;
        edit.Cursor = document.Selection.Cursor;
        edit.Anchor = document.Selection.Anchor;
        if (!Scrolls.TryGetValue(hit.Id, out var scroll))
            Scrolls[hit.Id] = scroll = new()
            {
                Follow = false
            };
        float contentWidth = Math.Max(1, width - padding * 2);
        var body = Theme.Typography.Body;
        if (edit.Composition.Length > 0 && (!hit.Focused || options.ReadOnly || options.Disabled ||
            !ReferenceEquals(edit.RichLayout?.Value, document.Value) || edit.RichSelection != document.Selection))
            edit.Composition = "";
        if (edit.RichLayout is not { } layout || !ReferenceEquals(layout.Value, document.Value) || layout.Width != contentWidth || layout.Body != body)
            edit.RichLayout = layout = new(document.Value, contentWidth, body);
        layout.Positions();
        int cursorParagraph = document.Value.ParagraphAt(document.Selection.Cursor);
        layout.Measure(this, cursorParagraph, cursorParagraph);
        int first = Math.Max(0, layout.At(scroll.Offset) - 2), last = Math.Min(layout.Value.Paragraphs.Count - 1, layout.At(scroll.Offset + height) + 2);
        layout.Measure(this, first, last);
        var current = document;
        bool reveal = false;
        void Emit(RichCommand command)
        {
            var next = current.Apply(command);
            if (next == current && command is not (RichCommand.ActivateLink or RichCommand.RequestLink))
                return;
            current = next;
            WithSource(hit.Id, () => send(command));
            reveal = true;
        }
        if ((hit.Pressed || Input.Down && active == hit.Id) && hit.Rect is { } rect)
        {
            var target = layout.Hit(this, new(Input.Pointer.X - rect.X - padding, Input.Pointer.Y - rect.Y - padding + scroll.Offset));
            int cursor = target.Cursor;
            int paragraphIndex = document.Value.ParagraphAt(cursor);
            var paragraph = document.Value.Paragraphs[paragraphIndex];
            if (Input.Pressed && !options.ReadOnly && paragraph.Style.List == RichList.Task && Input.Pointer.X - rect.X - padding < layout.Inset(paragraphIndex))
                Emit(new RichCommand.CheckTask(paragraphIndex));
            if (Input.Pressed && (options.ReadOnly || Input.Primary) && document.Value.AttributesAt(cursor).Link is { } link)
                Emit(new RichCommand.ActivateLink(link));
            Emit(new RichCommand.Select(Input.Pressed && (Input.Modifiers & Modifiers.Shift) == 0 ? cursor : current.Selection.Anchor, cursor) { Affinity = target.Affinity });
        }
        if (hit.Focused && !options.Disabled)
            RichInput(current, edit, layout, options, Emit);
        var captured = current;
        box.Accessibility(new(ReadOnly: options.ReadOnly, SelectionStart: captured.Selection.Start, SelectionEnd: captured.Selection.End,
            TextWidth: contentWidth, Multiline: true, TextOffsetX: padding, TextOffsetY: padding - scroll.Offset)
        {
            RichValue = captured.Value,
            Description = "Rich text. " + (captured.Typing.Bold ? "Bold. " : "") + (captured.Typing.Italic ? "Italic. " : "")
        }, request =>
        {
            if (request.Action == AccessibilityAction.SetSelection && request.Start >= 0 && request.End >= request.Start && request.End <= captured.Value.Length)
            {
                FocusNode(hit.Id);
                send(new RichCommand.Select(request.Start, request.End));
                return true;
            }
            if (request.Action == AccessibilityAction.SetValue && !options.ReadOnly && request.Text is { } text)
            {
                send(new RichCommand.Select(0, captured.Value.Length));
                send(new RichCommand.Insert(text));
                return true;
            }
            return false;
        });
        // Preedit uses an ephemeral attributed value; accessibility/history retain the committed projection.
        var painting = document;
        if (hit.Focused && edit.Composition.Length > 0)
        {
            var preview = document.Value.Replace(document.Selection.Start, document.Selection.End,
                RichValue.Text(edit.Composition, document.Typing, document.Value.Paragraphs[document.Value.ParagraphAt(document.Selection.Start)].Style));
            int position = preview.Snap(document.Selection.Start + edit.Composition.Length, forward: true);
            painting = document with
            {
                Value = preview,
                Selection = new(position, position)
            };
            layout = new(preview, contentWidth, body);
            layout.Positions();
            int p = preview.ParagraphAt(position);
            layout.Measure(this, p, p);
        }
        var caret = layout.Caret(this, painting.Selection.Cursor, painting.Selection.Affinity);
        if (hit.Focused && (reveal || edit.RichSelection != document.Selection || edit.Composition.Length > 0))
        {
            if (caret.Y < scroll.Offset)
                scroll.Offset = caret.Y;
            else if (caret.Bottom > scroll.Offset + height - 2 * padding)
                scroll.Offset = Math.Max(0, caret.Bottom - height + 2 * padding);
            scroll.Target = float.NaN;
        }
        edit.RichSelection = document.Selection;
        first = Math.Max(0, layout.At(scroll.Offset) - 2);
        last = Math.Min(layout.Value.Paragraphs.Count - 1, layout.At(scroll.Offset + height) + 2);
        layout.Measure(this, first, last);
        using (Div(key: "content").Width(contentWidth).Height(layout.Tops[^1]))
        {
            for (int p = first; p <= last; p++)
                RichParagraphPaint(painting, edit, layout, p, hit.Focused);
            if (hit.Focused)
            {
                using var cursor = Div(key: "caret").Absolute().Offset(caret.X, caret.Y).Width(1).Height(caret.Height).Background(Theme.Accent);
            }
        }
        box.SetCaret(new(padding + caret.X, padding + caret.Y - scroll.Offset, 1, caret.Height));
        if (hit.Hovered)
            Cursor = Cursor.Text;
        if (options.Disabled)
            box.Opacity(Theme.Controls.DisabledOpacity);
    }
}
