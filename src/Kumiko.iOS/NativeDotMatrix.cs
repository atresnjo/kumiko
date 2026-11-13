using CoreAnimation;
using CoreGraphics;
using CoreText;
using Foundation;
using UIKit;

namespace Kumiko.iOS;

/// <summary>
/// Dot-matrix numerals: light monospaced glyph outlines stroked with round zero-length dashes, so each stroke
/// reads as a double row of dots. The layer's frame matches the typographic box at the given size.
/// </summary>
internal sealed class NativeDotMatrixLayer : CAShapeLayer
{
    string text = "";
    nfloat size;

    internal NativeDotMatrixLayer()
    {
        FillColor = null;
        LineCap = CAShapeLayer.CapRound;
        LineJoin = CAShapeLayer.JoinRound;
        ContentsScale = UIScreen.MainScreen.Scale;
    }

    /// <summary>The typographic width of the text, and of its first <paramref name="prefix"/> characters.</summary>
    internal (nfloat Width, nfloat Prefix) Configure(string value, nfloat points, int prefix = 0)
    {
        text = value;
        size = points;
        using var font = UIFont.GetMonospacedSystemFont(points, UIFontWeight.Light);
        var attributes = new UIStringAttributes { Font = font };
        using var attributed = new NSAttributedString(value, attributes);
        using var line = new CTLine(attributed);
        using var path = new CGPath();
        var ascent = font.Ascender;
        nfloat prefixWidth = 0;
        foreach (var run in line.GetGlyphRuns())
        {
            using (run)
            {
                var runFont = run.GetAttributes()?.Font;
                if (runFont is null)
                    continue;
                var glyphs = run.GetGlyphs();
                var positions = run.GetPositions();
                var range = run.StringRange;
                for (var i = 0; i < glyphs.Length; i++)
                {
                    var transform = new CGAffineTransform(1, 0, 0, -1, positions[i].X, ascent);
                    using var glyph = runFont.GetPathForGlyph(glyphs[i], ref transform);
                    if (glyph is not null)
                        path.AddPath(glyph);
                    if (range.Location + i == prefix)
                        prefixWidth = positions[i].X;
                }
            }
        }
        var width = (nfloat)line.GetTypographicBounds(out _, out _, out _);
        if (prefix >= value.Length)
            prefixWidth = width;
        Path = path;
        LineWidth = (nfloat)Math.Max(1, points * .05);
        LineDashPattern = [NSNumber.FromDouble(0), NSNumber.FromDouble(points * .07)];
        Bounds = new CGRect(0, 0, width, ascent - font.Descender);
        return (width, prefixWidth);
    }

    /// <summary>Distance from the layer's top to the vertical centre of a numeral's cap height.</summary>
    internal nfloat CapCenter
    {
        get
        {
            using var font = UIFont.GetMonospacedSystemFont(size, UIFontWeight.Light);
            return font.Ascender - font.CapHeight / 2;
        }
    }

    internal string Text => text;
}
