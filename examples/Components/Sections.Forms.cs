using System.Collections.Immutable;
using System.Globalization;
using Kumiko.Components;
using Kumiko.Runtime;
using Kumiko.UI;

namespace Components;

public sealed partial class ComponentsApp
{
    static readonly ImmutableArray<Choice> Plans =
    [
        new("starter", "Starter"),
        new("pro", "Pro"),
        new("team", "Team"),
        new("enterprise", "Enterprise", Disabled: true)
    ];

    static readonly ImmutableArray<Choice> Layouts = [new("grid", "Grid"), new("list", "List"), new("board", "Board")];

    static void Forms(Context c, ShowcaseModel m, Dispatch<ShowcaseMsg> send) =>
        Columns(c, "forms", () => Account(c, m, send), () => Preferences(c, m, send));

    static void Account(Context c, ShowcaseModel m, Dispatch<ShowcaseMsg> send)
    {
        var ui = c.Kit;
        using var card = ui.Card(key: "account").FillWidth();
        ui.CardHeader("Create an account", "Enter your details below to get started.", key: "header");
        const float width = 382;
        ui.Field("Name", m.Name, text => send(new ShowcaseMsg.SetName(text)),
            new(Description: "Shown on your profile and in invitations.", Required: true, Width: width), key: "name");
        var invalid = m.Email.Length > 0 && !ValidEmail(m.Email);
        ui.Field("Email", m.Email, text => send(new ShowcaseMsg.SetEmail(text)),
            new(Error: invalid ? "Enter a valid email address." : null, Placeholder: "you@example.com", Width: width), key: "email");
        ui.Checkbox("Accept terms and conditions", m.Terms, on => send(new ShowcaseMsg.SetTerms(on)), key: "terms");
        ui.Checkbox("Send me product updates", m.Marketing, on => send(new ShowcaseMsg.SetMarketing(on)),
            new("At most one email a month. Unsubscribe anytime."), key: "marketing");
        using var footer = ui.Footer(key: "footer");
        if (ui.Button("Cancel", new(Appearance: Appearance.Outline), key: "cancel").Clicked)
            send(new ShowcaseMsg.Click("Cancel"));
        if (ui.Button("Create account", new(Tone.Primary, Disabled: !m.Terms), key: "create").Clicked)
            send(new ShowcaseMsg.Click("Create account"));
    }

    static void Preferences(Context c, ShowcaseModel m, Dispatch<ShowcaseMsg> send)
    {
        var ui = c.Kit;
        using (ui.Card(key: "settings").FillWidth())
        {
            ui.CardHeader("Connectivity", "Switches apply immediately.", key: "header");
            ui.Switch("Airplane mode", m.Airplane, on => send(new ShowcaseMsg.SetAirplane(on)), key: "airplane");
            ui.Switch("Wi-Fi", m.Wifi, on => send(new ShowcaseMsg.SetWifi(on)),
                new(m.Wifi ? "Connected to Studio 5G" : "Not connected"), key: "wifi");
            ui.Switch("Bluetooth", false, _ => { }, new("Managed by your organization", Disabled: true), key: "bluetooth");
            ui.Separator(key: "rule");
            using (c.Div(key: "volume-label").Row().FillWidth())
            {
                ui.Label("Volume", key: "label");
                c.Spacer(key: "space");
                ui.Muted(m.Volume.ToString("0", CultureInfo.InvariantCulture), key: "value");
            }
            ui.Slider("Volume", m.Volume, value => send(new ShowcaseMsg.SetVolume(value)), new(0, 100, 5, 382), key: "volume");
        }
        using (ui.Card(key: "plan").FillWidth())
        {
            ui.CardHeader("Plan", "Radio groups and toggle groups move with the arrow keys.", key: "header");
            ui.RadioGroup("Plan", Plans, m.Plan, plan => send(new ShowcaseMsg.SetPlan(plan)), key: "plans");
            ui.Label("Layout", key: "layout-label");
            ui.ToggleGroup("Layout", Layouts, m.Layout, layout => send(new ShowcaseMsg.SetLayout(layout)),
                icons: ["square.grid.2x2", "list.bullet", "rectangle.split.2x1"], key: "layout");
        }
    }

    static bool ValidEmail(string text)
    {
        var at = text.IndexOf('@', StringComparison.Ordinal);
        return at > 0 && text.IndexOf('.', at) > at + 1 && !text.EndsWith('.');
    }
}
