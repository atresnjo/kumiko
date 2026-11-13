using System.Collections.Immutable;
using Kumiko.iOS;
using Kumiko.UI;
using static UnwindApp;

internal sealed partial class UnwindScreen
{
    static ImmutableArray<(string Key, string Letter, string Name, Color Color, NativeOrbMark Mark)> Days =>
    [
        ("monday", "M", "Monday", Color.Hex(0xa8d48a), NativeOrbMark.Check),
        ("tuesday", "T", "Tuesday", Color.Hex(0xb6a5f2), NativeOrbMark.Check),
        ("wednesday", "W", "Wednesday", Color.Hex(0xc9c5bf), NativeOrbMark.Empty),
        ("thursday", "T", "Thursday", Color.Hex(0xf2c26c), NativeOrbMark.Check),
        ("friday", "F", "Friday", Color.Hex(0xc9c5bf), NativeOrbMark.Empty),
        ("saturday", "S", "Saturday", Color.Hex(0xb6a5f2), NativeOrbMark.Check),
        ("sunday", "S", "Sunday, today", Color.Hex(0xf2a23a), NativeOrbMark.Dot)
    ];

    void Week()
    {
        var c = C;
        using var card = c.NativeSurface(new(Color.White, Radius: 28, Elevation: 10), key: "unwind-week").FillWidth()
            .Padding(new Edges(18, 18, 14, 18)).Gap(14);
        using (c.Div(key: "week-head").Row().FillWidth().ItemsCenter().Gap(5))
        {
            c.NativeLabel("This week", new(Size: 17, Color: Ink), key: "week-title");
            c.Spacer();
            c.NativeSymbol("flame.fill", Ink, 13, key: "streak-flame");
            c.NativeLabel("4 of 7 days", new(Size: 14, Color: Muted), key: "streak");
        }
        using var days = c.Div(key: "week-days").Row().FillWidth().Justify(Justify.SpaceBetween);
        foreach (var day in Days)
        {
            using var column = c.Div(key: day.Key).ItemsCenter().Gap(8);
            var today = day.Mark == NativeOrbMark.Dot;
            var state = day.Mark == NativeOrbMark.Empty ? "open" : today ? "in progress" : "done";
            c.NativeOrb(day.Color, 35, day.Mark, today ? 1 : .45f, $"{day.Name}: {state}", key: "orb");
            c.NativeLabel(day.Letter, new(Size: 13, Weight: today ? Weight.Semibold : Weight.Regular, Color: today ? Ink : Faint),
                key: "letter");
        }
    }

    void Wins()
    {
        var ui = Ui;
        var c = ui.Context;
        using var section = c.Div(key: "unwind-wins").FillWidth().Gap(10);
        using (c.Div(key: "wins-head").Row().FillWidth().ItemsCenter().Padding(LayoutEdge.Left, 6))
        {
            c.NativeLabel("Small wins", new(Size: 22, Color: Ink), key: "wins-title");
            c.Spacer();
            using (ui.NativePressable("See all small wins", new UnwindMessage.Explain("Small wins"), key: "unwind-see-all")
                .Width(64).Height(44).Center())
                c.NativeLabel("See all", new(Size: 15, Color: Muted), key: "see-all");
        }
        using var row = c.Div(key: "wins-row").Row().FillWidth().Gap(12);
        Win("sleep-win", "moon.stars.fill", "Sleep", Color.Hex(0xae9cf2), "Lights out by 11", "5", "nights");
        Win("outside-win", "figure.walk", "Outside", Color.Hex(0x9acb78), "Morning light", "3", "days");
    }

    void Win(string key, string symbol, string title, Color orb, string caption, string count, string unit)
    {
        var c = C;
        using var card = c.NativeSurface(new(Color.White, Radius: 26, Elevation: 10), key: key).Flex().Height(172)
            .Padding(new Edges(14, 14, 14, 14)).ItemsCenter();
        using (c.Div(key: "chip").AlignSelf(Align.Start).Row().ItemsCenter().Gap(6).Background(Chip).Rounded(15)
            .Padding(new Edges(6, 12, 6, 10)))
        {
            c.NativeSymbol(symbol, Ink, 14, key: "chip-symbol");
            c.NativeLabel(title, new(Size: 15, Color: Ink), key: "chip-title");
        }
        using (c.Div(key: "orb-gap").Height(10))
        {
        }
        c.NativeOrb(orb, 48, glow: .6f, key: "win-orb");
        using (c.Div(key: "caption-gap").Height(12))
        {
        }
        c.NativeLabel(caption, new(Size: 15, Color: Faint), key: "caption");
        using var value = c.Div(key: "value").Row().Items(Align.End).Gap(5);
        c.NativeLabel(count, new(Size: 30, Color: Ink), key: "count");
        using (c.Div(key: "unit").Padding(LayoutEdge.Bottom, 4))
            c.NativeLabel(unit, new(Size: 15, Color: Muted), key: "unit-label");
    }

