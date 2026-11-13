using Kumiko.Runtime;
using Kumiko.UI;

namespace Launcher;

public sealed class LauncherScreen(LauncherModel model) : Screen<LauncherModel, LauncherMsg>(model)
{
    protected override void Render()
    {
        var theme = C.Theme;
        var composing = C.IsComposing;
        using var root = C.Div(key: "launcher").SizeFull().Background(theme.Surface).Padding(Space.S4).Gap(Space.S3);
        // Capture composition before TextField processes Escape/Enter; the IME owns the entire input turn.
        if (!composing && C.Shortcut(Key.Escape, Modifiers.None))
            Ui.Dispatch(new LauncherMsg.Dismiss());
        if (Model.FocusSearch && !Model.Settings)
        {
            C.FocusNextControl();
            Ui.Dispatch(new LauncherMsg.FocusedSearch());
        }
        if (Model.Settings)
            Settings();
        else
        {
            Search(composing);
            Results();
        }
        if (Model.Error is { } error)
        {
            using var notice = C.Div(key: "error").FillWidth().Padding(Space.S3).Rounded(Radius.Md).Background(theme.ErrorSurface).Semantics(Role.Alert, error);
            C.Text(error, Typography.Caption with
            {
                Color = theme.Text,
                Wrap = true
            }, key: "message");
        }
        Footer();
    }

    void Search(bool composing)
    {
        var focused = C.Accessibility.FirstOrDefault(n => n.Id == C.Focus);
        if (!composing && focused?.Name == "Search applications")
        {
            if (C.Shortcut(Key.Down, Modifiers.None))
                Ui.Dispatch(new LauncherMsg.Move(1));
            if (C.Shortcut(Key.Up, Modifiers.None))
                Ui.Dispatch(new LauncherMsg.Move(-1));
        }
        using var form = Ui.Form("search-form", new LauncherMsg.Open(), "Launch selected application");
        form.Layout.Row().ItemsCenter().Gap(Space.S3);
        C.Icon("magnifyingglass", size: 22, color: C.Theme.Muted, key: "search-icon");
        Ui.TextField("query", "Search applications", Model.Query, static text => new LauncherMsg.Query(text),
            new(Placeholder: "Search applications…", Width: Math.Max(120, C.Size.Width - 170), Height: 44));
        // The form supplies accessible submission and IME-safe Enter handling.
        Ui.Submit("open", "Open", new(Disabled: Model.Loading || Model.Results.Length == 0));
    }

    void Results()
    {
        var results = Model.Results;
        using (var heading = C.Div(key: "list-heading").Row().FillWidth().ItemsCenter())
        {
            C.Text(Model.Query.Length == 0 ? "Applications" : "Search results", Typography.Label, key: "label");
            using (C.Div(key: "spacer").Flex())
            {
            }
            C.Text(Model.Loading ? "Loading" : $"{results.Length} available", Typography.Caption with
            {
                Color = C.Theme.Muted
            }, key: "count");
        }
        using var list = C.Div(key: "applications").FillWidth().Flex().Scroll().Gap(Space.S1).Semantics(Role.List, "Applications");
        if (Model.Loading)
            State("Finding your applications", "The catalog loads in the background.");
        else if (results.Length == 0)
        {
            State(Model.Entries.Length == 0 ? "No applications available" : "No matching applications",
                Model.Entries.Length == 0 ? "Refresh to scan again, or open Settings." : "Try a shorter name or a different spelling.");
            Ui.Button("Refresh applications", new LauncherMsg.Refresh(), key: "refresh-empty");
        }
        else
            for (var i = 0; i < results.Length; i++)
                Row(results[i], i == Model.Selected);
    }

