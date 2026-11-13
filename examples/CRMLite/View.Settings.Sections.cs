using System.Collections.Immutable;
using CRMLite.Components;
using Kumiko.UI;
using static CRMLite.Palette;

namespace CRMLite;

public sealed partial class CrmView
{
    /// <summary>One setting: its control is chosen by <paramref name="Key"/>; keywords widen search.</summary>
    sealed record SettingRow(string Key, string Title, string Description, string Keywords = "");

    static readonly ImmutableArray<SettingRow> GeneralRows =
    [
        new("workspace", "Workspace name", "Shown in the sidebar and on invoices.", "rename title"),
        new("language", "Language", "Used for menus, dates and numbers.", "locale"),
        new("timezone", "Time zone", "Due dates and reminders follow this zone.", "clock"),
        new("start", "Start page", "Where CRMLite opens.", "home landing")
    ];

    static readonly ImmutableArray<SettingRow> AppearanceRows =
    [
        new("theme", "Theme", "Light or dark surfaces everywhere.", "dark mode light mode"),
        new("accent", "Accent color", "Buttons, selection and highlights.", "color brand"),
        new("sidebar", "Collapsed sidebar", "Keep the sidebar as an icon rail.", "rail navigation"),
        new("tilt", "Card tilt", "How deal cards lean while you drag them.", "drag angle board animation")
    ];

    static readonly ImmutableArray<SettingRow> NotificationRows =
    [
        new("desktop-alerts", "Desktop notifications", "Agent plans that finish or need you, and edits that fail to save, while CRMLite is in the background.",
            "banner alert notification center macos"),
        new("mentions", "Mentions", "When someone @mentions you or your team.", "email"),
        new("assigned", "Assigned to me", "New companies and deals that land on you.", "owner email"),
        new("stages", "Deal stage changes", "When a deal you own moves stage.", "pipeline email"),
        new("digest", "Weekly digest", "A Monday summary of your pipeline.", "email summary"),
        new("delivery", "Email delivery", "Batch emails to cut down on noise.", "email batch")
    ];

    static readonly ImmutableArray<SettingRow> SecurityRows =
    [
        new("two-step", "Require two-step sign in", "Everyone adds an authenticator app or key.", "2fa mfa"),
        new("saml", "Enforce SAML single sign-on", "Members sign in through Okta only.", "sso okta"),
        new("devices", "Approve new devices", "An admin confirms sign ins from unknown devices.", "login"),
        new("session", "Session length", "How long members stay signed in.", "timeout"),
        new("domains", "Allowed email domains", "Invites outside these domains are blocked.", "invite")
    ];

    static ImmutableArray<SettingRow> MemberRows
    {
        get;
    } =
        [.. Data.Team.Select(member => new SettingRow("member:" + member.Id, member.Name, member.Email, "role team people"))];

    static readonly ImmutableArray<SettingRow> BillingRows =
    [
        new("plan", "Business plan", "$29 per seat each month, billed monthly.", "subscription seats usage"),
        new("payment", "Payment method", "Visa ending in 4242, expires 08/28.", "card"),
        new("billing-email", "Billing email", "Receipts and invoices go here.", "receipt"),
        new("invoices", "Invoices", "Issued on the first of each month.", "receipts history")
    ];

