using Kumiko.iOS;
using Kumiko.UI;
using static UnwindApp;

internal sealed partial class UnwindScreen(UnwindModel model) : Kumiko.Runtime.Screen<UnwindModel, UnwindMessage>(model)
{
    protected override void Render()
    {
        var ui = Ui;
        var model = Model;
        var c = ui.Context;
        var width = Math.Min(c.Size.Width, 600);
        var hero = NativeComponents.ArcMorphHeight(width);
        using var page = c.Div(key: "unwind").SizeFull().Background(Paper).ItemsCenter();
        using (c.Div(key: "unwind-column").Width(width).FillHeight())
        {
            using (c.Div(key: "unwind-scroll").SizeFull().Scroll())
            {
                // Enough height to dock the header on every screen size.
                using (c.Div(key: "unwind-content").FillWidth().MinHeight(c.Size.Height + NativeComponents.ArcMorphCollapseDistance(width))
                    .Padding(new Edges(hero, 16, 112, 16)).Gap(26))
                {
                    Week();
                    Wins();
                    Notebook();
                    Tonight();
                }
                using (ui.NativeArcMorphCarousel(Routines, model.Index, Greeting, Paper, static index => new UnwindMessage.Select(index),
                    static index => new UnwindMessage.Play(index), key: "unwind-hero").Absolute().Offset(0, 0).FillWidth().Height(hero))
                {
                }
            }
            TopBar(width);
            using (c.NativeLinearGradient(Paper with
            {
                A = 0
            }, Paper, key: "unwind-fade").Absolute().Offset(0, c.Size.Height - 118)
                .FillWidth().Height(118))
            {
            }
            TabBar(width);
        }
        if (model.Notice is { } topic)
        {
            using var drawer = ui.NativeBottomDrawer(topic, new UnwindMessage.CloseNotice(), palette: Palette, key: "unwind-notice");
            c.NativeLabel(Notice(topic), new(Size: 17, Color: Ink, Wrap: true), key: "unwind-notice-copy");
        }
    }

    static string Notice(string topic) => Routines.Any(routine => routine.Category == topic)
        ? $"{topic} audio is not connected. This study recreates the reference's inverted arc carousel and its scroll morph into a pinned menu; routines and progress are sample content."
        : $"{topic} is not connected in this motion study. Routines, streaks, journaling and accounts are sample content.";

    void TopBar(float width)
    {
        var ui = Ui;
        var c = ui.Context;
        using var bar = c.Div(key: "unwind-top").Absolute().Offset(0, 0).Width(width).Height(44).Row().ItemsCenter()
            .Padding(new Edges(0, 10, 0, 4));
        using (ui.NativePressable("All examples", new UnwindMessage.Back(), haptics: true, key: "unwind-back").Width(44).Height(44).Center())
            c.NativeSymbol("arrow.left", Ink, 18, key: "back-symbol");
        c.Spacer();
        using (ui.NativePressable("Notifications", new UnwindMessage.Explain("Notifications"), haptics: true, key: "unwind-bell")
            .Width(44).Height(44).Center())
        using (c.NativeSurface(new(Color.White, Radius: 18, Elevation: 6), key: "bell-surface").Width(36).Height(36).Center())
        {
            c.NativeSymbol("bell.fill", Ink, 15, key: "bell-symbol");
            using (c.Div(key: "bell-badge").Absolute().Offset(20, 7).Width(8).Height(8).Background(Color.Hex(0xee4f37)).Rounded(4))
            {
            }
        }
        using (ui.NativePressable("Profile", new UnwindMessage.Explain("Profile"), haptics: true, key: "unwind-avatar")
            .Width(44).Height(44).Center())
        using (c.Div(key: "avatar").Width(36).Height(36).Background(Color.Hex(0xb9a3f4)).Rounded(18).Center())
            c.NativeLabel("🦊", new(Size: 20), key: "avatar-face");
    }

    void TabBar(float width)
    {
        var ui = Ui;
        var c = ui.Context;
        const float barWidth = 212;
        using var bar = c.NativeSurface(new(Night, Radius: 29, Elevation: 18), key: "unwind-tabs").Absolute()
            .Offset((width - barWidth) / 2, c.Size.Height - 64).Width(barWidth).Height(58).Row().ItemsCenter().Padding(6).Gap(4);
        using (ui.NativePressable("Today", new UnwindMessage.Select(Model.Index), key: "unwind-tab-today").Width(104).Height(46))
        using (c.NativeSurface(new(Color.White, Radius: 23), key: "today-pill").SizeFull().Row().Center().Gap(7))
        {
            c.NativeSymbol("house.fill", Ink, 17, key: "today-symbol");
            c.NativeLabel("Today", new(Size: 17, Color: Ink), key: "today-label");
        }
        Tab("Saved", "bookmark.fill", "unwind-tab-saved");
        Tab("Insights", "chart.pie.fill", "unwind-tab-insights");
    }

    void Tab(string label, string symbol, string key)
    {
        var ui = Ui;
        using var tab = ui.NativePressable(label, new UnwindMessage.Explain(label), haptics: true, key: key).Width(44).Height(46).Center();
        ui.Context.NativeSymbol(symbol, Color.White, 18, key: "tab-symbol");
    }
}
