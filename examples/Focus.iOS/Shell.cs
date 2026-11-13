using Kumiko.iOS;
using Kumiko.Runtime;
using Kumiko.UI;

internal static class FocusStyles
{
    extension(Div div)
    {
        /// <summary>The padded column above the tab bar that holds the header and the tab's content.</summary>
        public Div FocusContent() => div.FillWidth().Grow().Padding(new Edges(8, 22, 0, 22));

        public Div FocusSection() => div.FillWidth().Grow().Gap(16);
    }
}

/// <summary>The account row shared by every tab, with its simulated connection status.</summary>
internal sealed class FocusHeaderView(string status) : View<string, FocusMessage>(status)
{
    readonly NativePalette p = NativePalette.FocusDark;

    protected override void Render()
    {
        using var row = C.Div(key: "header").Row().FillWidth().Height(44).ItemsCenter().ColumnGap(12).Margin(new Edges(0, 0, 16, 0));
        using (C.Div(key: "identity").Flex().Gap(3))
        {
            C.NativeLabel("backo", new(17, Weight.Semibold, p.Text), key: "name");
            using var status = C.Div(key: "status").Row().ItemsCenter().Gap(6);
            using (C.NativeSurface(new(p.Accent, Radius: 3), key: "status-dot").Width(6).Height(6))
            {
            }
            C.NativeLabel(Model, new(12, Color: p.Muted), key: "connection");
        }
        Ui.NativeAction("Connection preview", new FocusMessage.OpenDrawer("connection"),
            new(Symbol: "pause.fill", Style: NativeActionStyle.Mint, Compact: true, Width: 44, Height: 44), p, key: "connection-action");
    }
}

/// <summary>
/// The background, account header, tab bar and drawer. As a navigation shell it keeps one identity across tab
/// visits, so UIKit retains the same UITabBar (VoiceOver focus, selection animation) while the outlet changes.
/// </summary>
internal sealed class FocusShell(FocusShellProps props) : NavigationShell<FocusShellProps, FocusMessage>(props)
{
    readonly NativePalette p = NativePalette.FocusDark;

    protected override void Render()
    {
        using var surface = C.NativeSurface(new(p.Background, Glow: p.Accent.Alpha(.07f)), key: "focus-root").SizeFull();
        using (C.Div(key: "safe-content").FocusContent())
        {
            Ui.Child(new FocusHeaderView(FocusApp.Status), "header", static message => message);
            Outlet();
        }
        using (C.Div(key: "navigation-row").FillWidth().ItemsCenter())
            Ui.NativeTabBar(FocusApp.Tabs, FocusApp.Tabs[(int)Model.Tab].Id, static id => new FocusMessage.SelectTab(id), p, key: "bottom-bar");
        if (Model.Drawer is { } drawer)
            Ui.Child(new FocusDrawerView(drawer), "drawer", static message => message);
    }
}

/// <summary>The sample day timeline shown on Now and Activity, with its current-time marker.</summary>
internal sealed class DayStripView(float marker) : View<float, FocusMessage>(marker)
{
    readonly NativePalette p = NativePalette.FocusDark;

    protected override void Render()
    {
        C.NativeTimeline([
            new(0, .03f, p.Warm.Alpha(.8f)), new(.03f, .045f, p.Accent.Alpha(.45f)),
            new(.045f, .075f, p.Warm.Alpha(.5f)), new(.075f, .16f, p.Accent.Alpha(.45f)),
            new(.16f, .18f, p.Warm.Alpha(.55f)), new(.18f, .20f, p.Accent.Alpha(.4f)),
            new(.20f, .25f, p.Warm.Alpha(.55f)), new(.25f, .42f, p.Accent.Alpha(.45f))],
            Model, p, key: "day-strip");
        using var labels = C.Div(key: "day-labels").Row().FillWidth().Height(18).Items(Align.Baseline).Justify(Justify.SpaceBetween);
        C.NativeLabel("5:10 woke up", new(11, Color: p.Muted), key: "woke");
        C.NativeLabel("2h 15m rest", new(11, Color: p.Warm), key: "rest");
        C.NativeLabel("22:23", new(11, Color: p.Muted), key: "bed");
    }
}

internal sealed class FocusDrawerView(FocusDrawerProps props) : View<FocusDrawerProps, FocusMessage>(props)
{
    readonly NativePalette p = NativePalette.FocusDark;

    protected override void Render()
    {
        var kind = Model.Kind;
        var title = kind == "duration" ? "Make time for focus" : kind == "blocking" ? "Keep distractions away" : "Your Mac, in the moment";
        using var drawer = Ui.NativeBottomDrawer(title, new FocusMessage.CloseDrawer(),
            kind == "blocking" ? 480 : 390, p, key: "focus-drawer");
        if (kind == "duration")
            Durations();
        else if (kind == "blocking")
        {
            C.NativeLabel("Your choices are saved in this demo session.", new(14, Color: p.Muted, Wrap: true), key: "blocking-copy");
            Ui.NativeCheckList("Blocked distractions", FocusApp.Targets, Model.Blocked,
                static id => new FocusMessage.ToggleBlock(id), p, key: "drawer-block-list");
        }
        else
        {
            C.NativeLabel("A quieter place to begin.", new(22, Weight.Medium, p.Accent), key: "connection-title");
            C.NativeLabel("This is a local Kumiko preview. The connection indicator, timeline and blocking targets are sample data. The countdown, tabs and session controls work locally.",
                new(16, Color: p.Muted, Wrap: true), key: "connection-copy");
        }
    }

    void Durations()
    {
        C.NativeLabel("Choose a little space to do your best work.", new(14, Color: p.Muted, Wrap: true), key: "duration-copy");
        using var choices = C.Div(key: "duration-choices").Row().Wrap().FillWidth().MaxWidth(420)
            .AlignSelf(Align.Center).ColumnGap(8).RowGap(8);
        foreach (var minutes in new[] { 15, 25, 45, 60 })
        {
            using var row = C.Div(key: minutes).Basis(140).Grow().Shrink().MinWidth(120).Height(48);
            Ui.NativeAction($"{minutes} min", new FocusMessage.SetDuration(minutes),
                new(Symbol: Model.Minutes == minutes ? "checkmark.circle.fill" : "circle", Accent: Model.Minutes == minutes,
                    Height: 48, FillWidth: true), p, key: "duration-choice");
        }
    }
}
