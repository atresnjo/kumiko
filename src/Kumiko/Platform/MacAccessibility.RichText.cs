using System.Runtime.InteropServices;
using O = Kumiko.Platform.ObjC;

namespace Kumiko.Platform;

internal sealed unsafe partial class MacAccessibility
{
    static readonly Lazy<nint> appKitSymbols = new(() => NativeLibrary.Load("/System/Library/Frameworks/AppKit.framework/AppKit"));
    static readonly Lazy<nint> attributeSend = new(() => NativeLibrary.GetExport(NativeLibrary.Load("/usr/lib/libobjc.A.dylib"), "objc_msgSend"));
    static nint AttributeKey(string name) => Marshal.ReadIntPtr(NativeLibrary.GetExport(appKitSymbols.Value, name));
    [UnmanagedCallersOnly]
    static nint AttributedString(nint self, nint cmd, NativeRange range) => Safe(self, () =>
    {
        var node = Node(self);
        if (node?.Properties?.Password == true || node?.Value is not { } text || range.Location > (nuint)text.Length || range.Length > (nuint)text.Length - range.Location)
            return 0;
        int start = (int)range.Location, end = start + (int)range.Length;
        var value = O.O(O.O(O.Class("NSMutableAttributedString"), "alloc"), "initWithString:", O.TempString(text[start..end]));
        if (node.Properties?.RichValue is { } rich)
        {
            for (int p = rich.ParagraphAt(start); p <= rich.ParagraphAt(end); p++)
            {
                int offset = rich.ParagraphStarts[p];
                var paragraph = rich.Paragraphs[p];
                foreach (var run in paragraph.Runs)
                {
                    int from = Math.Max(start, offset), to = Math.Min(end, offset + run.Text.Length);
                    if (to > from)
                    {
                        var a = run.Attributes;
                        var descriptions = O.New("NSMutableArray");
                        try
                        {
                            void Add(string description) => O.V(descriptions, "addObject:", O.TempString(description));
                            if (a.Bold)
                                Add("Bold");
                            if (a.Italic)
                                Add("Italic");
                            if (a.Code || paragraph.Style.Code)
                                Add("Code");
                            if (a.Link is { } link)
                                Add("Link: " + link);
                            if (a.Attachment is { } attachment)
                                Add("Attachment: " + attachment.Name);
                            if (paragraph.Style.Heading > 0)
                                Add("Heading " + paragraph.Style.Heading);
                            if (paragraph.Style.List != RichText.RichList.None)
                                Add(paragraph.Style.List + (paragraph.Style.Checked ? ", checked" : ""));
                            var local = new NativeRange((nuint)(from - start), (nuint)(to - from));
                            void Attribute(string name, nint attribute) => ((delegate* unmanaged<nint, nint, nint, nint, NativeRange, void>)attributeSend.Value)(value,
                                O.Sel("addAttribute:value:range:"), AttributeKey(name), attribute, local);
                            Attribute("NSAccessibilityCustomTextAttribute", descriptions);
                            if (a.Underline)
                                Attribute("NSAccessibilityUnderlineTextAttribute", O.O(O.Class("NSNumber"), "numberWithInteger:", (nint)1));
                            if (a.Strike)
                                Attribute("NSAccessibilityStrikethroughTextAttribute", O.O(O.Class("NSNumber"), "numberWithBool:", (nint)1));
                        }
                        finally
                        {
                            O.Release(descriptions);
                        }
                    }
                    offset += run.Text.Length;
                }
            }
        }
        return O.O(value, "autorelease");
    }, (nint)0);
}
