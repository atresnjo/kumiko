using System.Collections.Immutable;
using Kumiko.iOS;
using Kumiko.UI;

internal sealed partial class CapWordsApp
{
    static ImmutableArray<Word> LoadWords() =>
    [
        new("computer-mouse-feb-09", "Computer Mouse", "/kəm'pju:.tə maʊs/", Load("mouse")),
        new("ice-cream-cone-feb-09", "Ice Cream Cone", "/'aıs ,kri:m koʊn/", Load("ice-cream"))
    ];

    static ImmutableArray<byte> Load(string name)
    {
        using var stream = typeof(CapWordsApp).Assembly.GetManifestResourceStream($"CapWords.{name}.png")
            ?? throw new InvalidOperationException("Missing licensed reference artwork.");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return ImmutableArray.Create(memory.ToArray());
    }

    internal static class Palette
    {
        internal static Color Paper => Color.Hex(0xf8f8f8);
        internal static Color Detail => Color.Hex(0xf4f4f3);
        internal static Color Ink => Color.Hex(0x333333);
        internal static Color Muted => Color.Hex(0x6b7280);
        internal static Color Dot => Color.Hex(0xd0d0d0);
        internal static Color Orange => Color.Hex(0xfb923c);
        internal static NativePalette Native => new(Paper, Color.Hex(0xfefeff), Color.White,
            Ink, Muted, Orange, Color.Hex(0xc5a282), Dot);
        internal const float HeaderInset = 28;
        internal const float HeaderHeight = 56;
        internal const float DetailTop = 48;
        internal const float CardFraction = .35f;
        internal const float HeroFraction = .5f;
        internal const float DotSpacing = 20;
    }
}
