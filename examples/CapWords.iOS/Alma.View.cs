using Kumiko.iOS;
using Kumiko.UI;
using static AlmaApp;

internal sealed partial class AlmaScreen(AlmaModel model) : Kumiko.Runtime.Screen<AlmaModel, AlmaMessage>(model)
{
    protected override void Render()
    {
        var ui = Ui;
        var model = Model;
        var c = ui.Context;
        using var page = c.Div(key: "alma").SizeFull().Background(Paper).ItemsCenter();
        var width = Math.Min(c.Size.Width, 600);
        using (c.Div(key: "alma-content").Width(width).FillHeight().Padding(new Edges(12, 24, 12, 24)))
        {
            using (c.Div(key: "header").Row().FillWidth().Height(48).ItemsCenter())
            {
                Icon("All examples", "arrow.left", new AlmaMessage.Back(), "alma-back");
                c.Spacer();
                c.NativeLabel("alma", new(Size: 25, Weight: Weight.Semibold, Color: Forest), NativeFontDesign.Serif, key: "alma-wordmark");
                c.Spacer();
                using var toggle = ui.NativePressable(model.Playing ? "Pause carousel" : "Play carousel", new AlmaMessage.TogglePlayback(),
                    key: "alma-playback").Width(44).Height(44).Center();
                c.NativeSymbol(model.Playing ? "pause" : "play", Forest, 19, key: "playback-icon");
            }
            using (c.Div(key: "heading").FillWidth().ItemsCenter().Padding(LayoutEdge.Top, 26))
            {
                c.NativeLabel("Track nutrients towards\nthe goals you care about", new(Size: 24, Color: Ink, Wrap: true),
                    NativeFontDesign.Serif, key: "alma-heading");
            }
            c.Spacer();
            using (c.Div(key: "carousel-controls").Row().FillWidth().ItemsCenter().Justify(Justify.Center).Gap(16).Margin(LayoutEdge.Bottom, 16))
            {
                Icon("Previous nutrient", "chevron.left", new AlmaMessage.Previous(), "alma-previous");
                using (c.Div(key: "position").Width(90).Center())
                    c.NativeLabel($"{model.Index + 1} / {Items.Length}", new(Size: 13, Weight: Weight.Medium, Color: Forest), key: "alma-position");
                Icon("Next nutrient", "chevron.right", new AlmaMessage.Next(), "alma-next");
            }
            using (ui.NativePressable("Continue", new AlmaMessage.Continue(), key: "alma-continue").FillWidth().Height(56))
            using (c.Div(key: "continue-surface").SizeFull().Background(Forest).Rounded(19).Center())
                c.NativeLabel("Continue", new(Size: 18, Weight: Weight.Medium, Color: Color.White), key: "continue-label");
        }
        using (ui.NativeCircularCarousel(Items, model.Index, index => new AlmaMessage.Select(index), key: "nutrients-orbit")
            .Absolute().SizeFull())
        {
        }
        if (model.Notice)
        {
            using var drawer = ui.NativeBottomDrawer("Alma study", new AlmaMessage.CloseNotice(),
                palette: CapWordsApp.Palette.Native, key: "alma-notice");
            c.NativeLabel("The reference demonstrates the nutrient carousel. Account setup and nutrition tracking are not connected.",
                new(Size: 17, Color: Ink, Wrap: true), key: "alma-notice-copy");
        }
    }

    void Icon(string label, string symbol, AlmaMessage message, string key)
    {
        var ui = Ui;
        using var button = ui.NativePressable(label, message, haptics: true, key: key).Width(44).Height(44).Center();
        ui.Context.NativeSymbol(symbol, Forest, 20, key: "symbol");
    }
}

internal sealed partial class AlmaApp
{
    internal static Color Paper => Color.Hex(0xf7f5ed);
    internal static Color Forest => Color.Hex(0x3c5627);
    internal static Color Ink => Color.Hex(0x25291f);
}