    void Notebook()
    {
        var ui = Ui;
        var c = ui.Context;
        using var card = c.NativeSurface(new(Color.White, Radius: 26, Elevation: 10), key: "unwind-notebook").FillWidth()
            .Padding(new Edges(18, 18, 16, 18)).Gap(14);
        using (c.Div(key: "notebook-head").Row().FillWidth().ItemsCenter().Gap(12))
        {
            using (c.Div(key: "notebook-icon").Width(36).Height(36).Background(Chip).Rounded(18).Center())
                c.NativeSymbol("book.closed", Ink, 16, key: "notebook-symbol");
            using (c.Div(key: "notebook-copy").Flex().Gap(2))
            {
                c.NativeLabel("One line for today", new(Size: 17, Color: Ink), key: "notebook-title");
                c.NativeLabel("From your notebook", new(Size: 14, Color: Muted), key: "notebook-source");
            }
        }
        c.NativeLabel("What felt a little lighter this week?", new(Size: 22, Color: Ink, Wrap: true), key: "notebook-prompt");
        using var actions = c.Div(key: "notebook-actions").Row().FillWidth().ItemsCenter().Gap(8);
        using (c.NativeDashedLine(Color.Hex(0xc9c5bf), key: "notebook-rule").Flex())
        {
        }
        using (ui.NativePressable("Write", new UnwindMessage.Explain("Writing"), haptics: true, key: "unwind-write").Width(66).Height(44).Center())
        using (c.Div(key: "write-pill").Width(62).Height(32).Background(Ink).Rounded(16).Center())
            c.NativeLabel("Write", new(Size: 15, Weight: Weight.Medium, Color: Color.White), key: "write-label");
        using (ui.NativePressable("Record a voice note", new UnwindMessage.Explain("Voice notes"), haptics: true, key: "unwind-voice")
            .Width(44).Height(44).Center())
        using (c.Div(key: "voice-circle").Width(32).Height(32).Background(Color.Hex(0xecebe7)).Rounded(16).Center())
            c.NativeSymbol("mic.fill", Ink, 14, key: "voice-symbol");
    }

    void Tonight()
    {
        var c = C;
        using var section = c.Div(key: "unwind-tonight").FillWidth().Gap(12);
        using (c.Div(key: "tonight-head").Padding(LayoutEdge.Left, 6))
            c.NativeLabel("Tonight", new(Size: 22, Color: Color.Hex(0x5f5b56)), key: "tonight-title");
        using var card = c.NativeSurface(new(Color.White, Radius: 26, Elevation: 10), key: "tonight-card").FillWidth()
            .Padding(new Edges(16, 18, 16, 18)).Gap(14);
        Plan("wind-down", Color.Hex(0xae9cf2), "Wind down", "Body scan before bed", "9:30 pm");
        using (c.Div(key: "tonight-divider").FillWidth().Height(1).Background(Color.Hex(0xeeebe6)))
        {
        }
        Plan("lights-out", Color.Hex(0xf2c26c), "Lights out", "Phone stays outside the bedroom", "11:00 pm");
    }

    void Plan(string key, Color orb, string title, string detail, string time)
    {
        var c = C;
        using var row = c.Div(key: key).Row().FillWidth().ItemsCenter().Gap(12);
        c.NativeOrb(orb, 34, glow: .35f, key: "plan-orb");
        using (c.Div(key: "plan-copy").Flex().Gap(2))
        {
            c.NativeLabel(title, new(Size: 17, Color: Ink), key: "plan-title");
            c.NativeLabel(detail, new(Size: 14, Color: Muted), key: "plan-detail");
        }
        c.NativeLabel(time, new(Size: 15, Color: Ink), key: "plan-time");
    }
}
