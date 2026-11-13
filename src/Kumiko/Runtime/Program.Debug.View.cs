using Kumiko.UI;

namespace Kumiko.Runtime;

public sealed partial class Program<TModel, TMessage>
{
    const float DebugMargin = Space.S2, DebugWidth = 420, DebugCompactHeight = 76, DebugExpandedHeight = 560;
    const float DebugButtonHeight = 26, DebugEventHeight = 26;

    DebugAction Debugger(Context c)
    {
        var action = default(DebugAction);
        var width = Math.Max(0, Math.Min(DebugWidth, c.Size.Width - DebugMargin * 2));
        var height = Math.Max(0, Math.Min(debugExpanded ? DebugExpandedHeight : DebugCompactHeight, c.Size.Height - DebugMargin * 2));
        var x = Math.Max(0, c.Size.Width - width - DebugMargin);
        var y = Math.Max(0, c.Size.Height - height - DebugMargin);
        var start = c.Count;
        using (var panel = c.Div(key: "kumiko.debugger").Absolute().Offset(x, y).Width(width).Height(height)
            .Padding(Space.S2).Gap(Space.S1).Rounded(Radius.Lg).Background(c.Theme.Surface).Border(c.Theme.Border)
            .Clip().Semantics(Role.Group, "Event history"))
        {
            panel.Control();
            using (c.Div(key: "heading").Row().FillWidth().Height(DebugButtonHeight).ItemsCenter().Gap(Space.S1))
            {
                c.Text($"Event history · {(frozen ? "Paused" : "Live")} · #{Position}/{history.Count - 1}",
                    c.Theme.Typography.Caption with
                    {
                        Weight = Weight.Semibold
                    });
                c.Spacer();
                if (DebugButton(c, debugExpanded ? "Hide events" : "Show events", "expand"))
                    action = new(DebugOperation.Expand);
            }
            using (c.Div(key: "navigation").Row().FillWidth().Height(DebugButtonHeight).ItemsCenter().Gap(Space.S1))
            {
                if (DebugButton(c, "Back", "back", CanGoBack))
                    action = new(DebugOperation.Back);
                if (DebugButton(c, "Forward", "forward", CanGoForward))
                    action = new(DebugOperation.Forward);
                if (frozen && DebugButton(c, "Latest", "latest", Position != history.Count - 1))
                    action = new(DebugOperation.Latest);
                c.Spacer();
                if (DebugButton(c, frozen ? "Resume" : "Pause", "pause"))
                    action = new(frozen ? DebugOperation.Resume : DebugOperation.Pause);
            }
            if (debugExpanded)
                using (c.Div(key: "inspector-body").FillWidth().Grow().Scroll().Gap(Space.S1))
                    DebugContents(c, height, ref action);
        }
        c.SetDebugger(start, new(x, y, width, height));
        return action;
    }

    void DebugContents(Context c, float height, ref DebugAction action)
    {
        if (height < 230)
        {
            c.Text("Enlarge the window to inspect events.", c.Theme.Typography.Caption with
            {
                Wrap = true
            });
            return;
        }
        var entry = history[Position];
        using (c.Div(key: "selected").FillWidth().Height(24).Clip())
            c.Text($"#{Position}: {EventName(Position)} · parent #{entry.Parent} · frame {entry.Frame}", c.Theme.Typography.Caption);
        var requested = action;
        using (c.Div(key: "events").FillWidth().Height(Math.Clamp(height * .28f, 48, 156)).Clip())
            c.UniformList("history", history.Count, DebugEventHeight, (ctx, index) =>
            {
                var label = $"Event #{index}: {EventName(index)}";
                using var row = ctx.Div(key: "event").Row().FillWidth().Height(DebugEventHeight).ItemsCenter()
                    .Px(Space.S2).Rounded(Radius.Sm).Semantics(Role.Button, label);
                var hit = row.Control();
                if (hit.Clicked)
                    requested = new(DebugOperation.Select, index);
                if (index == Position || hit.Hovered)
                    row.Background(Color.Mix(c.Theme.Surface, c.Theme.Text, index == Position ? .1f : .05f));
                if (hit.Focused)
                    row.Border(c.Theme.Accent);
                ctx.Text($"#{index}  {EventName(index)}", ctx.Theme.Typography.Caption);
            }, revealRow: Position != debugRevealed || height != debugViewportHeight ? Position : null, rowKey: i => (ulong)i + 1);
        action = requested;
        debugRevealed = Position;
        debugViewportHeight = height;
        using (c.Div(key: "source").FillWidth().MaxHeight(48).Clip())
            c.Text(entry.Source is { } source ? $"{source.Role} {source.Name} · key={source.Key}\n{source.Path}" : "External message / initial model",
                c.Theme.Typography.Caption with
                {
                    Wrap = true
                });
        if (entry.Commands.Count > 0)
            using (c.Div(key: "commands").FillWidth().Height(18).Clip())
                c.Text("Commands: " + string.Join(", ", entry.Commands), c.Theme.Typography.Caption);
        using (c.Div(key: "tabs").Row().FillWidth().Height(DebugButtonHeight).Gap(Space.S1))
        {
            foreach (var tab in Enum.GetValues<DebugTab>())
                if (DebugButton(c, tab.ToString(), "tab." + tab, selected: debugTab == tab))
                {
                    debugTab = tab;
                    c.Changed = true;
                }
        }
        using (c.Div(key: $"detail.{Position}.{debugTab}").FillWidth().Height(Math.Max(80, height * .27f)).Scroll().Clip().Padding(Space.S1))
            c.Text(DebugDetail(), c.Theme.Typography.Caption with
            {
                Mono = true,
                Wrap = true
            });
    }

    static bool DebugButton(Context c, string label, string key, bool enabled = true, bool selected = false)
    {
        using var button = c.Div(key: key).Row().Height(DebugButtonHeight).Px(Space.S2).Center().Rounded(Radius.Sm)
            .Semantics(Role.Button, label);
        var hit = button.Control(enabled);
        button.Background(selected || enabled && hit.Hovered ? Color.Mix(c.Theme.Surface, c.Theme.Text, .1f) : c.Theme.Surface);
        if (hit.Focused)
            button.Border(c.Theme.Accent, 2);
        else if (selected)
            button.Border(c.Theme.Border);
        c.Text(label, c.Theme.Typography.Caption with
        {
            Weight = selected ? Weight.Semibold : Weight.Medium,
            Color = enabled ? c.Theme.Text : c.Theme.Muted
        });
        return hit.Clicked;
    }
}
