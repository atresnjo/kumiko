using System.Runtime.InteropServices;

namespace Kumiko.Platform;

internal sealed unsafe partial class LinuxAccessibility
{
    [StructLayout(LayoutKind.Sequential)]
    struct TextAttribute
    {
        internal nint Name, Value;
    }
    [UnmanagedCallersOnly]
    static nint RunAttributes(nint self, int offset, int* start, int* end)
    {
        try
        {
            var rich = Node(self)?.Properties?.RichValue;
            if (rich is null)
                return 0;
            string text = TextOf(self);
            int position = offset < 0 ? Node(self)?.Properties?.SelectionEnd ?? 0 : Utf16(text, offset);
            position = Math.Clamp(position, 0, rich.Length);
            int p = rich.ParagraphAt(position), from = rich.ParagraphStarts[p];
            var paragraph = rich.Paragraphs[p];
            var attributes = rich.AttributesAt(position);
            int to = from + paragraph.Text.Length;
            foreach (var run in paragraph.Runs)
            {
                if (position < from + run.Text.Length)
                {
                    to = from + run.Text.Length;
                    break;
                }
                from += run.Text.Length;
            }
            if (start != null)
                *start = Utf32(text, from);
            if (end != null)
                *end = Utf32(text, to);
            nint result = 0;
            void Add(string name, string value)
            {
                var pair = (TextAttribute*)g_malloc0((nuint)sizeof(TextAttribute));
                pair->Name = Atk.g_strdup(name);
                pair->Value = Atk.g_strdup(value);
                result = g_slist_prepend(result, (nint)pair);
            }
            Add("weight", attributes.Bold ? "700" : "400");
            Add("style", attributes.Italic ? "italic" : "normal");
            Add("underline", attributes.Underline ? "single" : "none");
            Add("strikethrough", attributes.Strike ? "true" : "false");
            if (attributes.Link is { } link)
                Add("link", link);
            if (attributes.Attachment is { } attachment)
                Add("attachment", attachment.Name);
            if (paragraph.Style.Heading > 0)
                Add("heading-level", paragraph.Style.Heading.ToString(System.Globalization.CultureInfo.InvariantCulture));
            return result;
        }
        catch
        {
            return 0;
        }
    }
    [DllImport("libglib-2.0.so.0")] static extern nint g_malloc0(nuint size);
    [DllImport("libglib-2.0.so.0")] static extern nint g_slist_prepend(nint list, nint value);
}