    void Row(Kumiko.Desktop.InstalledApplication app, bool selected)
    {
        using var row = C.Div(key: app.Id).Row().FillWidth().Height(58).Padding(Space.S3).Gap(Space.S3).ItemsCenter().Rounded(Radius.Md)
            .Background(selected ? C.Theme.Selection : Color.Transparent);
        if (selected)
            row.Reveal(Model.SelectionRequest);
        row.Accessibility(new(Selected: selected));
        var hit = Ui.Button(row, "Launch " + app.Name, new LauncherMsg.Open(app.Id), true);
        if (hit.Hovered && !selected)
            row.Background(C.Theme.SurfaceHover);
        if (C.FocusVisible && hit.Focused)
            row.Border(C.Theme.FocusRing, 2);
        C.ApplicationIcons.BeginFrame(C, C.FrameId);
        if (C.ApplicationIcons.Get(app.Path) is { } image)
            C.Image(image, 32, 32, key: "icon");
        else
            C.Icon("app", 32, C.Theme.Muted, key: "icon");
        if (C.ApplicationIcons.Pending)
            C.FrameIn(TimeSpan.FromSeconds(1 / 60d));
        using (var text = C.Div(key: "details").Flex().Gap(2))
        {
            C.Text(app.Name, Typography.Label, key: "name");
            C.Text(System.IO.Path.GetFileName(app.Path), Typography.Caption with
            {
                Color = selected ? Color.Mix(C.Theme.Text, C.Theme.Muted, .35f) : C.Theme.Muted
            }, key: "path");
        }
        if (selected)
            C.Text("Enter to open", Typography.Caption with
            {
                Color = Color.Mix(C.Theme.Text, C.Theme.Muted, .35f)
            }, key: "hint");
    }

    void State(string title, string detail)
    {
        using var state = C.Div(key: "state").FillWidth().Padding(Space.S5).Gap(Space.S2).Semantics(Role.Group, title);
        C.Text(title, Typography.Title, key: "title");
        C.Text(detail, Typography.Body with
        {
            Color = C.Theme.Muted,
            Wrap = true
        }, key: "detail");
    }

    void Settings()
    {
        C.Text("Launcher settings", Typography.Title, key: "title");
        using var settings = C.Div(key: "settings").FillWidth().Flex().Gap(Space.S4);
        C.Text("Summon from any application", Typography.Label, key: "summon-label");
        Ui.TextField("shortcut", "Global shortcut", Model.ShortcutDraft, static text => new LauncherMsg.ShortcutDraft(text),
            new(Width: Math.Max(200, C.Size.Width - 48)), showLabel: true);
        using (var actions = C.Div(key: "shortcut-actions").Row().Gap(Space.S2).ItemsCenter())
        {
            Ui.Button("Apply shortcut", new LauncherMsg.SaveShortcut(), key: "apply");
            C.Text("Current: " + Model.Shortcut, Typography.Caption with
            {
                Color = C.Theme.Muted
            }, key: "current");
        }
        if (Model.Startup.State is Kumiko.Desktop.StartupState.Unsupported or Kumiko.Desktop.StartupState.OwnedByAnother)
            C.Text("Start at login unavailable", Typography.Label, key: "startup");
        else
            Ui.Checkbox("Start at login", Model.Startup.State is Kumiko.Desktop.StartupState.Enabled or Kumiko.Desktop.StartupState.RequiresApproval,
                static enabled => new LauncherMsg.Startup(enabled), key: "startup");
        C.Text(Model.Startup.Detail ?? "Changes apply only when you use this control.", Typography.Caption with
        {
            Color = C.Theme.Muted,
            Wrap = true
        }, key: "startup-detail");
        Ui.Button("Refresh applications", new LauncherMsg.Refresh(), key: "refresh");
        Ui.Button("Quit launcher", new LauncherMsg.Quit(), key: "quit");
    }

    void Footer()
    {
        using var footer = C.Div(key: "footer").Row().FillWidth().ItemsCenter().Gap(Space.S2);
        C.Text(Model.Settings ? "Escape returns to search" : "↑ ↓ select   ·   Enter opens   ·   Escape dismisses", Typography.Caption with
        {
            Color = C.Theme.Muted
        }, key: "keys");
        using (C.Div(key: "spacer").Flex())
        {
        }
        Ui.Button(Model.Settings ? "Back" : "Settings", new LauncherMsg.ShowSettings(), key: "settings");
    }
}
