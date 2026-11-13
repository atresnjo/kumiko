using System.Globalization;
using System.Runtime.CompilerServices;

namespace Kumiko.UI;
/// <summary>Persistent editable text. Create outside the view so rebuilding does not reset edits.</summary>
public sealed class TextBuffer(string text = "")
{
    public string Text { get; set; } = text;
    public int Length => Text.Length;

    public override string ToString() => Text;
}

/// <summary>
/// Text field layout and behavior. Name defaults to Placeholder; password values are omitted from semantics.
/// </summary>
public readonly record struct TextFieldOptions(
    string Placeholder = "",
    float Width = 240,
    bool Password = false,
    bool Multiline = false,
    bool Disabled = false,
    string? Name = null,
    bool ReadOnly = false,
    string? Error = null,
    float? Height = null
)
{
    /// <summary>Moves keyboard focus to the field in the frame it first appears, as a search or finder opens.</summary>
    public bool AutoFocus
    {
        get; init;
    }
}
internal sealed class EditState
{
    public TextBuffer? Buffer;
    public Action<string>? ModelChanged;
    public float ScrollX, ScrollY;
    public int Cursor, Anchor;
    public string Composition = "";
    internal RichEditorLayout? RichLayout;
    internal Kumiko.RichText.RichSelection RichSelection;
    public readonly Stack<string> Undo = new(), Redo = new();
}

public sealed partial class Context
{
    static int PreviousCluster(string s, int position)
    {
        int result = 0;
        foreach (var index in StringInfo.ParseCombiningCharacters(s))
        {
            if (index >= position)
                break;
            result = index;
        }

        return result;
    }

    static int NextCluster(string s, int position)
    {
        foreach (var index in StringInfo.ParseCombiningCharacters(s))
            if (index > position)
                return index;
        return s.Length;
    }

