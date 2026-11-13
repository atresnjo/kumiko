using Kumiko.iOS;
using Kumiko.Runtime;
using Kumiko.UI;

internal sealed class NowScreen(NowProps props) : Screen<NowProps, FocusMessage>(props)
{
    readonly NativePalette p = NativePalette.FocusDark;

    protected override void Render()
    {
        using (C.Div(key: "session-column").FillWidth().ItemsCenter())
            Session();
        C.Spacer(key: "rest-space");
    }

    void Session()
    {
        var m = Model;
        var contentWidth = Math.Min(420, C.Size.Width - 44);
        using var content = C.Div(key: "session-content").FillWidth().MaxWidth(420).AlignSelf(Align.Center);
        var diameter = Math.Min(264, Math.Min(contentWidth - 20, Math.Max(160, C.Size.Height - 340)));
        using (C.Div(key: "dial-row").FillWidth().ItemsCenter().Margin(new Edges(0, 0, 14, 0)))
        {
            var time = $"{m.Remaining / 60:00}:{m.Remaining % 60:00}";
            var status = m.Running ? "Time to focus" : m.Started ? "Take your time" : "Ready when you are";
            var detail = m.Running ? "one thing at a time" : m.Started ? "session paused" : "until 12:24";
            C.NativeTimerDial(time, status, detail, diameter,
                m.Started ? 1f - (float)m.Remaining / (m.Minutes * 60) : 0, p, key: "timer-dial");
        }
        using (C.Div(key: "activity").FillWidth().Margin(new Edges(0, 0, 8, 0)))
            Ui.Child(new DayStripView(FocusApp.Now), "strip", static message => message);
        using (C.Div(key: "recipe").Row().FillWidth().Height(44).ItemsCenter().Justify(Justify.Center).Gap(8))
        {
            C.NativeLabel("Work for", new(18, Color: p.Muted), key: "work-for");
            Ui.NativeAction($"{m.Minutes} min", new FocusMessage.OpenDrawer("duration"),
                new(Symbol: "circle.fill", Compact: true, Chevron: true, Width: 106, Height: 44), p, key: "duration");
            C.NativeLabel("and", new(18, Color: p.Muted), key: "and");
        }
        using (C.Div(key: "blocking-row").FillWidth().Padding(new Edges(0, 10, 0, 10)))
            Ui.NativeAction(m.BlockingSummary, new FocusMessage.OpenDrawer("blocking"),
                new(Symbol: "checkmark.shield.fill", Compact: true, Chevron: true, Accent: true,
                    FillWidth: true, Height: 44), p, key: "blocking-summary");
        using (C.Div(key: "session-actions").Row().FillWidth().Height(52).ColumnGap(10).Margin(new Edges(12, 0, 0, 0)))
        {
            using (C.Div(key: "primary-action").Basis(0).Grow().Shrink().MinWidth(44))
                Ui.NativeAction(m.Running ? "Focusing" : m.Started ? "Resume" : "Start", new FocusMessage.Start(),
                    new(Symbol: "play.fill", Style: NativeActionStyle.Glow, Disabled: m.Running,
                        FillWidth: true, Height: 52), p, key: "start");
            Ui.NativeAction("Pause", new FocusMessage.Pause(),
                new(Symbol: "pause.fill", Disabled: !m.Running, Width: 48, Height: 52), p, key: "pause");
        }
    }
}

internal sealed class BlockingScreen(BlockingProps props) : Screen<BlockingProps, FocusMessage>(props)
{
    readonly NativePalette p = NativePalette.FocusDark;

    protected override void Render()
    {
        using var body = C.Div(key: "blocking").FocusSection();
        C.NativeLabel("Blocking", new(28, Weight.Semibold, p.Text), key: "section-title");
        C.NativeLabel("Choose what stays out of your way during a focus session.", new(15, Color: p.Muted, Wrap: true), key: "section-copy");
        Ui.NativeCheckList("Blocked distractions", FocusApp.Targets, Model.Blocked,
            static id => new FocusMessage.ToggleBlock(id), p, key: "block-list");
    }
}

internal sealed class ActivityScreen(SectionProps props) : Screen<SectionProps, FocusMessage>(props)
{
    readonly NativePalette p = NativePalette.FocusDark;

    protected override void Render()
    {
        using var body = C.Div(key: "activity").FocusSection();
        C.NativeLabel("Activity", new(28, Weight.Semibold, p.Text), key: "section-title");
        C.NativeLabel("A little focus adds up.", new(17, Color: p.Muted), key: "section-copy");
        Ui.Child(new DayStripView(FocusApp.Now), "strip", static message => message);
        C.NativeLabel("Today's preview", new(20, Weight.Medium, p.Text), key: "activity-heading");
        C.NativeLabel("45 min focus · 2h 15m rest", new(16, Color: p.Accent), key: "activity-total");
        C.NativeLabel("This timeline uses sample data. Your current session is tracked on Now.", new(15, Color: p.Muted, Wrap: true), key: "activity-note");
    }
}

internal sealed class HealthScreen(SectionProps props) : Screen<SectionProps, FocusMessage>(props)
{
    readonly NativePalette p = NativePalette.FocusDark;

    protected override void Render()
    {
        using var body = C.Div(key: "health").FocusSection();
        C.NativeLabel("Health", new(28, Weight.Semibold, p.Text), key: "section-title");
        C.NativeLabel("Make room to recharge.", new(20, Weight.Medium, p.Text), key: "section-copy");
        C.NativeLabel("Take a short walk, look away from your screen, and give yourself a moment between sessions.",
            new(16, Color: p.Muted, Wrap: true), key: "health-copy");
        Ui.NativeAction("Use a 15 min session", new FocusMessage.SetDuration(15),
            new(Symbol: "sun.max", Accent: true), p, key: "short-session");
    }
}

internal sealed class SettingsScreen(SectionProps props) : Screen<SectionProps, FocusMessage>(props)
{
    readonly NativePalette p = NativePalette.FocusDark;

    protected override void Render()
    {
        using var body = C.Div(key: "settings").FocusSection();
        C.NativeLabel("Settings", new(28, Weight.Semibold, p.Text), key: "section-title");
        C.NativeLabel("Kumiko Focus", new(21, Weight.Semibold, p.Text), key: "section-copy");
        C.NativeLabel("A reusable UIKit component example, based on your reference image. Connection, activity and blocking data are simulated; no apps or sites are blocked.",
            new(15, Color: p.Muted, Wrap: true), key: "demo-note");
        Ui.NativeAction("Connection preview", new FocusMessage.OpenDrawer("connection"),
            new(Symbol: "desktopcomputer", Chevron: true), p, key: "settings-connection");
    }
}
