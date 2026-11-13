using Kumiko.iOS;
using Kumiko.UI;
using static CapWordsApp;

internal sealed partial class CapWordsScreen(CapWordsModel model) : Kumiko.Runtime.Screen<CapWordsModel, CapWordsMessage>(model)
{
    protected override void Render()
    {
        var ui = Ui;
        var model = Model;
        var c = ui.Context;
        using var page = c.Div(key: "page").SizeFull().Background(model.SelectedId is null ? Palette.Paper : Palette.Detail);
        var width = Math.Min(c.Size.Width, 600);
        if (model.Words.IsDefaultOrEmpty)
        {
            c.NativeLabel(model.Error ?? "Loading word artwork…", new(Size: 17, Color: Palette.Ink), key: "loading");
            return;
        }
        if (model.SelectedId is null)
            Grid(width);
        else
            Detail(model.Words.Single(word => word.Id == model.SelectedId), width);
        if (model.Notice is { } notice)
        {
            using var drawer = ui.NativeBottomDrawer("Reference demo", new CapWordsMessage.DismissNotice(),
                palette: Palette.Native, key: "notice");
            c.NativeLabel(notice, new(Size: 17, Color: Palette.Ink), key: "notice-copy");
        }
    }

    void Grid(float width)
    {
        var ui = Ui;
        var model = Model;
        var c = ui.Context;
        Dots();
        using var gridPage = c.Div(key: "grid").FillWidth().FillHeight().ItemsCenter();
        using (c.Div(key: "grid-content").Width(width).FillHeight().Py(12))
        {
            using (c.Div(key: "navigation").Row().FillWidth().Height(Palette.HeaderHeight).Px(16).ItemsCenter())
            {
                IconButton("All examples", "chevron.left", new CapWordsMessage.Gallery(), "collection-back");
                c.Spacer();
                using (ui.NativePressable("Welcome! Save 40%", new CapWordsMessage.Notice("The offer is decorative in the original reference."),
                    key: "offer").Height(44))
                using (c.Div(key: "offer-surface").Row().ItemsCenter().Gap(8).Px(12).Height(44)
                    .Background(Palette.Native.Warm).Rounded(22))
                {
                    c.NativeSymbol("gift", Color.White, 16, key: "gift");
                    c.NativeLabel("Welcome! Save 40%", new(Size: 12, Weight: Weight.Semibold, Color: Color.White), key: "offer-label");
                    c.NativeSymbol("chevron.right", Color.White, 14, key: "offer-chevron");
                }
            }
            using (c.Div(key: "grid-heading").FillWidth().Px(Palette.HeaderInset).Padding(LayoutEdge.Bottom, 16))
                Date();
            using (c.Div(key: "words").Row().FillWidth().Wrap())
            {
                foreach (var word in model.Words)
                {
                    using var cell = c.Div(key: word.Id).Width(width / 2).Height(width / 2).Center();
                    using var button = ui.NativePressable(word.Name, new CapWordsMessage.Open(word.Id), haptics: true, key: "open-word").Center();
                    Hero(word, width * Palette.CardFraction);
                }
            }
            c.Spacer();
            using (c.Div(key: "capture-position").FillWidth().ItemsCenter().Padding(LayoutEdge.Bottom, 32))
            using (ui.NativePressable("Capture a word", new CapWordsMessage.Notice("Camera capture is a placeholder in the reference."),
                key: "capture").Width(80).Height(80).Center())
            using (c.Div(key: "capture-surface").Width(80).Height(80).Rounded(40).Background(Color.White).Center())
            {
                Aperture();
            }
        }
    }

    void Detail(Word word, float width)
    {
        var ui = Ui;
        var c = ui.Context;
        using var detail = c.Div(key: "detail").SizeFull().ItemsCenter();
        using var content = c.Div(key: "detail-content").Width(width).FillHeight().Py(12);
        using (c.Div(key: "navigation").Row().FillWidth().Px(16).ItemsCenter().Height(76))
        {
            IconButton("Back", "chevron.left", new CapWordsMessage.Back(), "back", haptics: true);
            using (c.Div(key: "detail-heading").Flex().ItemsCenter())
                Date();
            IconButton("More options", "ellipsis", new CapWordsMessage.Notice("More options are a placeholder in the reference."), "more");
        }
        using (c.Div(key: "word-content").FillWidth().ItemsCenter().Padding(LayoutEdge.Top, Palette.DetailTop))
        {
            Hero(word, width * Palette.HeroFraction);
            using (c.Div(key: "word-name").Enter(Arrival(.1f)).Margin(LayoutEdge.Bottom, 4))
                c.NativeLabel(word.Name, new(Size: 18, Color: Palette.Muted), key: "name");
            using (c.Div(key: "pronunciation").Row().ItemsCenter().Gap(8).Enter(Arrival(.2f)))
            {
                c.NativeLabel(word.Phonetic, new(Size: 18, Color: Palette.Muted), key: "phonetic");
                IconButton("Pronounce " + word.Name, "speaker.wave.2", new CapWordsMessage.Notice("Pronunciation playback is a placeholder in the reference."), "pronounce");
            }
        }
        c.Spacer();
        using (c.Div(key: "examples-position").FillWidth().ItemsCenter().Padding(LayoutEdge.Bottom, 16).Enter(Arrival(.3f)))
        using (ui.NativePressable("View Examples", new CapWordsMessage.Notice("Examples are locked in the reference; no purchase flow is connected."),
            key: "examples").Width(236).Height(56).Center())
        using (c.Div(key: "examples-surface").Row().Width(236).Height(56).Center().Gap(8)
            .Background(Color.White).Rounded(28))
        {
            c.NativeSymbol("lock", Palette.Orange, 20, key: "lock");
            c.NativeLabel("View Examples", new(Size: 16, Weight: Weight.Semibold, Color: Palette.Orange), key: "examples-label");
        }
    }

    static Transition Arrival(float delay) => new(Y: 50, Scale: .8f) { Delay = delay };

    void Hero(Word word, float size)
    {
        var c = C;
        using var hero = c.Div(key: "hero").Width(size).Height(size).SharedLayout("main-word-card" + word.Id);
        c.NativeImage(word.Image, size, size, key: "artwork");
    }

    void Date()
    {
        var c = C;
        using (c.Div(key: "date-shared").SharedLayout("date-and-number-of-words-date"))
            c.NativeLabel("Feb 09", new(Size: 30, Weight: Weight.Medium, Color: Palette.Ink), NativeFontDesign.Serif, key: "date");
        using (c.Div(key: "count-shared").SharedLayout("date-and-number-of-words-word-count"))
            c.NativeLabel("2 Words", new(Size: 16, Color: Color.Hex(0x4b5563)), key: "word-count");
    }

    void IconButton(string label, string symbol, CapWordsMessage message, string key, bool haptics = false)
    {
        var ui = Ui;
        using var button = ui.NativePressable(label, message, haptics: haptics, key: key).Width(44).Height(44).Center();
        ui.Context.NativeSymbol(symbol, Palette.Ink, 24, key: "symbol");
    }
}
