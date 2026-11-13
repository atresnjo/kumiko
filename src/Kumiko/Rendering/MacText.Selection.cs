using System.Runtime.InteropServices;
using Kumiko.Platform;
using Kumiko.UI;

namespace Kumiko.Rendering;

internal sealed unsafe partial class MacText
{
    internal Rect[] Selection(string text, TextStyle style, string? family, int start, int end)
    {
        using var pool = new AutoreleasePool();
        nint line = CreateLine(text, style, family);
        try
        {
            var result = new List<Rect>();
            nint runs = CTLineGetGlyphRuns(line);
            for (nint i = 0; i < CFArrayGetCount(runs); i++)
            {
                nint run = CFArrayGetValueAtIndex(runs, i);
                var range = CTRunGetStringRange(run);
                int from = Math.Max(start, (int)range.Location), to = Math.Min(end, (int)(range.Location + range.Length));
                int count = checked((int)CTRunGetGlyphCount(run));
                if (from >= to || count == 0)
                    continue;
                var positions = new NativePoint[count];
                fixed (NativePoint* pointer = positions)
                    CTRunGetPositions(run, default, pointer);
                double left = positions.Min(p => p.X);
                double right = left + CTRunGetTypographicBounds(run, default, null, null, null);
                bool rtl = (CTRunGetStatus(run) & 1) != 0;
                double Offset(int position)
                {
                    double secondary;
                    double primary = CTLineGetOffsetForStringIndex(line, position, &secondary);
                    return primary >= left - .01 && primary <= right + .01 ? primary : secondary;
                }
                double a = from == range.Location ? rtl ? right : left : Offset(from);
                double b = to == range.Location + range.Length ? rtl ? left : right : Offset(to);
                result.Add(new((float)Math.Min(a, b), 0, (float)Math.Abs(b - a), LineHeight(Font(style, family), style.Leading)));
            }
            return result.ToArray();
        }
        finally
        {
            CFRelease(line);
        }
    }
    [DllImport(CT)]
    static extern Range CTRunGetStringRange(nint run);
    [DllImport(CT)]
    static extern uint CTRunGetStatus(nint run);
    [DllImport(CT)]
    static extern double CTRunGetTypographicBounds(nint run, Range range, double* ascent, double* descent, double* leading);
}
