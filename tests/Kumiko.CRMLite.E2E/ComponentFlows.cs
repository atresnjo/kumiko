using System.Collections.Immutable;
using CRMLite;
using CRMLite.Components;
using Kumiko.Runtime;
using Kumiko.UI;

static class ComponentFlows
{
    public static void Run(string output, Action<bool, string> check)
    {
        using var program = ComponentHost.CreateProgram();
        using var tester = new Tester(program, new(Width: 760, Height: 360, DevMode: true));
        tester.Context.ReduceMotion = true;
        tester.Settle();
        var first = tester.Get(Role.Group, "First");
        var second = tester.Get(Role.Group, "Second");
        var firstSwitch = first.Get(Role.Switch, "Enabled");
        var secondSwitch = second.Get(Role.Switch, "Enabled");
        var firstId = firstSwitch.Id;
        var secondId = secondSwitch.Id;
        check(firstId != secondId && firstSwitch.Rect.Width == 30 && secondSwitch.Rect.Width == 42,
            "component-local keys isolate compact and settings switch instances");
        firstSwitch.Click();
        tester.Settle();
        check(program.Model.First.Enabled && program.Model.Second.Enabled,
            "a mapped switch message changes only its owning parent instance");
        second.Get(Role.Option, "Expanded").Click();
        tester.Settle();
        check(program.Model.First.Selection == 1 && program.Model.Second.Selection == 2,
            "segmented components send typed values through Ui.Child independently");
        var count = program.Model.Actions;
        second.Get(Role.Option, "Expanded").Click();
        tester.Settle();
        check(program.Model.Actions == count, "choosing the current segment does not dispatch again");
        var disabled = first.Get(Role.Button, "Unavailable");
        tester.Click(disabled.Rect.X + disabled.Rect.Width / 2, disabled.Rect.Y + disabled.Rect.Height / 2);
        tester.Settle();
        check(!disabled.Enabled && program.Model.Actions == count, "disabled reusable icon actions ignore pointer input");
        tester.Get(Role.Button, "Reverse").Click();
        tester.Settle();
        check(firstSwitch.Id == firstId && secondSwitch.Id == secondId && first.Rect.X > second.Rect.X,
            "keyed component instances retain identity when their order changes");
        tester.Context.FocusControl(first.Get(Role.Button, "Save").Id);
        tester.Key(Key.Enter);
        tester.Settle();
        check(program.Model.Actions == count + 1 && program.Model.LastAction == "First",
            "reusable action buttons bind keyboard activation to the correct parent");
        var firstSlot = first.Get(Role.Text, "First value").Rect;
        var secondSlot = second.Get(Role.Text, "Second value").Rect;
        check(firstSlot.X - first.Rect.X == 120 && secondSlot.X - second.Rect.X == 152,
            "property row slots keep independent keys and configurable label widths");
        var plain = tester.Context.Nodes.ToArray().Single(n => n.Key == "plain").Rect;
        var pixels = tester.Render();
        bool Fill(Tester.Locator button, Color color)
        {
            var rect = button.Rect;
            var pixel = ((int)(rect.Y + rect.Height / 2) * pixels.Width + (int)(rect.Right - 5)) * 4;
            return Math.Abs(pixels.Bytes[pixel + 2] - color.R * 255) < 2 &&
                Math.Abs(pixels.Bytes[pixel + 1] - color.G * 255) < 2 && Math.Abs(pixels.Bytes[pixel] - color.B * 255) < 2;
        }
        check(Fill(first.Get(Role.Button, "Save"), new Palette(false).Accent) &&
            Fill(second.Get(Role.Button, "Save"), new Palette(true, Accent.Green).Accent),
            "sibling component instances paint with their own supplied palettes");
        var at = ((int)(plain.Y + 12) * pixels.Width + (int)(plain.X + 12)) * 4;
        var clear = tester.Context.Theme.Background;
        check(Math.Abs(pixels.Bytes[at + 2] - clear.R * 255) < 2 &&
            Math.Abs(pixels.Bytes[at + 1] - clear.G * 255) < 2 && Math.Abs(pixels.Bytes[at] - clear.B * 255) < 2,
            "private component surface styles do not affect an adjacent plain Div");
        check(tester.Context.Warnings.Count == 0, "reused components render without identity, layout or contrast warnings");
        Capture(tester, output, "components");
        var session = Path.Combine(output, "components-session.jsonl");
        program.Save(session);
        using var replay = ComponentHost.CreateProgram();
        replay.Replay(session);
        using var replayTester = new Tester(replay, new(Width: 760, Height: 360, DevMode: true));
        replayTester.Context.ReduceMotion = true;
        replayTester.Settle();
        check(replay.Model == program.Model && replayTester.HasText("First value"),
            "an independent host replays mapped component messages and renders its slots");
        Capture(replayTester, output, "components-replay");
    }

    static void Capture(Tester tester, string output, string name)
    {
        tester.Screenshot(Path.Combine(output, name + ".png"));
        File.WriteAllText(Path.Combine(output, name + ".txt"), tester.Dump());
        tester.WriteWarnings(Path.Combine(output, name + ".warnings.json"));
    }
}

