using Kumiko.UI;
using static CapWordsApp;

internal sealed partial class CapWordsScreen
{
    void Dots()
    {
        var c = C;
        using var background = c.Div(key: "dotted-paper").Absolute().SizeFull().Clip();
        var columns = (int)Math.Ceiling(c.Size.Width / Palette.DotSpacing);
        var rows = (int)Math.Ceiling(c.Size.Height / Palette.DotSpacing);
        for (var row = 0; row < rows; row++)
        {
            using var band = c.Div(key: row).Absolute().Offset(0, row * Palette.DotSpacing).FillWidth().Height(2);
            for (var column = 0; column < columns; column++)
            {
                using var dot = c.Div(key: column).Absolute().Offset(column * Palette.DotSpacing, 0)
                    .Width(2).Height(2).Rounded(1).Background(Palette.Dot);
            }
        }
    }

    void Aperture()
    {
        var c = C;
        using var aperture = c.Div(key: "aperture").Width(64).Height(64).Rounded(32).Clip();
        using (c.Div(key: "yellow").Absolute().Offset(32, 0).Width(32).Height(32).Background(Color.Hex(0xfacc15)))
        {
        }
        using (c.Div(key: "pink").Absolute().Offset(32, 16).Width(32).Height(32).Background(Color.Hex(0xf9a8d4)))
        {
        }
        using (c.Div(key: "blue").Absolute().Offset(32, 32).Width(32).Height(32).Background(Color.Hex(0x93c5fd)))
        {
        }
        using (c.Div(key: "green").Absolute().Offset(0, 32).Width(32).Height(32).Background(Color.Hex(0x86efac)))
        {
        }
        using (c.Div(key: "purple").Absolute().Offset(0, 16).Width(32).Height(32).Background(Color.Hex(0xd8b4fe)))
        {
        }
        using (c.Div(key: "center").Absolute().Offset(12, 12).Width(40).Height(40).Rounded(20).Background(Color.White))
        {
        }
    }
}
