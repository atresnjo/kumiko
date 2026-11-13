using System.Runtime.InteropServices;
using System.Text;
using Kumiko.UI;

namespace Kumiko.Platform;

internal sealed unsafe partial class LinuxAccessibility
{
    static string TextOf(nint self)
    {
        var node = Node(self);
        return node?.Properties?.Password == true ? "" : node?.Value ?? (node?.Role is Role.Text or Role.Heading ? node.Name : "");
    }
    static int Utf16(string text, int offset)
    {
        if (offset < 0)
            return text.Length;
        int position = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            if (offset-- == 0)
                break;
            position += rune.Utf16SequenceLength;
        }
        return position;
    }
    static int Utf32(string text, int offset)
    {
        int count = 0;
        foreach (var rune in text.AsSpan(0, Math.Clamp(offset, 0, text.Length)).EnumerateRunes())
            count++;
        return count;
    }
    [UnmanagedCallersOnly]
    static void TextInit(nint iface, nint data)
    {
        Slot(iface, 0, (nint)(delegate* unmanaged<nint, int, int, nint>)&GetText);
        Slot(iface, 3, (nint)(delegate* unmanaged<nint, int, uint>)&Character);
        Slot(iface, 5, (nint)(delegate* unmanaged<nint, int>)&Caret);
        Slot(iface, 6, (nint)(delegate* unmanaged<nint, int, int*, int*, nint>)&RunAttributes);
        Slot(iface, 9, (nint)(delegate* unmanaged<nint, int>)&CharacterCount);
        Slot(iface, 11, (nint)(delegate* unmanaged<nint, int>)&SelectionCount);
        Slot(iface, 12, (nint)(delegate* unmanaged<nint, int, int*, int*, nint>)&Selection);
        Slot(iface, 13, (nint)(delegate* unmanaged<nint, int, int, int>)&AddSelection);
        Slot(iface, 14, (nint)(delegate* unmanaged<nint, int, int>)&RemoveSelection);
        Slot(iface, 15, (nint)(delegate* unmanaged<nint, int, int, int, int>)&SetSelection);
        Slot(iface, 16, (nint)(delegate* unmanaged<nint, int, int>)&SetCaret);
        Slot(iface, 23, (nint)(delegate* unmanaged<nint, int, int, int*, int*, nint>)&StringAtOffset);
    }
    [UnmanagedCallersOnly]
    static nint GetText(nint self, int start, int end)
    {
        var text = TextOf(self);
        int from = Utf16(text, Math.Max(0, start)), to = Utf16(text, end);
        return Atk.g_strdup(text[from..Math.Max(from, to)]);
    }
    [UnmanagedCallersOnly]
    static uint Character(nint self, int offset)
    {
        var text = TextOf(self);
        int index = Utf16(text, offset);
        return index < text.Length ? (uint)Rune.GetRuneAt(text, index).Value : 0;
    }
    [UnmanagedCallersOnly] static int Caret(nint self) => Utf32(TextOf(self), Node(self)?.Properties?.SelectionEnd ?? 0);
    [UnmanagedCallersOnly] static int CharacterCount(nint self) => TextOf(self).EnumerateRunes().Count();
    [UnmanagedCallersOnly] static int SelectionCount(nint self) => Node(self)?.Properties is { Password: false } p && p.SelectionStart != p.SelectionEnd ? 1 : 0;
    [UnmanagedCallersOnly]
    static nint Selection(nint self, int index, int* start, int* end)
    {
        var text = TextOf(self);
        var p = Node(self)?.Properties;
        int from = p?.Password == true ? 0 : Math.Clamp(p?.SelectionStart ?? 0, 0, text.Length);
        int to = p?.Password == true ? 0 : Math.Clamp(p?.SelectionEnd ?? 0, from, text.Length);
        if (start != null)
            *start = Utf32(text, from);
        if (end != null)
            *end = Utf32(text, to);
        return index == 0 ? Atk.g_strdup(text[from..to]) : 0;
    }
    static int SelectText(nint self, int start, int end)
    {
        if (Node(self)?.Properties?.Password == true || start < 0 || end < start)
            return 0;
        var text = TextOf(self);
        return Act(self, new(AccessibilityAction.SetSelection, Start: Utf16(text, start), End: Utf16(text, end))) ? 1 : 0;
    }
    [UnmanagedCallersOnly] static int AddSelection(nint self, int start, int end) => SelectText(self, start, end);
    [UnmanagedCallersOnly] static int SetSelection(nint self, int index, int start, int end) => index == 0 ? SelectText(self, start, end) : 0;
    [UnmanagedCallersOnly] static int RemoveSelection(nint self, int index) => index == 0 ? SelectText(self, 0, 0) : 0;
    [UnmanagedCallersOnly] static int SetCaret(nint self, int offset) => SelectText(self, offset, offset);
    [UnmanagedCallersOnly]
    static nint StringAtOffset(nint self, int offset, int granularity, int* start, int* end)
    {
        var text = TextOf(self);
        offset = Math.Clamp(offset, 0, text.EnumerateRunes().Count());
        int from = Utf16(text, offset), to = from;
        if (granularity == 0)
            to = Utf16(text, offset + 1);
        else if (granularity == 1)
        {
            while (from > 0 && !char.IsWhiteSpace(text[from - 1]))
                from--;
            while (to < text.Length && !char.IsWhiteSpace(text[to]))
                to++;
        }
        else
        {
            while (from > 0 && text[from - 1] != '\n')
                from--;
            while (to < text.Length && text[to] != '\n')
                to++;
        }
        if (start != null)
            *start = Utf32(text, from);
        if (end != null)
            *end = Utf32(text, to);
        return Atk.g_strdup(text[from..to]);
    }
    [UnmanagedCallersOnly]
    static void EditInit(nint iface, nint data)
    {
        Slot(iface, 1, (nint)(delegate* unmanaged<nint, nint, void>)&SetText);
        Slot(iface, 2, (nint)(delegate* unmanaged<nint, nint, int, int*, void>)&InsertText);
        Slot(iface, 5, (nint)(delegate* unmanaged<nint, int, int, void>)&DeleteText);
    }
    [UnmanagedCallersOnly]
    static void SetText(nint self, nint value)
    {
        if (Node(self)?.Properties?.ReadOnly != true)
            Act(self, new(AccessibilityAction.SetValue, Text: Marshal.PtrToStringUTF8(value) ?? ""));
    }
    [UnmanagedCallersOnly]
    static void InsertText(nint self, nint value, int length, int* position)
    {
        if (Node(self)?.Properties?.ReadOnly == true || Node(self)?.Properties?.Password == true || position == null)
            return;
        var text = TextOf(self);
        var inserted = length < 0 ? Marshal.PtrToStringUTF8(value) ?? "" : Encoding.UTF8.GetString(new ReadOnlySpan<byte>((void*)value, length));
        int at = Utf16(text, *position);
        if (Act(self, new(AccessibilityAction.SetValue, Text: text.Insert(at, inserted))))
            *position += inserted.EnumerateRunes().Count();
    }
    [UnmanagedCallersOnly]
    static void DeleteText(nint self, int start, int end)
    {
        if (Node(self)?.Properties?.ReadOnly == true || Node(self)?.Properties?.Password == true)
            return;
        var text = TextOf(self);
        int from = Utf16(text, start), to = Utf16(text, end);
        if (to >= from)
            Act(self, new(AccessibilityAction.SetValue, Text: text.Remove(from, to - from)));
    }
}
