using Kumiko.Icons;
using Kumiko.Runtime;
using Kumiko.UI;

namespace TraceWaterfall;

// Reference: Flo's affectUI trace waterfall, https://x.com/flohoeller/status/2112823884689133905.
// Independent native implementation: preserve the split tree/timeline, quiet neutral surfaces,
// violet LLM calls, teal tools, live time cursor and floating call inspector in both themes.
// The synthetic pricing-launch trace is the content; replay, folding and inspection are the task.
public sealed partial class TraceScreen(TraceModel trace) : Screen<TraceModel, TraceMsg>(trace)
{
    readonly Palette p = new(trace.Dark);

    protected override void Render()
    {
        var ui = Ui;
        var model = Model;
        var c = ui.Context;
        c.Kit.Apply(p.Kit);
        using var page = c.Div(key: "trace-example").SizeFull().Padding(Palette.Inset).Gap(16).Background(p.Page);
        Toolbar();
        using (c.Div(key: "trace").FillWidth().Flex().Rounded(16).Border(p.Border).Background(p.Surface).Clip())
        {
            Header();
            Metrics();
            Waterfall();
        }
        using (c.Div(key: "footer").Row().FillWidth().ItemsCenter().Gap(16))
        {
            Text("Illustrative trace · no API calls", Palette.Caption, p.Muted, key: "demo");
            c.Spacer(key: "space");
            Text("Kumiko · C# + Elm", Palette.Caption, p.Muted, key: "framework");
        }
    }

    void Toolbar()
    {
        var ui = Ui;
        var model = Model;
        var c = ui.Context;
        using var bar = c.Div(key: "toolbar").Row().FillWidth().Height(34).ItemsCenter().Gap(8);
        Text("Trace waterfall", Palette.Heading, p.Text, Weight.Semibold, "title");
        c.Spacer(key: "space");
        Action(model.Playing ? "Pause" : "Resume", model.Playing ? Hugeicons.Pause : Hugeicons.Play,
            new TraceMsg.TogglePlaying(), "play", model.Now < 92000);
        Action("Restart", Hugeicons.Refresh, new TraceMsg.Restart(), "restart");
        Action("Show completed trace", Hugeicons.Tick02, new TraceMsg.Complete(), "complete", model.Now < 92000, iconOnly: true);
        Action(model.Dark ? "Light theme" : "Dark theme", model.Dark ? Hugeicons.Sun03 : Hugeicons.Moon02,
            new TraceMsg.ToggleTheme(), "theme", iconOnly: true);
    }

    void Header()
    {
        var c = C;
        var model = Model;
        using var bar = c.Div(key: "header").Row().FillWidth().Height(62).Px(24).ItemsCenter().Gap(12);
        c.Icon(Hugeicons.Sparkles, 20, p.Blue, key: "icon");
        Text("Trace", Palette.Heading, p.Text, Weight.Semibold, "title");
        using (c.Div(key: "prompt").Flex().Clip())
            Text(model.Spans[0].Input, Palette.Heading, p.Muted, key: "text");
        var state = model.Now >= 92000 ? "Done" : model.Playing ? "Live" : "Paused";
        using var badge = c.Div(key: "state").Row().Height(27).Px(9).Gap(6).ItemsCenter().Rounded(6).Background(p.Hover)
            .Semantics(Role.Group, "Trace status", state);
        using (c.Div(key: "dot").Width(6).Height(6).Rounded(3).Background(p.Blue))
        {
        }
        Text(state, 13, p.Blue, Weight.Medium, "text");
    }

    void Metrics()
    {
        var c = C;
        var model = Model;
        var total = TraceQueries.Totals(model, model.Spans[0]);
        using var metrics = c.Div(key: "metrics").Row().FillWidth().Height(94).Py(18).Border(p.Grid);
        Metric("DURATION", TraceQueries.Time(model.Now), "duration");
        Metric("COST", TraceQueries.Cost(total.Cost), "cost");
        Metric("TOKENS", TraceQueries.Tokens(total.TokensIn + total.TokensOut), "tokens");
        Metric("LLM CALLS", total.LlmCalls.ToString(c.Culture), "llm");
        Metric("TOOL CALLS", total.ToolCalls.ToString(c.Culture), "tools");
        Metric("ERRORS", total.Errors.ToString(c.Culture), "errors", total.Errors > 0 ? p.Danger : null);
    }

    void Metric(string label, string value, string key, Color? color = null)
    {
        var c = C;
        using var metric = c.Div(key: key).Flex().FillHeight().Px(24).Gap(7).Semantics(Role.Group, label, value);
        Text(label, Palette.Caption, p.Muted, key: "label");
        Text(value, Palette.Metric, color ?? p.Text, Weight.Medium, "value");
    }

    void Action(string label, string icon, TraceMsg message, string key,
        bool enabled = true, bool iconOnly = false)
    {
        var ui = Ui;
        var c = ui.Context;
        using var button = c.Div(key: key).Row().Height(32).Px(iconOnly ? 8 : 10).ItemsCenter().Gap(7).Rounded(6);
        var hit = ui.Button(button, label, message, enabled);
        Feedback(button, hit);
        c.Icon(icon, 16, enabled ? p.Muted : Color.Mix(p.Surface, p.Muted, .5f), key: "icon");
        if (!iconOnly)
            Text(label, 13, enabled ? p.Text : p.Muted, key: "label");
    }

    void Feedback(Div box, Interaction hit)
    {
        var c = C;
        if (hit.Hovered)
        {
            box.Background(p.Hover);
            c.Cursor = Cursor.Hand;
        }
        if (hit.Focused && c.FocusVisible)
            box.Border(p.Blue, 2);
    }

    void Text(string text, float size, Color color, Weight weight = Weight.Regular, NodeKey key = default) =>
        C.Text(text, new(Size: size, Weight: weight, Color: color), key: key);
}
