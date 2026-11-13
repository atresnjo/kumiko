using System.Globalization;
using Kumiko.UI;

namespace Kumiko.Rendering;

public sealed partial class TextSystem
{
    /// <summary>Returns the visual x coordinate of a UTF-16 caret in a shaped line.</summary>
    public float CaretPosition(string text, TextStyle style, int position)
    {
        position = Snap(text, position);
        var run = Shape(text, style);
        if (run.Native is { } nativeLayout)
            return nativeLayout.Caret(position);
        if (mac is not null && text.Any(ch => ch > 127))
            return mac.Caret(text, Normalize(style), style.Mono ? MonoFamily : Family, position);
        return Shape(text[..position], style).Width;
    }

    /// <summary>Maps a visual x coordinate to a complete grapheme boundary.</summary>
    public int HitTest(string text, TextStyle style, float x)
    {
        var run = Shape(text, style);
        if (run.Native is { } nativeLayout)
            return Snap(text, nativeLayout.HitTest(x));
        if (mac is not null && text.Any(ch => ch > 127))
            return Snap(text, mac.HitTest(text, Normalize(style), style.Mono ? MonoFamily : Family, x));
        var positions = StringInfo.ParseCombiningCharacters(text).Append(text.Length).ToArray();
        int closest = 0;
        float distance = float.PositiveInfinity;
        foreach (int position in positions)
        {
            float delta = Math.Abs(CaretPosition(text, style, position) - x);
            if (delta < distance)
            {
                distance = delta;
                closest = position;
            }
        }
        return closest;
    }

    static int Snap(string text, int position)
    {
        position = Math.Clamp(position, 0, text.Length);
        if (position == text.Length)
            return position;
        int result = 0;
        foreach (int boundary in StringInfo.ParseCombiningCharacters(text))
        {
            if (boundary > position)
                break;
            result = boundary;
        }
        return result;
    }

    internal int MoveVisual(string text, TextStyle style, int position, int direction)
    {
        float x = CaretPosition(text, style, position), distance = float.PositiveInfinity;
        int next = position;
        foreach (int boundary in StringInfo.ParseCombiningCharacters(text).Append(text.Length))
        {
            float delta = (CaretPosition(text, style, boundary) - x) * direction;
            if (delta > .01f && delta < distance)
            {
                distance = delta;
                next = boundary;
            }
        }
        return next;
    }
}
