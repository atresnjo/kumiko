using Flint.Git;
using Flint.Services;
using Kumiko.Runtime;

namespace Flint;

public sealed partial class FlintApp
{
    static (FlintModel, Cmd<Msg>) EditRemote(FlintModel m, Msg.EditRemote edit)
    {
        if (m.Current is not { } tab || m.Operations.ContainsKey(tab.Snapshot.Path))
            return Pure(m with
            {
                Menu = null
            });
        var id = m.Sequence + 1;
        var loading = edit.Action == GitAction.SetRemoteUrl;
        m = m with
        {
            Sequence = id,
            Menu = null,
            Palette = PaletteMode.Closed,
            Pending = null,
            RemoteEditor = new(id, tab.Snapshot.Path, edit.Action, edit.Target, edit.Target, Loading: loading)
        };
        return (m, loading ? Cmd<Msg>.Run<IGitService>("remote-url", async (git, token) =>
            new Msg.RemoteUrlLoaded(id, await git.RemoteUrl(tab.Snapshot.Path, edit.Target, token).ConfigureAwait(false)),
            error => new Msg.RemoteUrlLoaded(id, "", error.Message)) : Cmd<Msg>.None);
    }
    static (FlintModel, Cmd<Msg>) SaveRemote(FlintModel m, string upstream)
    {
        if (m.RemoteEditor is not { Loading: false } editor || !m.Repositories.TryGetValue(editor.Path, out var tab)
            || m.Operations.ContainsKey(editor.Path))
            return Pure(m);
        var name = editor.Name.Trim();
        var url = editor.Url.Trim();
        var error = "";
        if (editor.Action is GitAction.AddRemote or GitAction.RenameRemote)
        {
            if (name.Length == 0 || name[0] == '-' || name.Any(char.IsWhiteSpace) || name.Any(char.IsControl))
                error = "Enter a valid remote name.";
            else if (tab.Snapshot.Remotes.Contains(name))
                error = "A remote with this name already exists.";
        }
        if (error.Length == 0 && editor.Action is GitAction.AddRemote or GitAction.SetRemoteUrl
            && (url.Length == 0 || url[0] == '-' || url.Any(char.IsControl)))
            error = "Enter a remote URL or repository path.";
        if (editor.Action == GitAction.SetUpstream && !tab.Snapshot.Refs.Any(r => r.Kind == "remote" && r.Name == upstream))
            error = "Choose a fetched remote branch.";
        if (error.Length > 0)
            return Pure(m with
            {
                RemoteEditor = editor with
                {
                    Error = error
                }
            });
        var value = editor.Action == GitAction.AddRemote ? name : editor.Target;
        var body = editor.Action == GitAction.RenameRemote ? name : editor.Action == GitAction.SetUpstream ? upstream : url;
        return Execute(m with
        {
            RemoteEditor = null
        }, editor.Path, new(editor.Action, Value: value, Body: body));
    }
}
