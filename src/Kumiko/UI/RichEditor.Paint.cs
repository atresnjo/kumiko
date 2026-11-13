using Kumiko.RichText;

namespace Kumiko.UI;

public sealed partial class Context
{
    static string NumberMarker(RichValue value, int paragraph)
    {
        int number = 1, indent = value.Paragraphs[paragraph].Style.Indent;
        for (int p = paragraph - 1; p >= 0; p--)
        {
            var style = value.Paragraphs[p].Style;
            if (style.List != RichList.Number || style.Indent < indent)
                break;
            if (style.Indent == indent)
                number++;
        }
        return number.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".";
    }
    void RichParagraphPaint(RichDocument document, EditState edit, RichEditorLayout layout, int p, bool focused)
    {
        var paragraph = layout.Value.Paragraphs[p];
        float inset = layout.Inset(p), alignment = layout.Alignment(this, p);
        float width = Math.Max(1, layout.Width - inset);
        using var row = Div(key: p).Absolute().Offset(0, layout.Tops[p]).Width(layout.Width).Height(layout.Heights[p]);
        if (paragraph.Style.Code)
            row.Background(Theme.Background).Rounded(Theme.Radius.Sm);
        if (paragraph.Style.List != RichList.None || paragraph.Style.Quote)
        {
            string marker = paragraph.Style.Quote ? "│" : paragraph.Style.List switch
            {
                RichList.Bullet => "•",
                RichList.Number => NumberMarker(layout.Value, p),
                RichList.Task => paragraph.Style.Checked ? "☑" : "☐",
                _ => ""
            };
            using var mark = Div(key: "marker").Absolute().Offset(Math.Max(0, inset - 28), 0).Width(26).Height(24);
            Text(marker, Theme.Typography.Body with
            {
                Color = Theme.Muted
            }, key: "text");
        }
        using var content = Div(key: "paragraph").Absolute().Offset(inset + alignment, 0).Width(width).Height(layout.Heights[p]).Flow(layout.RightToLeft(p) ? FlowDirection.RightToLeft : FlowDirection.LeftToRight);
        int start = document.Selection.Start - layout.Value.ParagraphStarts[p], end = document.Selection.End - layout.Value.ParagraphStarts[p];
        if (focused && start != end)
        {
            int index = 0;
            foreach (var rect in TextSystem.DisplaySelection(layout.Display(this, p), start, end, width, layout.RightToLeft(p)))
            {
                using var selection = Div(key: $"selection:{index++}").Absolute().Inset(LayoutEdge.Left, rect.X).Inset(LayoutEdge.Top, rect.Y).Width(rect.Width).Height(rect.Height)
                    .Background(Theme.Accent.Alpha(.25f));
            }
        }
        RichText(paragraph.Text, layout.Spans(p, Theme), layout.Style(p), key: "text");
        if (edit.Composition.Length > 0)
            Tree[Count - 1].NativeCovered = true;
        if (paragraph.Style.Heading > 0)
        {
            Tree[Count - 1].Role = Role.Heading;
            Tree[Count - 1].Accessible = new()
            {
                Level = paragraph.Style.Heading
            };
        }
        if (focused && edit.Composition.Length > 0)
        {
            int markedStart = document.Selection.Cursor - edit.Composition.Length - layout.Value.ParagraphStarts[p];
            int markedEnd = document.Selection.Cursor - layout.Value.ParagraphStarts[p];
            int index = 0;
            foreach (var rect in TextSystem.DisplaySelection(layout.Display(this, p), markedStart, markedEnd, width, layout.RightToLeft(p)))
            {
                using var mark = Div(key: $"composition:{index++}").Absolute().Inset(LayoutEdge.Left, rect.X).Inset(LayoutEdge.Top, rect.Bottom - 1)
                    .Width(rect.Width).Height(1).Background(Theme.Accent);
            }
        }
    }
}