internal sealed record ComponentValue(bool Enabled, int Selection);
internal sealed record ComponentModel(ComponentValue First, ComponentValue Second, bool Reversed = false, int Actions = 0, string LastAction = "");
internal abstract partial record ComponentEvent
{
    public sealed record Toggle : ComponentEvent;
    public sealed record Select(int Value) : ComponentEvent;
    public sealed record Save : ComponentEvent;
}
internal abstract partial record ComponentMessage
{
    public sealed record From(string Id, ComponentEvent Event) : ComponentMessage;
    public sealed record Reverse : ComponentMessage;
}

internal sealed partial class ComponentHost : IKumiko<ComponentModel, ComponentMessage>
{
    public static Kumiko.Runtime.NavigationRoot<ComponentMessage> Root(ComponentModel model) => Kumiko.Runtime.NavigationRoot<ComponentMessage>.Create(
        Kumiko.Runtime.NavigationHistory<string>.Start("root"), "app", _ => new ComponentHostView(model).Map<ComponentMessage>(static message => message),
        static _ => throw new InvalidOperationException("This application has one root screen."));
    public static ComponentModel Init => new(new(false, 1), new(true, 1));

    public static (ComponentModel, Cmd<ComponentMessage>) Update(ComponentModel model, ComponentMessage message)
    {
        if (message is ComponentMessage.Reverse)
            return (model with
            {
                Reversed = !model.Reversed
            }, Cmd<ComponentMessage>.None);
        if (message is not ComponentMessage.From from)
            throw new ArgumentOutOfRangeException(nameof(message));
        var value = from.Id == "First" ? model.First : model.Second;
        return ComponentPanel.Update(value, from.Event).Fold(
            next => (from.Id == "First" ? model with
            {
                First = next
            } : model with
            {
                Second = next
            }) with
            {
                Actions = model.Actions + 1,
                LastAction = from.Id
            },
            child => (ComponentMessage)new ComponentMessage.From(from.Id, child));
    }

}

internal sealed class ComponentHostView(ComponentModel model) : Kumiko.Runtime.Screen<ComponentModel, ComponentMessage>(model)
{
    protected override void Render()
    {
        var c = Ui.Context;
        using var root = c.Div(key: "root").SizeFull().Padding(24).Gap(24);
        Ui.Button("Reverse", new ComponentMessage.Reverse(), key: "reverse");
        using (c.Div(key: "instances").Row().Gap(24))
        {
            foreach (var id in Model.Reversed ? new[] { "Second", "First" } : new[] { "First", "Second" })
            {
                var value = id == "First" ? Model.First : Model.Second;
                Ui.Child(new ComponentPanelView(new(value, id == "Second", id == "First" ? Accent.Blue : Accent.Green, id)), id, message => new ComponentMessage.From(id, message));
            }
        }
        using (c.Div(key: "plain").Width(100).Height(24))
        {
        }
    }
}

internal sealed class ComponentPanel : IKumikoComponent<ComponentValue, ComponentEvent>
{
    public static (ComponentValue, Cmd<ComponentEvent>) Update(ComponentValue value, ComponentEvent message) => (message switch
    {
        ComponentEvent.Toggle => value with { Enabled = !value.Enabled },
        ComponentEvent.Select select => value with { Selection = select.Value },
        ComponentEvent.Save => value,
        _ => throw new ArgumentOutOfRangeException(nameof(message))
    }, Cmd<ComponentEvent>.None);

}

internal sealed record ComponentPanelProps(ComponentValue Value, bool Dark, Accent Accent, string Id);
internal sealed class ComponentPanelView(ComponentPanelProps props) : View<ComponentPanelProps, ComponentEvent>(props)
{
    protected override void Render()
    {
        var ui = Ui;
        var value = Model.Value;
        var palette = new Palette(Model.Dark, Model.Accent);
        var id = Model.Id;
        var c = ui.Context;
        using var card = c.Div(key: "panel").Width(344).Padding(12).Gap(16).Background(palette.Page).Semantics(Role.Group, id);
        ToggleSwitch.View(ui, palette, "Enabled", value.Enabled, new ComponentEvent.Toggle(),
            id == "First" ? ToggleVariant.Compact : ToggleVariant.Settings, key: "switch");
        SegmentedChoice.View(ui, palette, "Density", value.Selection,
            [new Segment<int>("compact", "Compact", 1), new Segment<int>("expanded", "Expanded", 2)],
            static selected => new ComponentEvent.Select(selected), key: "segmented");
        PropertyRow.View(c, palette, "Value", () => c.Text(id + " value", new(Color: palette.Text), key: "text"),
            id == "First" ? PropertyRowProps.Compact : PropertyRowProps.Comfortable, key: "property");
        using var actions = c.Div(key: "actions").Row().Gap(8);
        ui.OnPress(ActionButton.Primary(c, palette, "Save", icon: null, key: "save"), new ComponentEvent.Save());
        ui.OnPress(ActionButton.IconButton(c, palette, Kumiko.Icons.Hugeicons.Cancel01, "Unavailable", false, key: "disabled"), new ComponentEvent.Save());
    }
}
