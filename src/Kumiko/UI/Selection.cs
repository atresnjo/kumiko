using System.Globalization;

namespace Kumiko.UI;

public sealed partial class Context
{
    ulong selectionAnchor, selectionHead;
    int selectionStart, selectionEnd;
    bool selecting;
    bool IsSelectable(int index)
    {
        bool result = false;
        for (int i = index; i >= 0; i = Tree[i].Parent)
        {
            if (Tree[i].Style.Control)
                return false;
            result |= Tree[i].Style.Selectable;
        }

        return result;
    }

    int TextIndex(int node, Point point)
    {
        var n = Tree[node];
        var text = n.Text ?? "";
        if (n.TextStyle.Overflow != TextOverflow.Visible)
        {
            var display = TextSystem.Display(text, n.TextStyle, n.Rect.Width, n.RightToLeft, n.Spans);
            return TextSystem.DisplayHitTest(display, new(point.X - n.Rect.X, point.Y - n.Rect.Y), n.Rect.Width, n.RightToLeft);
        }
        var starts = StringInfo.ParseCombiningCharacters(text);
        float target = point.X - n.Rect.X;
        for (int i = 0; i < starts.Length; i++)
        {
            int end = i + 1 < starts.Length ? starts[i + 1] : text.Length;
            float left = TextSystem.Measure(text[..starts[i]], n.TextStyle).Width, right = TextSystem.Measure(text[..end], n.TextStyle).Width;
            if (target < (left + right) / 2)
                return starts[i];
        }

        return text.Length;
    }

    void SelectInput()
    {
        if (Input.Pressed)
        {
            selecting = false;
            for (int i = Count - 1; i >= 0; i--)
            {
                var n = Tree[i];
                if (n.Kind != ContentKind.Text || !IsSelectable(i) || !n.Rect.Intersect(n.ClipRect).Contains(Input.Pointer))
                    continue;
                selectionAnchor = selectionHead = n.Id;
                selectionStart = selectionEnd = TextIndex(i, Input.Pointer);
                selecting = true;
                focus = 0;
                break;
            }

            if (!selecting)
                selectionAnchor = selectionHead = 0;
        }

        if (selecting && (Input.Down || Input.Released))
        {
            int nearest = -1;
            float distance = float.PositiveInfinity;
            for (int i = 0; i < Count; i++)
            {
                var n = Tree[i];
                if (n.Kind != ContentKind.Text || !IsSelectable(i))
                    continue;
                float dy = Input.Pointer.Y - Math.Clamp(Input.Pointer.Y, n.Rect.Y, n.Rect.Bottom);
                float dx = Input.Pointer.X - Math.Clamp(Input.Pointer.X, n.Rect.X, n.Rect.Right);
                float d = dx * dx + dy * dy;
                if (d < distance)
                {
                    nearest = i;
                    distance = d;
                }
            }

            if (nearest >= 0)
            {
                selectionHead = Tree[nearest].Id;
                selectionEnd = TextIndex(nearest, Input.Pointer);
            }
        }

        if (Input.Released)
            selecting = false;
        if (Input.Primary && Input.Key == UI.Key.C && !Edits.ContainsKey(focus))
        {
            var text = new System.Text.StringBuilder();
            for (int i = 0; i < Count; i++)
                if (SelectedRange(i) is { } range)
                {
                    if (text.Length > 0)
                        text.Append('\n');
                    text.Append(Tree[i].Text.AsSpan(range.Start, range.End - range.Start));
                }

            if (text.Length > 0)
            {
                Clipboard = text.ToString();
                WriteClipboard?.Invoke(Clipboard);
            }
        }
    }

    internal (int Start, int End)? SelectedRange(int index)
    {
        if (selectionAnchor == 0 || selectionHead == 0 || Tree[index].Kind != ContentKind.Text)
            return null;
        int a = -1, b = -1;
        for (int i = 0; i < Count; i++)
        {
            if (Tree[i].Id == selectionAnchor)
                a = i;
            if (Tree[i].Id == selectionHead)
                b = i;
        }

        if (a < 0 || b < 0 || index < Math.Min(a, b) || index > Math.Max(a, b))
            return null;
        int start = a <= b ? selectionStart : selectionEnd, end = a <= b ? selectionEnd : selectionStart;
        int low = index == Math.Min(a, b) ? start : 0, high = index == Math.Max(a, b) ? end : Tree[index].Text!.Length;
        if (a == b)
        {
            low = Math.Min(selectionStart, selectionEnd);
            high = Math.Max(selectionStart, selectionEnd);
        }

        return high > low ? (low, high) : null;
    }
}
