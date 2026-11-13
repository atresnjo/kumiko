using System.Collections.Immutable;
using Kumiko.Components;
using Kumiko.Runtime;
using Kumiko.UI;

namespace Components;

public sealed partial class ComponentsApp
{
    static readonly ImmutableArray<string> Team = ["Ada Lovelace", "Grace Hopper", "Alan Turing", "Katherine Johnson", "Linus Torvalds", "Barbara Liskov"];

    static void Overview(Context c, ShowcaseModel m, Dispatch<ShowcaseMsg> send)
    {
        var ui = c.Kit;
        using (ui.Card(key: "buttons").FillWidth())
        {
            ui.CardHeader("Buttons", "Tones set the color role; appearances set how it is painted.", key: "header");
            using (c.Div(key: "variants").Row().ItemsCenter().Gap(8))
            {
                (string Label, ButtonProps Props)[] variants =
                [
                    ("Primary", new(Tone.Primary)),
                    ("Secondary", new(Tone.Secondary)),
                    ("Default", new()),
                    ("Outline", new(Appearance: Appearance.Outline)),
                    ("Ghost", new(Appearance: Appearance.Ghost)),
                    ("Destructive", new(Tone.Danger)),
                    ("Link", new(Appearance: Appearance.Link))
                ];
                foreach (var (label, props) in variants)
                {
                    if (ui.Button(label, props, key: label).Clicked)
                        send(new ShowcaseMsg.Click(label));
                }
            }
            using (c.Div(key: "tones").Row().ItemsCenter().Gap(8))
            {
                // Soft buttons carry their own action names so each control is distinguishable when announced.
                (string Label, Tone Tone, string Icon, string Action)[] tones =
                [
                    ("Success", Tone.Success, "checkmark.circle.fill", "Approve"),
                    ("Warning", Tone.Warning, "exclamationmark.triangle", "Review"),
                    ("Info", Tone.Info, "info.circle", "Details"),
                    ("Danger", Tone.Danger, "trash", "Remove")
                ];
                foreach (var (label, tone, icon, _) in tones)
                {
                    if (ui.Button(label, new(tone, Appearance.Outline, Icon: icon), key: label).Clicked)
                        send(new ShowcaseMsg.Click(label));
                }
                foreach (var (label, tone, _, action) in tones)
                {
                    if (ui.Button(action, new(tone, Appearance.Soft, ControlSize.Small), key: $"soft-{label}").Clicked)
                        send(new ShowcaseMsg.Click(action));
                }
            }
            using (c.Div(key: "sizes").Row().ItemsCenter().Gap(8))
            {
                (string Label, ControlSize Size)[] sizes =
                [
                    ("Extra small", ControlSize.XSmall),
                    ("Small", ControlSize.Small),
                    ("Medium", ControlSize.Medium),
                    ("Large", ControlSize.Large)
                ];
                foreach (var (label, size) in sizes)
                {
                    if (ui.Button(label, new(Tone.Primary, Size: size), key: label).Clicked)
                        send(new ShowcaseMsg.Click(label));
                }
                if (ui.Button("New project", new(Icon: "plus", Pill: true), key: "new").Clicked)
                    send(new ShowcaseMsg.Click("New project"));
                if (ui.IconButton("plus", "Add item", new(Appearance: Appearance.Outline), key: "add").Clicked)
                    send(new ShowcaseMsg.Click("Add item"));
                if (ui.IconButton("pencil", "Edit", new(Appearance: Appearance.Ghost), key: "edit").Clicked)
                    send(new ShowcaseMsg.Click("Edit"));
                ui.Button("Disabled", new(Tone.Primary, Disabled: true), key: "disabled");
            }
            using (c.Div(key: "status").Row().ItemsCenter().Gap(8))
            {
                ui.Muted("Last action:", key: "label");
                ui.Badge($"{m.LastAction} · {m.Clicks}", new(Tone.Default), key: "value");
            }
        }
        Columns(c, "pair", () => Badges(c), () => Alerts(c));
        Columns(c, "people", () => People(c), () => Loading(c));
    }

