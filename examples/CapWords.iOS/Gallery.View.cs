using Kumiko.iOS;
using Kumiko.UI;
using static AnimationGalleryApp;

internal sealed partial class GalleryHomeScreen(GalleryHomeProps props) : Kumiko.Runtime.Screen<GalleryHomeProps, GalleryMessage>(props)
{
    protected override void Render()
    {
        var ui = Ui;
        var c = ui.Context;
        using var background = c.Div(key: "gallery").SizeFull().Background(Paper).ItemsCenter();
        using var scroll = c.Div(key: "gallery-scroll").FillWidth().MaxWidth(600).FillHeight().Scroll();
        using var content = c.Div(key: "gallery-content").FillWidth().Padding(new Edges(24, 20, 28, 20)).Gap(18);
        using (c.Div(key: "gallery-heading").FillWidth().Gap(8).Padding(LayoutEdge.Bottom, 10))
        {
            using (c.Div(key: "title-row").Row().FillWidth().ItemsCenter())
            {
                c.NativeLabel("Motion studies", new(Size: c.Size.Width < 360 ? 32 : 36, Weight: Weight.Semibold, Color: Ink),
                    NativeFontDesign.Serif, key: "gallery-title");
                c.Spacer();
                c.NativeSymbol("sparkle", Color.Hex(0x78874e), 23, key: "gallery-spark");
            }
            c.NativeLabel("A small collection of things in motion.", new(Size: 15, Color: Muted), key: "gallery-subtitle");
        }
        WordCard();
        AlmaCard();
        SlackCard();
        OpalCard();
        UnwindCard();
        c.NativeLabel("Built with Kumiko · Native C#", new(Size: 12, Color: Muted), key: "gallery-credit");
    }

    void WordCard()
    {
        var words = Model.Words;
        var ui = Ui;
        var c = ui.Context;
        using var button = ui.NativePressable("Open CapWords", new GalleryMessage.Open(GalleryScreen.CapWords), haptics: true,
            key: "open-capwords").FillWidth().Height(252);
        using var card = c.Div(key: "word-card").SizeFull().Background(Color.Hex(0xfffdf7)).Rounded(26).Clip();
        using (c.Div(key: "word-preview").FillWidth().Height(156).Row().Center().Gap(20).Background(Color.Hex(0xf7f3e9)))
        {
            if (!words.IsDefaultOrEmpty)
                foreach (var word in words)
                {
                    using var image = c.Div(key: word.Id).Width(114).Height(124).Center();
                    c.NativeImage(word.Image, 114, 114, key: "sticker");
                }
            else
                c.NativeLabel("CapWords", new(Size: 27, Weight: Weight.Semibold, Color: Ink), NativeFontDesign.Serif, key: "preview-placeholder");
        }
        Footer("CapWords", "A little word, a shared journey.", Ink, "word-footer");
    }

    void AlmaCard()
    {
        var ui = Ui;
        var c = ui.Context;
        using var button = ui.NativePressable("Open Alma", new GalleryMessage.Open(GalleryScreen.Alma), haptics: true,
            key: "open-alma").FillWidth().Height(252);
        using var card = c.Div(key: "alma-card").SizeFull().Background(Color.Hex(0x304b2a)).Rounded(26).Clip();
        using (c.Div(key: "alma-preview").FillWidth().Height(156).Background(Color.Hex(0xdce6c5)))
        using (c.NativeCircularCarousel(AlmaApp.Items, 1, preview: true, key: "alma-preview-orbit"))
        {
        }
        Footer("Alma", "Good things come around.", Color.Hex(0xf2f5e8), "alma-footer");
    }

    void SlackCard()
    {
        var ui = Ui;
        var c = ui.Context;
        using var button = ui.NativePressable("Open Slack Catch Up", new GalleryMessage.Open(GalleryScreen.Slack), haptics: true,
            key: "open-slack").FillWidth().Height(252);
        using var card = c.Div(key: "slack-card").SizeFull().Background(Color.Hex(0x30102f)).Rounded(26).Clip();
        using (c.NativeLinearGradient(Color.Hex(0x542051), Color.Hex(0x20031f), key: "slack-preview").FillWidth().Height(156))
            SlackApp.Preview(c);
        Footer("Slack", "A little swipe. A clear head.", Color.Hex(0xf4e8f3), "slack-footer");
    }

    void OpalCard()
    {
        var ui = Ui;
        var c = ui.Context;
        using var button = ui.NativePressable("Open Opal", new GalleryMessage.Open(GalleryScreen.Opal), haptics: true,
            key: "open-opal").FillWidth().Height(252);
        using var card = c.Div(key: "opal-card").SizeFull().Background(Color.Hex(0x102a24)).Rounded(26).Clip();
        using (c.Div(key: "opal-preview-surface").FillWidth().Height(156).Background(Color.Black))
            OpalApp.Preview(c);
        Footer("Opal", "A little time. Room to focus.", Color.Hex(0xe1f9f0), "opal-footer");
    }

    void UnwindCard()
    {
        var ui = Ui;
        var c = ui.Context;
        using var button = ui.NativePressable("Open Unwind", new GalleryMessage.Open(GalleryScreen.Unwind), haptics: true,
            key: "open-unwind").FillWidth().Height(252);
        using var card = c.Div(key: "unwind-card").SizeFull().Background(Color.Hex(0x4a2416)).Rounded(26).Clip();
        using (c.Div(key: "unwind-preview").FillWidth().Height(156).Background(UnwindApp.Paper).Row().Center().Gap(30))
        {
            c.NativeOrb(Color.Hex(0xb6a5f2), 42, glow: .5f, key: "preview-sleep");
            c.NativeOrb(Color.Hex(0xec7a3c), 62, glow: .9f, key: "preview-breathe");
            c.NativeOrb(Color.Hex(0xa8d48a), 42, glow: .5f, key: "preview-outside");
        }
        Footer("Unwind", "Arc cards that settle into a menu.", Color.Hex(0xfbe7da), "unwind-footer");
    }

    void Footer(string title, string description, Color color, string key)
    {
        var c = C;
        using var footer = c.Div(key: key).Row().FillWidth().Grow().Padding(new Edges(14, 20, 16, 20)).ItemsCenter().Gap(12);
        using (c.Div(key: "copy").Flex().Gap(4))
        {
            c.NativeLabel(title, new(Size: 25, Weight: Weight.Semibold, Color: color), NativeFontDesign.Serif, key: "card-title");
            c.NativeLabel(description, new(Size: 13, Color: color), key: "card-description");
        }
        c.NativeSymbol("arrow.up.right", color, 22, key: "open-icon");
    }
}

internal sealed partial class AnimationGalleryApp
{
    internal static Color Paper => Color.Hex(0xf1f1eb);
    internal static Color Ink => Color.Hex(0x252b24);
    internal static Color Muted => Color.Hex(0x687064);
}
