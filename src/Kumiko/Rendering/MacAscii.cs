using Kumiko.UI;

namespace Kumiko.Rendering;

internal sealed class MacAscii(MacText owner, TextStyle style, string? family)
{
    readonly ShapedRun?[] chars = new ShapedRun?[95];
    readonly Dictionary<ushort, (float Kern, bool Complex)> pairs = new();
    nint runFont;
    ShapedRun? Character(char value)
    {
        int index = value - 32;
        if (chars[index] is { } cached)
            return cached;
        var shaped = owner.ShapeNative(value.ToString(), style, family);
        if (shaped.Glyphs.Length != 1)
            return null;
        if (runFont == 0)
            runFont = shaped.Glyphs[0].Font;
        if (shaped.Glyphs[0].Font != runFont)
            return null;
        chars[index] = shaped;
        return shaped;
    }

    internal ShapedRun? Shape(string text)
    {
        foreach (char ch in text)
            if (ch < 32 || ch >= 127)
                return null;
        var glyphs = new ShapedGlyph[text.Length];
        float x = 0, height = 0, ascent = 0;
        for (int i = 0; i < text.Length; i++)
        {
            var character = Character(text[i]);
            if (character is null)
                return null;
            height = character.Height;
            ascent = character.Ascent;
            if (i > 0)
            {
                var key = (ushort)((text[i - 1] << 8) | text[i]);
                if (!pairs.TryGetValue(key, out var pair))
                {
                    var previous = Character(text[i - 1])!;
                    var native = owner.ShapeNative(text.Substring(i - 1, 2), style, family);
                    bool complex = native.Glyphs.Length != 2 || native.Glyphs[0].Glyph != previous.Glyphs[0].Glyph || native.Glyphs[1].Glyph != character.Glyphs[0].Glyph || native.Glyphs.Any(g => g.Font != runFont);
                    pair = (complex ? 0 : native.Glyphs[1].X - previous.Width, complex);
                    pairs[key] = pair;
                }

                if (pair.Complex)
                    return null;
                x += pair.Kern;
            }

            glyphs[i] = new(runFont, character.Glyphs[0].Glyph, x, 0);
            x += character.Width;
        }

        if (text.Length == 0)
        {
            var empty = owner.ShapeNative("", style, family);
            height = empty.Height;
            ascent = empty.Ascent;
        }

        return new(x, height, ascent, glyphs);
    }
}