    static void Badges(Context c)
    {
        var ui = c.Kit;
        using var card = ui.Card(key: "badges").FillWidth();
        ui.CardHeader("Badges & tags", "Solid badges, soft tags and outlines in every tone.", key: "header");
        Tone[] tones = [Tone.Primary, Tone.Secondary, Tone.Success, Tone.Warning, Tone.Info, Tone.Danger];
        foreach (var appearance in new[] { Appearance.Solid, Appearance.Soft, Appearance.Outline })
        {
            using var row = c.Div(key: appearance.ToString()).Row().ItemsCenter().Gap(6);
            foreach (var tone in tones)
                ui.Badge(tone.ToString(), new(tone, appearance, Pill: appearance == Appearance.Soft), key: tone.ToString());
        }
        using (c.Div(key: "extras").Row().ItemsCenter().Gap(8))
        {
            ui.Badge("Verified", new(Tone.Info, Appearance.Soft, Icon: "checkmark.circle.fill"), key: "verified");
            ui.Badge("Beta", new(Tone.Default), key: "beta");
            ui.Kbd("⌘", key: "cmd");
            ui.Kbd("K", key: "k");
            ui.Code("dotnet run", key: "code");
        }
    }

    static void Alerts(Context c)
    {
        var ui = c.Kit;
        using var card = ui.Card(key: "alerts").FillWidth();
        ui.CardHeader("Alerts", "Callouts for contextual feedback.", key: "header");
        ui.Alert("Heads up!", "You can add components to your app using the kit.", key: "default");
        ui.Alert("Deployment complete", "Your changes are live on production.", new(Tone.Success), key: "success");
        ui.Alert("Usage at 85%", "Upgrade to keep builds running.", new(Tone.Warning), key: "warning");
        ui.Alert("Payment failed", "Update your billing details.", new(Tone.Danger), key: "danger");
    }

    static void People(Context c)
    {
        var ui = c.Kit;
        using var card = ui.Card(key: "people").FillWidth();
        ui.CardHeader("Team members", "Invite your team to collaborate.", key: "header");
        using (c.Div(key: "group").Row().ItemsCenter().Gap(12))
        {
            ui.AvatarGroup(Team, 4, key: "avatars");
            ui.Muted($"{Team.Length} members", key: "count");
        }
        string[] roles = ["Owner", "Editor", "Viewer"];
        for (int i = 0; i < roles.Length; i++)
        {
            using var row = c.Div(key: Team[i]).Row().FillWidth().ItemsCenter().Gap(12);
            ui.Avatar(Team[i], key: "avatar");
            using (c.Div(key: "who").Flex().Column().Gap(2))
            {
                ui.Small(Team[i], key: "name");
                ui.Muted($"{Team[i].Split(' ')[0].ToLowerInvariant()}@example.com", key: "email");
            }
            ui.Badge(roles[i], new(i == 0 ? Tone.Primary : Tone.Secondary, i == 0 ? Appearance.Solid : Appearance.Soft), key: "role");
        }
    }

    static void Loading(Context c)
    {
        var ui = c.Kit;
        var t = ui.Theme;
        using var card = ui.Card(key: "loading").FillWidth();
        ui.CardHeader("Progress & placeholders", "Determinate progress and skeleton loading states.", key: "header");
        (string Label, float Value, Tone Tone)[] bars = [("Storage", .62f, Tone.Default), ("Builds", .35f, Tone.Success),
            ("Bandwidth", .85f, Tone.Warning)];
        foreach (var (label, value, tone) in bars)
        {
            using var row = c.Div(key: label).Column().FillWidth().Gap(6);
            using (c.Div(key: "labels").Row().FillWidth())
            {
                ui.Small(label, key: "name");
                c.Spacer(key: "space");
                ui.Muted($"{value * 100:0}%", key: "value");
            }
            ui.Progress(value, new(tone, ControlSize.Small), label, key: "bar");
        }
        ui.Separator("loading", key: "rule");
        using (c.Div(key: "skeleton").Row().FillWidth().ItemsCenter().Gap(12))
        {
            ui.Skeleton(40, 40, circle: true, key: "avatar");
            using (c.Div(key: "lines").Flex().Column().Gap(8))
            {
                ui.Skeleton(Length.Percent(80), 12, key: "line1");
                ui.Skeleton(Length.Percent(55), 12, key: "line2");
            }
        }
        ui.Muted($"Corner radius {t.Radius:0}px · large surfaces {t.RadiusLarge:0}px", key: "radius");
    }
}
