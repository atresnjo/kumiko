using System.Collections.Immutable;
using Kumiko.Components;
using Kumiko.Runtime;
using Kumiko.UI;

namespace Components;

public sealed partial class ComponentsApp
{
    static readonly ImmutableArray<Choice> Accents =
    [
        new("neutral", "Neutral"),
        new("blue", "Blue"),
        new("violet", "Violet"),
        new("green", "Green"),
        new("orange", "Orange"),
        new("rose", "Rose")
    ];

    static readonly ImmutableArray<Choice> Radii = [new("0", "0"), new("4", "4"), new("6", "6"), new("8", "8"), new("12", "12")];

    static void Overlays(Context c, ShowcaseModel m, Dispatch<ShowcaseMsg> send) => Columns(c, "overlays", () =>
    {
        var ui = c.Kit;
        using var card = ui.Card(key: "dialog").FillWidth();
        ui.CardHeader("Dialog", "Modal, focus-trapped and dismissed with Escape.", key: "header");
        ui.P("Destructive actions ask for confirmation. The dialog dispatches messages; the model decides what happens.", key: "body");
        using (c.Div(key: "result").Row().ItemsCenter().Gap(8))
        {
            ui.Muted("Result:", key: "label");
            ui.Badge(m.LastAction, new(m.LastAction == "Project deleted" ? Tone.Danger : Tone.Secondary, Appearance.Soft), key: "value");
        }
        using var footer = ui.Footer(key: "footer");
        if (ui.Button("Delete project", new(Tone.Danger, Appearance.Outline, Icon: "trash"), key: "open").Clicked)
            send(new ShowcaseMsg.OpenDialog());
    }, () =>
    {
        var ui = c.Kit;
        using var card = ui.Card(key: "notifications").FillWidth();
        ui.CardHeader("Notifications", "Toast cards; the application owns their lifetime.", key: "header");
        ui.Notification("Event created", "Sunday, December 3 at 9:00 AM", key: "default", width: 382);
        ui.Notification("Invoice paid", "Acme Inc. paid $1,200.00", Tone.Success, key: "success", width: 382);
        ui.Notification("Build failed", "main · 2 errors in Checkout.cs", Tone.Danger, key: "danger", width: 382);
    });

    static void ThemePage(Context c, ShowcaseModel m, Dispatch<ShowcaseMsg> send)
    {
        var ui = c.Kit;
        Columns(c, "theme", () =>
        {
            using var card = ui.Card(key: "customize").FillWidth();
            ui.CardHeader("Customize", "Every component reads the same KitTheme tokens.", key: "header");
            ui.Label("Accent", key: "accent-label");
            ui.ToggleGroup("Accent", Accents, m.Accent, accent => send(new ShowcaseMsg.SetAccent(accent)),
                new(ControlSize.Small), key: "accent");
            ui.Label("Radius", key: "radius-label");
            ui.ToggleGroup("Radius", Radii, m.Radius.ToString(System.Globalization.CultureInfo.InvariantCulture),
                radius => send(new ShowcaseMsg.SetRadius(float.Parse(radius, System.Globalization.CultureInfo.InvariantCulture))),
                new(ControlSize.Small), key: "radius");
            ui.Separator(key: "rule");
            using (c.Div(key: "sample").Row().ItemsCenter().Gap(8))
            {
                ui.Button("Primary", new(Tone.Primary), key: "primary");
                ui.Button("Outline", new(Appearance: Appearance.Outline), key: "outline");
                ui.Switch("Preview switch", true, _ => { }, key: "switch");
            }
            ui.Progress(.6f, key: "progress");
        }, () =>
        {
            using var card = ui.Card(key: "scoped").FillWidth();
            ui.CardHeader("Scoped themes", "Kit.Use restyles a region without changing the window theme.", key: "header");
            var inverse = m.Dark ? KitTheme.Light : KitTheme.Dark;
            using (ui.Use(inverse with
            {
                Radius = ui.Theme.Radius
            }))
            {
                using var inner = ui.Card(key: "inverse").FillWidth();
                ui.CardHeader("Upgrade to Pro", "Unlimited projects and priority builds.", key: "header");
                using (c.Div(key: "price").Row().ItemsCenter().Gap(8))
                {
                    ui.Badge("Popular", new(Tone.Primary), key: "badge");
                    ui.Badge("Save 20%", new(Tone.Success, Appearance.Soft), key: "save");
                }
                using var footer = ui.Footer(key: "footer");
                ui.Button("Maybe later", new(Appearance: Appearance.Ghost), key: "later");
                ui.Button("Upgrade", new(Tone.Primary), key: "upgrade");
            }
        });
    }
}
