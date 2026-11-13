using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Kumiko.UI;

namespace Kumiko.Platform;

[GeneratedComClass]
internal sealed partial class UiaTextRange(UiaElement element, int start, int end) : IUiaTextRange
{
    const int Unavailable = unchecked((int)0x80040201), Invalid = unchecked((int)0x80070057);
    string Text => element.Snapshot?.Properties?.Password == true ? "" : element.Snapshot?.Value ?? "";
    bool Alive => element.IsAlive && element.Snapshot?.Properties?.Password != true;
    UiaElement Element => element;
    int Start => Math.Clamp(start, 0, Text.Length);
    int End => Math.Clamp(end, Start, Text.Length);
    static unsafe UiaTextRange? Managed(nint pointer) => pointer == 0 ? null : ComInterfaceMarshaller<IUiaTextRange>.ConvertToManaged((void*)pointer) as UiaTextRange;
    bool Compatible(UiaTextRange? other) => other is not null && other.Element.Owner == element.Owner && other.Element.Id == element.Id;
    public int Clone(out nint range)
    {
        range = Alive ? element.TextRange(Start, End) : 0;
        return Alive ? 0 : Unavailable;
    }
    public int Compare(nint range, out int same)
    {
        var other = Managed(range);
        same = Compatible(other) && Start == other!.Start && End == other.End ? 1 : 0;
        return Alive ? 0 : Unavailable;
    }
    public int CompareEndpoints(int endpoint, nint range, int targetEndpoint, out int comparison)
    {
        comparison = 0;
        var other = Managed(range);
        if (!Compatible(other) || endpoint is < 0 or > 1 || targetEndpoint is < 0 or > 1)
            return Invalid;
        comparison = (endpoint == 0 ? Start : End) - (targetEndpoint == 0 ? other!.Start : other!.End);
        return Alive ? 0 : Unavailable;
    }
    int[] Boundaries(int unit)
    {
        string text = Text;
        if (unit == 0)
            return StringInfo.ParseCombiningCharacters(text).Append(text.Length).Distinct().ToArray();
        if (unit is 1 or 5 or 6)
            return new[] { 0, text.Length }.Distinct().ToArray();
        var points = new List<int> { 0 };
        for (int i = 1; i < text.Length; i++)
            if (unit == 2 ? char.IsWhiteSpace(text[i - 1]) && !char.IsWhiteSpace(text[i]) : text[i - 1] == '\n')
                points.Add(i);
        points.Add(text.Length);
        return points.Distinct().ToArray();
    }
    public int ExpandToEnclosingUnit(int unit)
    {
        if (!Alive)
            return Unavailable;
        if (unit is < 0 or > 6)
            return Invalid;
        var points = Boundaries(unit);
        int index = Array.FindLastIndex(points, point => point <= Start);
        index = Math.Min(index, Math.Max(0, points.Length - 2));
        start = points[Math.Max(0, index)];
        end = points[Math.Min(points.Length - 1, index + 1)];
        return 0;
    }
    public int FindAttribute(int attribute, UiaValue value, int backward, out nint range)
    {
        range = 0;
        if (!Alive)
            return Unavailable;
        if (attribute == 40015 && value.Type == 11 && (value.Boolean != 0) == (element.Snapshot?.Properties?.ReadOnly == true))
            range = element.TextRange(Start, End);
        return 0;
    }
    public int FindText(string text, int backward, int ignoreCase, out nint range)
    {
        range = 0;
        if (!Alive)
            return Unavailable;
        if (text.Length == 0)
            return Invalid;
        string haystack = Text[Start..End];
        var comparison = ignoreCase != 0 ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        int index = backward != 0 ? haystack.LastIndexOf(text, comparison) : haystack.IndexOf(text, comparison);
        if (index >= 0)
            range = element.TextRange(Start + index, Start + index + text.Length);
        return 0;
    }
    public int GetAttributeValue(int attribute, out UiaValue value)
    {
        value = default;
        if (!Alive)
            return Unavailable;
        if (attribute == 40015)
            value = UiaValue.Bool(element.Snapshot?.Properties?.ReadOnly == true);
        else if (element.Snapshot?.Properties?.RichValue is { } rich && attribute is 40007 or 40014 or 40026 or 40030)
        {
            int Attribute(RichText.RichAttributes a) => attribute switch
            {
                40007 => a.Bold ? 700 : 400,
                40014 => a.Italic ? 1 : 0,
                40026 => a.Strike ? 1 : 0,
                _ => a.Underline ? 1 : 0
            };
            int common = Attribute(rich.AttributesAt(Start));
            bool mixed = false;
            for (int p = rich.ParagraphAt(Start); p <= rich.ParagraphAt(End) && !mixed; p++)
            {
                int offset = rich.ParagraphStarts[p];
                foreach (var run in rich.Paragraphs[p].Runs)
                {
                    if (End > offset && Start < offset + run.Text.Length && Attribute(run.Attributes) != common)
                        mixed = true;
                    offset += run.Text.Length;
                }
            }
            if (mixed)
            {
                UiaNative.UiaGetReservedMixedAttributeValue(out var reserved);
                value = new()
                {
                    Type = 13,
                    Pointer = reserved
                };
            }
            else
                value = attribute == 40014 ? UiaValue.Bool(common != 0) : UiaValue.Number(common);
        }
        else
        {
            UiaNative.UiaGetReservedNotSupportedValue(out var reserved);
            value = new()
            {
                Type = 13,
                Pointer = reserved
            };
        }
        return 0;
    }
    public int GetBoundingRectangles(out nint array)
    {
        var boxes = Start == End || !Alive ? [] : element.TextGeometry?.Bounds(Start, End) ?? [];
        array = UiaNative.SafeArrayCreateVector(5, 0, (uint)(boxes.Length * 4));
        if (!Alive)
            return Unavailable;
        if (Start == End)
            return 0;
        var snapshot = element.Owner.Current;
        double[] values = boxes.SelectMany(rect => new double[]
        {
            snapshot.X + rect.X * snapshot.Scale, snapshot.Y + rect.Y * snapshot.Scale,
            rect.Width * snapshot.Scale, rect.Height * snapshot.Scale
        }).ToArray();
        for (int i = 0; i < values.Length; i++)
        {
            int index = i;
            double value = values[i];
            UiaNative.PutDouble(array, ref index, ref value);
        }
        return 0;
    }
    public unsafe int GetEnclosingElement(out nint enclosing)
    {
        enclosing = Alive ? (nint)ComInterfaceMarshaller<IUiaSimple>.ConvertToUnmanaged(element) : 0;
        return Alive ? 0 : Unavailable;
    }
    public int GetText(int maximum, out nint text)
    {
        text = 0;
        if (!Alive)
            return Unavailable;
        string value = Text[Start..End];
        if (maximum >= 0 && maximum < value.Length)
        {
            int limit = StringInfo.ParseCombiningCharacters(value).Append(value.Length).Last(i => i <= maximum);
            value = value[..limit];
        }
        text = Marshal.StringToBSTR(value);
        return 0;
    }
    public int Move(int unit, int count, out int moved)
    {
        moved = 0;
        if (!Alive)
            return Unavailable;
        if (unit is < 0 or > 6)
            return Invalid;
        if (count == 0)
            return 0;
        var points = Boundaries(unit);
        int from = Math.Max(0, Array.FindLastIndex(points, point => point <= Start));
        int to = (int)Math.Clamp((long)from + count, 0, Math.Max(0, points.Length - (Start == End ? 1 : 2)));
        bool collapsed = Start == End;
        moved = to - from;
        start = points[to];
        end = collapsed ? start : points[Math.Min(points.Length - 1, to + 1)];
        return 0;
    }
    public int MoveEndpointByUnit(int endpoint, int unit, int count, out int moved)
    {
        moved = 0;
        if (!Alive)
            return Unavailable;
        if (endpoint is < 0 or > 1 || unit is < 0 or > 6)
            return Invalid;
        var points = Boundaries(unit);
        int current = endpoint == 0 ? Start : End;
        int from = Math.Max(0, Array.FindLastIndex(points, point => point <= current));
        int to = (int)Math.Clamp((long)from + count, 0, points.Length - 1);
        moved = to - from;
        if (endpoint == 0)
        {
            start = points[to];
            end = Math.Max(start, End);
        }
        else
        {
            end = points[to];
            start = Math.Min(Start, end);
        }
        return 0;
    }
    public int MoveEndpointByRange(int endpoint, nint range, int targetEndpoint)
    {
        var other = Managed(range);
        if (!Compatible(other) || endpoint is < 0 or > 1 || targetEndpoint is < 0 or > 1)
            return Invalid;
        int position = targetEndpoint == 0 ? other!.Start : other!.End;
        if (endpoint == 0)
        {
            start = position;
            end = Math.Max(position, End);
        }
        else
        {
            end = position;
            start = Math.Min(Start, position);
        }
        return Alive ? 0 : Unavailable;
    }
    public int Select() => Alive ? element.Action(new(AccessibilityAction.SetSelection, Start: Start, End: End)) : Unavailable;
    public int AddToSelection() => Select();
    public int RemoveFromSelection() => Alive ? element.Action(new(AccessibilityAction.SetSelection, Start: Start, End: Start)) : Unavailable;
    public int ScrollIntoView(int alignTop) => Alive ? element.Action(new(AccessibilityAction.Focus)) : Unavailable;
    public int GetChildren(out nint array)
    {
        array = UiaNative.SafeArrayCreateVector(13, 0, 0);
        return Alive ? 0 : Unavailable;
    }
}