    /// <summary>
    /// Adds an accessible text field backed by a persistent TextBuffer. Reports changes and rebuilds
    /// same-frame content.
    /// </summary>
    internal bool TextField(
        TextBuffer buffer,
        TextFieldOptions options = default,
        NodeKey key = default,
        [CallerFilePath] string source = "",
        [CallerLineNumber] int line = 0
    )
    {
        float paddingX = Theme.Space.S3;
        // Internals keep left-to-right geometry; single-line right-to-left text is aligned by offset instead.
        var rtl = IsRightToLeft() && !options.Multiline;
        if (options.Width <= 0)
            options = options with
            {
                Width = 240
            };
        float Align(string text) => rtl ? Math.Max(0, options.Width - 2 * paddingX - TextSystem.Measure(text, Theme.Typography.Body).Width) : 0;
        using var box = Div(key, source, line)
            .Width(options.Width)
            .MinHeight(Theme.Controls.Height)
            .Px(Theme.Space.S3)
            .Py(Theme.Space.S2)
            .Rounded(Theme.Radius.Md)
            .Background(Theme.Surface)
            .Clip()
            .Flow(FlowDirection.LeftToRight)
            .Semantics(Role.TextField, options.Name ?? options.Placeholder, options.Password ? null : buffer.Text);
        var hit = box.Control(!options.Disabled);
        if (options.Height is { } height)
            box.Height(height);
        if (!Edits.TryGetValue(hit.Id, out var edit))
            Edits[hit.Id] = edit = new()
            {
                Cursor = buffer.Length,
                Anchor = buffer.Length
            };
        if (options.AutoFocus && !options.Disabled && box.PreviousRect is null)
            FocusNode(hit.Id);
        edit.Buffer = buffer;
        edit.ModelChanged = null;
        edit.Cursor = Math.Clamp(edit.Cursor, 0, buffer.Length);
        edit.Anchor = Math.Clamp(edit.Anchor, 0, buffer.Length);
        box.Accessibility(new(
            ReadOnly: options.ReadOnly,
            Password: options.Password,
            SelectionStart: options.Password ? 0 : Math.Min(edit.Anchor, edit.Cursor),
            SelectionEnd: options.Password ? 0 : Math.Max(edit.Anchor, edit.Cursor),
            Error: options.Error
        ), request =>
        {
            if (request.Action == AccessibilityAction.SetValue && !options.ReadOnly && request.Text is { } next)
            {
                edit.Undo.Push(buffer.Text);
                edit.Redo.Clear();
                buffer.Text = options.Multiline ? next : next.Replace("\r", "").Replace("\n", "");
                edit.Cursor = edit.Anchor = buffer.Length;
                edit.Composition = "";
                edit.ModelChanged?.Invoke(buffer.Text);
                return true;
            }
            if (request.Action == AccessibilityAction.SetSelection && !options.Password && request.Start >= 0 && request.End >= request.Start && request.End <= buffer.Length)
            {
                var boundaries = StringInfo.ParseCombiningCharacters(buffer.Text).Append(buffer.Length).ToArray();
                edit.Anchor = boundaries.Last(i => i <= request.Start);
                edit.Cursor = boundaries.First(i => i >= request.End);
                FocusNode(hit.Id);
                return true;
            }
            return false;
        });
        if ((hit.Pressed || (hit.Clicked && Input.Key == UI.Key.None)) && hit.Rect is { } rect)
        {
            string hitText = options.Password ? new string('•', new StringInfo(buffer.Text).LengthInTextElements) : buffer.Text;
            float target = Input.Pointer.X - rect.X - paddingX + edit.ScrollX - Align(hitText);
            int cursor = options.Multiline ? TextSystem.HitTest(hitText, Theme.Typography.Body with
            {
                Wrap = true
            },
                new(target, Input.Pointer.Y - rect.Y - Theme.Space.S2 + edit.ScrollY), Math.Max(1, options.Width - 2 * paddingX))
                : TextSystem.HitTest(hitText, Theme.Typography.Body, target);
            if (options.Password)
            {
                var starts = StringInfo.ParseCombiningCharacters(buffer.Text).Append(buffer.Length).ToArray();
                cursor = starts[Math.Clamp(cursor, 0, starts.Length - 1)];
            }

            edit.Cursor = cursor;
            if (Input.Pressed && (Input.Modifiers & Modifiers.Shift) == 0)
                edit.Anchor = cursor;
        }

        box.Border(options.Error is not null ? Theme.Danger : hit.Focused ? Theme.FocusRing : Theme.InputBorder,
            hit.Focused ? Theme.Controls.FocusWidth : 1);
        if (options.Disabled)
            box.Opacity(Theme.Controls.DisabledOpacity);
        bool changed = false;
        void Replace(string value)
        {
            if (options.ReadOnly)
                return;
            edit.Undo.Push(buffer.Text);
            edit.Redo.Clear();
            var start = Math.Min(edit.Cursor, edit.Anchor);
            var end = Math.Max(edit.Cursor, edit.Anchor);
            buffer.Text = string.Concat(buffer.Text.AsSpan(0, start), value, buffer.Text.AsSpan(end));
            edit.Cursor = edit.Anchor = start + value.Length;
            changed = true;
        }

        if (hit.Focused && !options.Disabled)
        {
            var primary = Input.Primary;
            var shift = (Input.Modifiers & Modifiers.Shift) != 0;
            if (primary && Input.Key == UI.Key.A)
            {
                edit.Anchor = 0;
                edit.Cursor = buffer.Length;
            }

            if (primary && Input.Key is UI.Key.C or UI.Key.X && edit.Cursor != edit.Anchor)
            {
                Clipboard = buffer.Text[Math.Min(edit.Cursor, edit.Anchor)..Math.Max(edit.Cursor, edit.Anchor)];
                WriteClipboard?.Invoke(Clipboard);
                if (Input.Key == UI.Key.X)
                    Replace("");
            }

            if (primary && Input.Key == UI.Key.V)
                Replace(ReadClipboard?.Invoke() ?? Clipboard);
            if (!options.ReadOnly && primary && Input.Key is UI.Key.Z or UI.Key.Y)
            {
                var undo = Input.Key == UI.Key.Z && !shift;
                var history = undo ? edit.Undo : edit.Redo;
                var dest = undo ? edit.Redo : edit.Undo;
                if (history.TryPop(out var text))
                {
                    dest.Push(buffer.Text);
                    buffer.Text = text;
                    edit.Cursor = edit.Anchor = text.Length;
                    changed = true;
                }
            }

            if (Input.Key is UI.Key.Left or UI.Key.Right or UI.Key.Home or UI.Key.End)
            {
                if (Input.Key == UI.Key.Home || (primary && Input.Key == UI.Key.Left))
                    edit.Cursor = 0;
                else if (Input.Key == UI.Key.End || (primary && Input.Key == UI.Key.Right))
                    edit.Cursor = buffer.Length;
                else if (!shift && edit.Cursor != edit.Anchor)
                    edit.Cursor = Input.Key == UI.Key.Left ? Math.Min(edit.Cursor, edit.Anchor) : Math.Max(edit.Cursor, edit.Anchor);
                else
                    edit.Cursor = !options.Multiline && buffer.Text.Any(ch => ch > 127)
                        ? TextSystem.MoveVisual(buffer.Text, Theme.Typography.Body, edit.Cursor, Input.Key == UI.Key.Left ? -1 : 1)
                        : Input.Key == UI.Key.Left ? PreviousCluster(buffer.Text, edit.Cursor) : NextCluster(buffer.Text, edit.Cursor);
                if (!shift)
                    edit.Anchor = edit.Cursor;
            }

            if (options.Multiline && Input.Key is UI.Key.Up or UI.Key.Down)
            {
                var multilineStyle = Theme.Typography.Body with
                {
                    Wrap = true
                };
                float textWidth = Math.Max(1, options.Width - 2 * paddingX);
                var caret = TextSystem.CaretRectangle(buffer.Text, multilineStyle, edit.Cursor, textWidth);
                edit.Cursor = TextSystem.HitTest(buffer.Text, multilineStyle,
                    new(caret.X, caret.Y + (Input.Key == UI.Key.Up ? -.5f : caret.Height + .5f)), textWidth);
                if (!shift)
                    edit.Anchor = edit.Cursor;
            }

            if (Input.Key is UI.Key.Backspace or UI.Key.Delete)
            {
                if (edit.Cursor == edit.Anchor)
                {
                    if (Input.Key == UI.Key.Backspace)
                    {
                        edit.Anchor = primary ? 0 : PreviousCluster(buffer.Text, edit.Cursor);
                        if ((Input.Modifiers & Modifiers.Alt) != 0)
                        {
                            while (edit.Anchor > 0 && char.IsWhiteSpace(buffer.Text[edit.Anchor]))
                                edit.Anchor = PreviousCluster(buffer.Text, edit.Anchor);
                            while (edit.Anchor > 0 && !char.IsWhiteSpace(buffer.Text[PreviousCluster(buffer.Text, edit.Anchor)]))
                                edit.Anchor = PreviousCluster(buffer.Text, edit.Anchor);
                        }
                    }
                    else
                        edit.Anchor = primary ? buffer.Length : NextCluster(buffer.Text, edit.Cursor);
                }

                if (edit.Cursor != edit.Anchor)
                    Replace("");
            }

            if (Input.Text.Length > 0)
                Replace(options.Multiline ? Input.Text : Input.Text.Replace("\r", "").Replace("\n", ""));
            if (Input.Composition is { } composition)
            {
                if (Input.CommitComposition)
                {
                    Replace(composition);
                    edit.Composition = "";
                }
                else
                    edit.Composition = composition;
            }

            if (Input.Key == UI.Key.Escape)
            {
                var composing = edit.Composition.Length > 0 || Input.Composition is not null || Input.CommitComposition;
                edit.Composition = "";
                if (composing)
                    Input.Key = UI.Key.None;
                else
                    FocusNode(0);
            }

            if (Input.Key == UI.Key.Enter && options.Multiline)
                Replace("\n");
        }

        string shown = options.Password ? new string('•', new StringInfo(buffer.Text).LengthInTextElements) : buffer.Text;
        int shownCursor = options.Password ? new StringInfo(buffer.Text[..edit.Cursor]).LengthInTextElements : edit.Cursor;
        int shownAnchor = options.Password ? new StringInfo(buffer.Text[..edit.Anchor]).LengthInTextElements : edit.Anchor;
        int markedTextStart = Math.Min(shownCursor, shownAnchor);
        string markedText = options.Password ? new string('•', new StringInfo(edit.Composition).LengthInTextElements) : edit.Composition;
        var preview = markedText.Length == 0 ? shown : string.Concat(shown.AsSpan(0, markedTextStart), markedText, shown.AsSpan(Math.Max(shownCursor, shownAnchor)));
        bool placeholder = preview.Length == 0;
        var display = placeholder ? options.Placeholder : preview;
        int previewCursor = markedText.Length > 0 ? markedTextStart + markedText.Length : shownCursor;
        var displayStyle = Theme.Typography.Body with
        {
            Wrap = options.Multiline
        };
        float contentWidth = Math.Max(1, options.Width - 2 * paddingX);
        var caretRect = TextSystem.CaretRectangle(preview, displayStyle, previewCursor, contentWidth);
        float align = Align(preview);
        float caretX = caretRect.X + align;
        if (hit.Focused && !options.Multiline && align > 0)
            edit.ScrollX = 0;
        else if (hit.Focused && !options.Multiline)
        {
            float available = Math.Max(1, options.Width - 2 * paddingX - 2);
            if (caretX - edit.ScrollX > available)
                edit.ScrollX = caretX - available;
            if (caretX < edit.ScrollX)
                edit.ScrollX = caretX;
            edit.ScrollX = Math.Max(0, edit.ScrollX);
        }
        if (hit.Focused && options.Multiline && options.Height is { } fixedHeight)
        {
            float available = Math.Max(1, fixedHeight - 2 * Theme.Space.S2);
            edit.ScrollY = Math.Max(0, Math.Clamp(edit.ScrollY, caretRect.Bottom - available, Math.Max(caretRect.Y, caretRect.Bottom - available)));
        }

        if (hit.Focused && edit.Anchor != edit.Cursor && markedText.Length == 0)
        {
            foreach (var selection in TextSystem.SelectionRectangles(shown, displayStyle, Math.Min(shownAnchor, shownCursor), Math.Max(shownAnchor, shownCursor), contentWidth))
            {
                using var highlight = Div(key: $"selection:{selection.Y.ToString(CultureInfo.InvariantCulture)}").Absolute().Offset(selection.X + align - edit.ScrollX, selection.Y - edit.ScrollY)
                    .Width(selection.Width).Height(selection.Height).Background(Theme.Accent.Alpha(.25f));
            }
        }

        if (hit.Focused && markedText.Length > 0)
            foreach (var mark in TextSystem.SelectionRectangles(preview, displayStyle, markedTextStart, markedTextStart + markedText.Length, contentWidth))
            {
                using var underline = Div(key: $"markedText:{mark.Y.ToString(CultureInfo.InvariantCulture)}").Absolute()
                    .Offset(mark.X + align - edit.ScrollX, mark.Bottom - edit.ScrollY - 1).Width(mark.Width).Height(1).Background(Theme.Accent);
            }

        Text(display, Theme.Typography.Body with
        {
            Color = placeholder ? Theme.Muted : Theme.Text,
            Wrap = options.Multiline
        });
        Tree[Count - 1].Style.Offset = new((placeholder ? Align(display) : align) - edit.ScrollX, -edit.ScrollY);
        if (hit.Focused)
        {
            using (Div().Absolute().Offset(caretX - edit.ScrollX, caretRect.Y - edit.ScrollY).Width(1).Height(caretRect.Height).Background(Theme.Accent))
            {
            }
        }

        if (hit.Hovered)
            Cursor = Cursor.Text;
        if (changed)
            Changed = true;
        box.SetCaret(new(paddingX + caretX - edit.ScrollX, Theme.Space.S2 + caretRect.Y - edit.ScrollY, 1, caretRect.Height));
        box.Accessibility(new(
            ReadOnly: options.ReadOnly, Password: options.Password,
            SelectionStart: options.Password ? 0 : Math.Min(edit.Anchor, edit.Cursor),
            SelectionEnd: options.Password ? 0 : Math.Max(edit.Anchor, edit.Cursor), Error: options.Error,
            TextOffsetX: paddingX + align - edit.ScrollX, TextOffsetY: Theme.Space.S2 - edit.ScrollY,
            TextWidth: contentWidth, Multiline: options.Multiline
        ));
        return changed;
    }
}
