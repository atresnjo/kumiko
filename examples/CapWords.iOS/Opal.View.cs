using Kumiko.iOS;
using Kumiko.UI;
using static OpalApp;

internal sealed partial class OpalView(OpalModel model) : Kumiko.Runtime.View<OpalModel, OpalMessage>(model)
{
    protected override void Render()
    {
        var ui = Ui;
        var model = Model;
        var c = ui.Context;
        using var page = c.Div(key: "opal").SizeFull().Background(Color.Black).ItemsCenter();
        using (c.Div(key: "opal-content").FillWidth().MaxWidth(600).FillHeight().Padding(new Edges(8, 12, 0, 12)))
        {
            using (c.Div(key: "opal-header").Row().FillWidth().Height(48).ItemsCenter())
            {
                Icon("All examples", "arrow.left", new OpalMessage.Back(), Color.White, "opal-back");
                c.Spacer();
                c.NativeLabel("Opal", new(Size: 21, Weight: Weight.Medium, Color: Color.White), key: "opal-wordmark");
                c.Spacer();
                using var balance = c.Div(key: "header-balance").Width(44).Height(44);
            }
            c.Spacer();
            TimerControls();
            using (c.Div(key: "timer-gap").Height(20))
            {
            }
            ui.NativeAction("Start Timer", new OpalMessage.Explain("Start Timer"),
                new(Symbol: "play.fill", Style: NativeActionStyle.Glow, Height: 58, FillWidth: true), Palette, key: "opal-start");
            using (c.Div(key: "tabs-gap").Height(16))
            {
            }
            Tabs();
        }
        if (model.Notice is { } title)
        {
            using var drawer = ui.NativeBottomDrawer(title, new OpalMessage.CloseNotice(), palette: Palette, key: "opal-notice");
            c.NativeLabel(title == "Start Timer" ? $"Your selected duration is {Steps[model.Index].Label}. This study demonstrates the timer controls; app blocking and a running focus session are not connected."
                : "This motion study follows the reference timer controls. App blocking, schedules, screen-time data and accounts are not connected.",
                new(Size: 17, Color: Color.White, Wrap: true), key: "opal-notice-copy");
        }
    }

    void TimerControls()
    {
        var ui = Ui;
        var model = Model;
        var c = ui.Context;
        var width = Math.Min(c.Size.Width, 600) - 24;
        var valueWidth = Math.Min(c.Size.Width, 600) * .22f;
        var sliderWidth = Math.Min(Math.Min(c.Size.Width, 600) * .71f, width - valueWidth - 6);
        using var row = c.Div(key: "opal-timer").FillWidth().Height(44);
        using (c.NativePresentation(new(Opacity: model.Expanded ? 0 : 1, Interactive: !model.Expanded), PresentationSpring,
            key: "opal-decrement-pose").Absolute().Width(44).Height(44))
            StepButton("Decrease duration", "minus", -1, "opal-minus");
        using (c.NativePresentation(new(X: model.Expanded ? -46 : 0), new(1400, 90, .65f),
            key: "opal-value-pose").Absolute().Offset(48, 0).Width(valueWidth).Height(44))
        using (ui.NativePressable(model.Expanded ? "Close duration slider" : "Open duration slider", _ => new OpalMessage.Toggle(),
            pressedScale: .95f, haptics: true, pressOnTouchDown: true, key: "opal-value").SizeFull().Center())
        using (c.Div(key: "value-surface").Width(valueWidth).Height(40).Background(Color.White).Rounded(16).Center())
            c.NativeLabel(Value(model.Index), new(Size: 16, Weight: Weight.Medium, Color: Color.Black), key: "opal-duration");
        using (c.NativePresentation(new(Opacity: model.Expanded ? 0 : 1, Interactive: !model.Expanded), PresentationSpring,
            key: "opal-increment-pose").Absolute().Offset(52 + valueWidth, 0).Width(44).Height(44))
            StepButton("Increase duration", "plus", 1, "opal-plus");
        var blockLeft = 112 + valueWidth;
        using (c.NativePresentation(new(ScaleX: model.Expanded ? .8f : 1, ScaleY: model.Expanded ? .8f : 1,
            Opacity: model.Expanded ? 0 : 1, Interactive: !model.Expanded), PresentationSpring,
            key: "opal-block-pose").Absolute().Offset(blockLeft, 0).Width(Math.Max(44, width - blockLeft)).Height(44))
        using (ui.NativePressable("Block", new OpalMessage.Explain("Block"), enabled: !model.Expanded,
            haptics: true, key: "opal-block").SizeFull().Center())
        using (c.Div(key: "block-surface").FillWidth().Height(40).Row().Center().Gap(12).Background(Color.Hex(0x252525)).Border(Border, .5f).Rounded(20))
        {
            c.NativeLabel("Block", new(Size: 18, Color: Color.White), key: "block-label");
            c.NativeSymbol("ellipsis.bubble.fill", Color.White, 20, key: "block-symbol");
        }
        using (c.NativePresentation(new(ScaleX: model.Expanded ? 1 : .75f, ScaleY: model.Expanded ? 1 : .75f,
            Opacity: model.Expanded ? 1 : 0, Interactive: model.Expanded, FadeDelay: model.Expanded ? .144f : 0),
            PresentationSpring, key: "opal-slider-pose").Absolute().Offset(width - sliderWidth, 0).Width(sliderWidth).Height(44))
            ui.NativeRubberSlider("Timer duration", Steps, Steps[model.Index].Id, id => new OpalMessage.Select(id),
                minimumIndex: 1, disabled: !model.Expanded, key: "opal-slider");
    }