    static ImmutableArray<SettingRow> RowsOf(SettingsSection section) => section switch
    {
        SettingsSection.Appearance => AppearanceRows,
        SettingsSection.Notifications => NotificationRows,
        SettingsSection.Security => SecurityRows,
        SettingsSection.Members => MemberRows,
        SettingsSection.Billing => BillingRows,
        _ => GeneralRows
    };
    void SettingsSectionBody(SettingsPage page)
    {
        using (C.Div(key: "heading").FillWidth().Column().Gap(4).Padding(new Edges(0, 0, 8, 0)).Semantics(Role.Heading, page.Title))
        {
            C.Text(page.Title, new(Size: TextSize.Title + 2, Weight: Weight.Medium, Color: p.Text), key: "title");
            C.Text(page.Description, new(Size: TextSize.Setting - .5f, Color: p.Secondary), key: "description");
        }
        var rows = RowsOf(page.Section).Where(row => RowMatches(page.Section, row)).ToImmutableArray();
        for (var i = 0; i < rows.Length; i++)
        {
            var row = rows[i];
            using (C.Div(key: row.Key).FillWidth().Column().Gap(14).Py(18))
            {
                using (C.Div(key: "line").Row().FillWidth().ItemsCenter().Gap(Space.S5))
                {
                    if (row.Key.StartsWith("member:", StringComparison.Ordinal))
                        Avatar(Crm.Member(row.Key["member:".Length..]), 32, "avatar");
                    using (C.Div(key: "text").Flex().Column().Gap(4))
                    {
                        var you = row.Key == "member:" + Model.CurrentUser.Id ? " (you)" : "";
                        Label(row.Title + you, TextSize.Setting, p.Text, key: "title");
                        C.Text(row.Description, new(Size: TextSize.Body + .5f, Color: p.Secondary, Wrap: true), key: "description");
                        if (row.Key == "desktop-alerts" && DesktopStatus() is { } status)
                            C.Text(status, new(Size: TextSize.Body + .5f, Color: p.Stale, Wrap: true), key: "status");
                    }
                    SettingControl(row);
                }
                if (row.Key == "invoices")
                    Invoices();
            }
            if (i < rows.Length - 1)
                Rule("rule-" + row.Key);
        }
    }
    void SettingControl(SettingRow row)
    {
        var pref = Model.Preferences;
        CrmMsg With(Preferences next) => new CrmMsg.SetPreferences(next);
        switch (row.Key)
        {
            case "workspace":
                Ui.TextField(key: "input", label: "Workspace name", value: pref.Workspace, onChange: text => With(pref with { Workspace = text }),
                    options: new("Workspace name", 260));
                break;
            case "language":
                SettingSelect(row, pref.Language, ["English (US)", "English (UK)", "Deutsch", "Français", "Español"], value => With(pref with { Language = value }));
                break;
            case "timezone":
                SettingSelect(row, pref.TimeZone, ["Pacific Time (US & Canada)", "Eastern Time (US & Canada)", "London", "Berlin", "Tokyo"], value => With(pref with { TimeZone = value }));
                break;
            case "start":
                SettingSelect(row, pref.StartPage, ["Inbox", "Companies", "Deals"], value => With(pref with { StartPage = value }));
                break;
            case "theme":
                SegmentedChoice.View(Ui, p, "Theme", Model.Dark,
                    [new Segment<bool>("Light", "Light", false), new Segment<bool>("Dark", "Dark", true)],
                    static dark => new CrmMsg.SetDark(dark), key: "segmented");
                break;
            case "accent":
                Swatches();
                break;
            case "sidebar":
                ToggleSwitch.View(Ui, p, row.Title, ForcedRail || Model.SidebarCollapsed, new CrmMsg.ToggleSidebar(), ToggleVariant.Settings, key: "switch", enabled: !ForcedRail);
                break;
            case "tilt":
                SegmentedChoice.View(Ui, p, row.Title, pref.Tilt,
                    [.. Enum.GetValues<CardTilt>().Select(t => new Segment<CardTilt>(t.ToString(), t.ToString(), t))],
                    value => With(pref with { Tilt = value }), key: "segmented");
                break;
            case "desktop-alerts":
                ToggleSwitch.View(Ui, p, row.Title, pref.DesktopAlerts, With(pref with
                {
                    DesktopAlertsOff = pref.DesktopAlerts
                }), ToggleVariant.Settings, key: "switch");
                break;
            case "mentions":
                ToggleSwitch.View(Ui, p, row.Title, pref.Mentions, With(pref with
                {
                    Mentions = !pref.Mentions
                }), ToggleVariant.Settings, key: "switch");
                break;
            case "assigned":
                ToggleSwitch.View(Ui, p, row.Title, pref.Assigned, With(pref with
                {
                    Assigned = !pref.Assigned
                }), ToggleVariant.Settings, key: "switch");
                break;
            case "stages":
                ToggleSwitch.View(Ui, p, row.Title, pref.StageChanges, With(pref with
                {
                    StageChanges = !pref.StageChanges
                }), ToggleVariant.Settings, key: "switch");
                break;
            case "digest":
                ToggleSwitch.View(Ui, p, row.Title, pref.WeeklyDigest, With(pref with
                {
                    WeeklyDigest = !pref.WeeklyDigest
                }), ToggleVariant.Settings, key: "switch");
                break;
            case "delivery":
                SettingSelect(row, pref.Delivery, ["Instantly", "Hourly digest", "Daily digest"], value => With(pref with { Delivery = value }));
                break;
            case "two-step":
                ToggleSwitch.View(Ui, p, row.Title, pref.TwoStep, With(pref with
                {
                    TwoStep = !pref.TwoStep
                }), ToggleVariant.Settings, key: "switch");
                break;
            case "saml":
                ToggleSwitch.View(Ui, p, row.Title, pref.Saml, With(pref with
                {
                    Saml = !pref.Saml
                }), ToggleVariant.Settings, key: "switch");
                break;
            case "devices":
                ToggleSwitch.View(Ui, p, row.Title, pref.DeviceApproval, With(pref with
                {
                    DeviceApproval = !pref.DeviceApproval
                }), ToggleVariant.Settings, key: "switch");
                break;
            case "session":
                SettingSelect(row, pref.SessionLength, ["1 hour", "8 hours", "24 hours", "7 days", "30 days"], value => With(pref with { SessionLength = value }));
                break;
            case "domains":
                Ui.TextField(key: "input", label: "Allowed email domains", value: pref.Domains, onChange: text => With(pref with { Domains = text }),
                    options: new("example.com", 260));
                break;
            case "plan":
                PlanUsage();
                break;
            case "payment":
                CardMark();
                break;
            case "billing-email":
                Ui.TextField(key: "input", label: "Billing email", value: pref.BillingEmail,
                    onChange: text => With(pref with { BillingEmail = text }), options: new("name@company.com", 260));
                break;
            case var key when key.StartsWith("member:", StringComparison.Ordinal):
                var id = key["member:".Length..];
                if (pref.RoleOf(id) == MemberRole.Owner)
                {
                    using (C.Div(key: "owner").Height(28).Px(10).Center().Rounded(Corners.Control).Background(p.Lane))
                        Label("Owner", TextSize.Body, p.Secondary, key: "text");
                    break;
                }
                var roles = pref.Roles ?? ImmutableSortedDictionary<string, MemberRole>.Empty;
                SettingSelect(row with
                {
                    Title = $"{row.Title} role"
                }, pref.RoleOf(id).ToString(), ["Admin", "Member"], value => With(pref with { Roles = roles.SetItem(id, Enum.Parse<MemberRole>(value)) }), 140);
                break;
        }
    }

    /// <summary>Why desktop notifications cannot arrive in this host, if they cannot.</summary>
    string? DesktopStatus() => Model.Desktop.Access switch
    {
        NoticeAccess.Unavailable => "Unavailable here. Run the packaged CRMLite app to receive them.",
        NoticeAccess.Denied => "Turned off for CRMLite in System Settings › Notifications.",
        _ => null
    };
}
