using Flint.Git;
using Kumiko.UI;
using static Flint.Palette;

namespace Flint;

public sealed partial class FlintView
{
    void RemoteDialog()
    {
        var editor = Model.RemoteEditor!;
        var tracking = editor.Action == GitAction.SetUpstream;
        var title = editor.Action switch
        {
            GitAction.AddRemote => "Add Remote",
            GitAction.SetRemoteUrl => "Edit Remote URL",
            GitAction.RenameRemote => "Rename Remote",
            _ => "Set Upstream"
        };
        var width = MathF.Min(520, C.Size.Width - 48);
        using var overlay = C.Overlay("remote-editor").Items(Align.Center).Padding(new Edges(120, 24, 24, 24)).Background(Overlay);
        var backdrop = overlay.Control();
        if (backdrop.Clicked || C.Shortcut(Key.Escape, Modifiers.None))
            Ui.Dispatch(new Msg.Cancel());
        using var dialog = C.Div(key: editor.Id).Width(width).Padding(20).Gap(12).Rounded(12)
            .Background(Canvas).Border(BorderStrong).Shadow(48, 18, Color.Black.Alpha(.25f)).Semantics(Role.Dialog, title);
        dialog.Control();
        Label(title, "title", 15, weight: Weight.Semibold);
        Label(System.IO.Path.GetFileName(editor.Path) + (editor.Target.Length > 0 ? " · " + editor.Target : ""), "repository", 12, Muted);
        if (tracking)
        {
            UpstreamChoices(editor);
            Button("Cancel", new Msg.Cancel(), "cancel");
            return;
        }
        using var form = Ui.Form("remote-form", new Msg.SaveRemote(), label: title);
        form.Layout.FillWidth().Gap(10);
        if (editor.Action is GitAction.AddRemote or GitAction.RenameRemote)
        {
            Label("Remote name", "name-label", 12, Muted);
            Field("name", "Remote name", editor.Name, static text => new Msg.RemoteNameChanged(text), "origin", width - 40, 28);
        }
        if (editor.Action is GitAction.AddRemote or GitAction.SetRemoteUrl)
        {
            Label("Remote URL", "url-label", 12, Muted);
            if (editor.Loading)
                Label("Loading URL…", "loading", 12, Muted);
            else
                Field("url", "Remote URL", editor.Url, static text => new Msg.RemoteUrlChanged(text), "URL or local repository path", width - 40, 28);
            if (editor.Action == GitAction.SetRemoteUrl)
                C.Text("Changes the fetch URL. An explicitly configured push URL is kept.", new(Size: 12, Color: Muted, Wrap: true), key: "hint");
        }
        if (editor.Error.Length > 0)
            C.Text(editor.Error, new(Size: 12, Color: Danger, Wrap: true), key: "error");
        using var actions = C.Div(key: "actions").FillWidth().Row().Justify(Justify.End).Gap(8).Margin(new Edges(6, 0, 0, 0));
        Button("Cancel", new Msg.Cancel(), "cancel");
        using var submit = C.Div(key: "save").Height(28).Px(12).Center().Rounded(6).Background(Accent);
        var label = editor.Action == GitAction.SetRemoteUrl ? "Save URL" : title;
        var hit = Ui.Submit(submit, label, !editor.Loading);
        if (hit.Focused && C.FocusVisible)
            submit.Border(Accent, 2);
        Label(label, "label", 12, Color.White, Weight.Medium);
    }
    void UpstreamChoices(RemoteEditor editor)
    {
        var snapshot = Model.Repositories.Lookup(editor.Path)?.Snapshot;
        var branch = snapshot?.Refs.FirstOrDefault(r => r.Kind == "branch" && r.Name == editor.Target);
        C.Text(branch?.Upstream is { Length: > 0 } current ? "Currently tracking " + current + (branch.Gone ? " (gone)" : "") : "No upstream configured.",
            new(Size: 12, Color: Muted, Wrap: true), key: "current");
        C.Text("Choose a fetched remote branch. This keeps your current checkout and working changes.",
            new(Size: 12, Color: Muted, Wrap: true), key: "hint");
        var refs = snapshot?.Refs.Where(r => r.Kind == "remote").ToArray() ?? [];
        if (refs.Length == 0)
            C.Text("No remote branches yet. Add a remote and fetch it first.", new(Size: 12, Color: Muted, Wrap: true), key: "empty");
        else
        {
            using var list = C.Div(key: "branches").FillWidth().Height(MathF.Min(280, refs.Length * 30));
            C.UniformList("upstreams", refs.Length, 30, (_, index) =>
            {
                var reference = refs[index];
                Button(reference.Name, new Msg.SaveRemote(reference.Name), "branch", name: "Track " + reference.Name);
            }, rowKey: index => StableId(refs[index].Name));
        }
        if (editor.Error.Length > 0)
            C.Text(editor.Error, new(Size: 12, Color: Danger, Wrap: true), key: "error");
    }
}