    void StepButton(string label, string symbol, int direction, string key)
    {
        var ui = Ui;
        var model = Model;
        var c = ui.Context;
        using var button = ui.NativePressable(label, time => new OpalMessage.Step(direction, time), enabled: !model.Expanded, haptics: true, key: key).SizeFull().Center();
        using var surface = c.Div(key: "step-surface").Width(40).Height(40).Background(Color.Hex(0x252525)).Border(Border, .5f).Rounded(20).Center();
        c.NativeSymbol(symbol, Color.Hex(0xf5f5f5), 16, key: "symbol");
    }

    void Tabs()
    {
        var ui = Ui;
        var c = ui.Context;
        using var tabs = c.Div(key: "opal-tabs").Row().FillWidth().Height(56).Justify(Justify.SpaceAround).ItemsCenter();
        Icon("Home", "house.fill", new OpalMessage.Explain("Opal Home"), Color.White, "opal-home");
        Icon("Blocks", "folder.badge.plus", new OpalMessage.Explain("Blocks"), Muted, "opal-blocks");
        Icon("Screen Time", "chart.bar.fill", new OpalMessage.Explain("Screen Time"), Muted, "opal-screen-time");
        Icon("Leaderboard", "trophy.fill", new OpalMessage.Explain("Leaderboard"), Muted, "opal-leaderboard");
        Icon("Account", "person.fill", new OpalMessage.Explain("Account"), Muted, "opal-account");
    }

    void Icon(string label, string symbol, OpalMessage message, Color color, string key)
    {
        var ui = Ui;
        using var button = ui.NativePressable(label, message, haptics: true, key: key).Width(44).Height(44).Center();
        ui.Context.NativeSymbol(symbol, color, 22, key: "symbol");
    }

}

internal sealed partial class OpalApp
{
    internal static Color Mint => Color.Hex(0x99f6e4);
    internal static Color Border => Color.Hex(0x404040);
    internal static Color Muted => Color.Hex(0x8a8a8a);
    internal static NativeSpring PresentationSpring => new(1200, 90, .65f);
    internal static NativePalette Palette => new(Color.Black, Color.Hex(0x171717), Color.Hex(0x262626), Color.White,
        Muted, Color.Hex(0x0e6659), Mint, Border);

    // Experience surface: the source's black canvas and bottom timer strip are the
    // visual authority. The gallery frames this artifact without changing other studies.

    internal static void Preview(Context c)
    {
        using var preview = c.Div(key: "opal-preview").SizeFull().Padding(new Edges(34, 24, 24, 24)).Gap(14);
        using (c.Div(key: "preview-timer").Row().FillWidth().Height(36).Gap(6))
        {
            using (c.Div(key: "preview-value").Width(64).Height(36).Background(Color.White).Rounded(13).Center())
                c.NativeLabel("2h", new(Size: 15, Weight: Weight.Medium, Color: Color.Black), key: "preview-duration");
            using var track = c.Div(key: "preview-track").Flex().Height(36).Background(Color.Hex(0x262626)).Rounded(13).Clip();
            using (c.Div(key: "preview-ticks").Absolute().SizeFull().Row().ItemsCenter().Justify(Justify.SpaceEvenly))
                for (var i = 0; i < 24; i++)
                {
                    using var tick = c.Div(key: i).Width(2).Height(i % 2 == 0 ? 18 : 9).Background(Border).Rounded(1);
                }
            using var fill = c.Div(key: "preview-fill").Absolute().Width(Length.Percent(38)).FillHeight().Background(Color.White);
        }
        using (c.Div(key: "preview-start-clip").FillWidth().Height(42).Rounded(21).Clip())
        using (c.NativeLinearGradient(Color.Hex(0x183b2e), Color.Hex(0x07574c), diagonal: true, key: "preview-start").SizeFull().Row().Center().Gap(6))
        {
            c.NativeSymbol("play.fill", Color.White, 12, key: "play");
            c.NativeLabel("Start Timer", new(Size: 15, Weight: Weight.Medium, Color: Color.White), key: "start-label");
        }
    }
}
