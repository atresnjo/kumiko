using System.Collections.Immutable;
using Flint.Git;
using Flint.Services;
using Kumiko.Runtime;

namespace Flint;

public sealed partial class FlintApp
{
    static (FlintModel, Cmd<Msg>) Navigate(FlintModel m, Page page)
    {
        m = Edit(m, r => r with { Page = page, Finding = false });
        if (page == Page.History && m.Current is { } tab && tab.Snapshot.Commits.Length > 0)
            return SelectCommit(m, tab.Sha ?? tab.Snapshot.Commits[0].Sha);
        return m.Workspace.Active is { } path ? ShowWorkingFile(m, path) : Pure(m);
    }
    static (FlintModel, Cmd<Msg>) SelectCommit(FlintModel m, string sha, string? path = null)
    {
        path ??= m.Workspace.Active;
        if (path is null || !m.Repositories.TryGetValue(path, out var tab))
            return Pure(m);
        var root = path;
        var request = tab.Request + 1;
        m = m with
        {
            Repositories = m.Repositories.SetItem(root, tab with
            {
                Sha = sha,
                RevealHistory = true,
                Detail = tab.Detail?.Sha == sha ? tab.Detail : null,
                File = null,
                Patch = "",
                Rows = [],
                SearchResults = [],
                Match = 0,
                Request = request
            })
        };
        var commit = tab.Snapshot.Commits.FirstOrDefault(c => c.Sha == sha);
        return (m, Cmd<Msg>.Debounce<IGitService>("detail:" + root, TimeSpan.Zero, async (services, token) =>
        {
            var (detail, patch) = commit is not null ? await services.Commit(root, commit, token).ConfigureAwait(false)
                : await Sequential(services, root, sha, token).ConfigureAwait(false);
            return new Msg.DetailLoaded(root, request, detail, patch, Diff.Parse(patch));
        }, error => new Msg.Failed(error.Message, root, request)));
    }
    /// <summary>A commit outside the loaded history: its header names the parents the patch needs.</summary>
    static async Task<(CommitDetail, string)> Sequential(IGitService git, string root, string sha, CancellationToken token)
    {
        var detail = await git.Detail(root, sha, token).ConfigureAwait(false);
        return (detail, await git.Patch(root, detail, null, false, token).ConfigureAwait(false));
    }
    static (FlintModel, Cmd<Msg>) DetailLoaded(FlintModel m, Msg.DetailLoaded d)
    {
        if (!m.Repositories.TryGetValue(d.Path, out var tab) || tab.Request != d.Request)
            return Pure(m);
        var preload = tab.Amend && tab.Subject.Length == 0 && tab.Body.Length == 0 && d.Detail.Sha == tab.Snapshot.Head.Sha;
        return Pure(m with
        {
            Repositories = m.Repositories.SetItem(d.Path, tab with
            {
                Detail = d.Detail,
                Patch = d.Patch,
                Rows = d.Lines,
                SearchResults = Diff.Matches(d.Lines, tab.Find),
                Subject = preload ? d.Detail.Subject : tab.Subject,
                Body = preload ? d.Detail.Body : tab.Body
            })
        });
    }
    static (FlintModel, Cmd<Msg>) PatchLoaded(FlintModel m, Msg.PatchLoaded p) =>
        m.Repositories.TryGetValue(p.Path, out var tab) && tab.Request == p.Request
            ? Pure(m with
            {
                Repositories = m.Repositories.SetItem(p.Path, tab with
                {
                    Patch = p.Patch,
                    Rows = p.Lines,
                    SearchResults = Diff.Matches(p.Lines, tab.Find),
                    RevealDiff = true
                })
            }) : Pure(m);
    static (FlintModel, Cmd<Msg>) Amend(FlintModel m, bool on)
    {
        m = Edit(m, r => r with { Amend = on });
        if (!on || m.Current is not { Subject.Length: 0, Body.Length: 0 } tab || tab.Snapshot.Head.Unborn)
            return Pure(m);
        var root = tab.Snapshot.Path;
        var sha = tab.Snapshot.Head.Sha;
        var request = tab.Request;
        return (m, Cmd<Msg>.Debounce<IGitService>("amend:" + root, TimeSpan.Zero, async (services, token) =>
        {
            var detail = await services.Detail(root, sha, token).ConfigureAwait(false);
            return new Msg.AmendLoaded(root, detail.Subject, detail.Body);
        }, error => new Msg.Failed(error.Message, root, request)));
    }
    static (FlintModel, Cmd<Msg>) StepCommit(FlintModel m, int delta)
    {
        if (m.Current is not { } tab || tab.Snapshot.Commits.IsEmpty)
            return Pure(m);
        var index = tab.Snapshot.Commits.FindIndex(c => c.Sha == tab.Sha);
        var target = delta == int.MinValue ? 0 : delta == int.MaxValue ? tab.Snapshot.Commits.Length - 1 : Math.Clamp(index + delta, 0, tab.Snapshot.Commits.Length - 1);
        return SelectCommit(m, tab.Snapshot.Commits[target].Sha);
    }
    /// <summary>Rows of one file section in visible order.</summary>
    static ImmutableArray<TreeRow> Rows(RepoTab tab, bool staged) =>
        FileTree.Rows(staged ? tab.Snapshot.Staged : tab.Snapshot.Unstaged, tab.Folders, staged ? "staged:" : "unstaged:");
    static (FlintModel, Cmd<Msg>) SelectFile(FlintModel m, Msg.SelectFile selection)
    {
        if (m.Current is not { } tab)
            return Pure(m);
        var path = tab.Snapshot.Path;
        if (tab.Page == Page.History)
        {
            m = m with
            {
                Repositories = m.Repositories.SetItem(path, tab with
                {
                    File = selection.Path,
                    ChangesTab = true
                })
            };
            return LoadPatch(m, path, selection.Path is null ? null : tab.Detail?.Files.FirstOrDefault(f => f.Path == selection.Path), false);
        }
        if (selection.Path is not { } key)
            return Pure(m);
        var sameSection = tab.Staged == selection.Staged;
        var selected = ImmutableArray.Create(key);
        var anchor = key;
        if (selection.Extend && sameSection)
            selected = tab.Selected.Contains(key) ? tab.Selected.Remove(key) : tab.Selected.Add(key);
        else if (selection.Range && sameSection && tab.Anchor is { } fixedAnchor)
        {
            var keys = Rows(tab, selection.Staged).Select(r => r.Key).ToImmutableArray();
            var from = keys.IndexOf(fixedAnchor);
            var to = keys.IndexOf(key);
            if (from >= 0 && to >= 0)
            {
                selected = keys.Slice(Math.Min(from, to), Math.Abs(from - to) + 1);
                anchor = fixedAnchor;
            }
        }
        tab = tab with
        {
            Staged = selection.Staged,
            Selection = selected,
            Anchor = anchor,
            Cursor = key
        };
        if (key.EndsWith('/') && !selection.Extend && !selection.Range)
        {
            var folder = (selection.Staged ? "staged:" : "unstaged:") + key[..^1];
            tab = tab with
            {
                Collapsed = tab.Folders.Contains(folder) ? tab.Folders.Remove(folder) : tab.Folders.Add(folder)
            };
        }
        m = m with
        {
            Repositories = m.Repositories.SetItem(path, tab)
        };
        return key.EndsWith('/') ? Pure(m) : ShowFile(m, path, key, selection.Staged);
    }
    static (FlintModel, Cmd<Msg>) ShowFile(FlintModel m, string path, string file, bool staged)
    {
        var tab = m.Repositories[path];
        var change = (staged ? tab.Snapshot.Staged : tab.Snapshot.Unstaged).FirstOrDefault(f => f.Path == file);
        m = m with
        {
            Repositories = m.Repositories.SetItem(path, tab with
            {
                File = file,
                Staged = staged
            })
        };
        return change is null ? Pure(m) : LoadPatch(m, path, change, staged);
    }
    /// <summary>
    /// The diff pane is never empty when something changed: keep the shown file, or fall back to the first unstaged
    /// and then the first staged change, like the original. Reloads the diff for fresh status.
    /// </summary>
    static (FlintModel, Cmd<Msg>) ShowWorkingFile(FlintModel m, string path)
    {
        var tab = m.Repositories[path];
        var snapshot = tab.Snapshot;
        var kept = tab.File is { } file && (tab.Staged ? snapshot.Staged : snapshot.Unstaged).Any(f => f.Path == file);
        var (shown, staged) = kept ? (tab.File!, tab.Staged) : snapshot.Unstaged.Length > 0 ? (snapshot.Unstaged[0].Path, false)
            : snapshot.Staged.Length > 0 ? (snapshot.Staged[0].Path, true) : ("", false);
        if (shown.Length == 0)
            return Pure(m with
            {
                Repositories = m.Repositories.SetItem(path, tab with
                {
                    File = null,
                    Patch = "",
                    Rows = [],
                    SearchResults = []
                })
            });
        if (!kept)
            m = m with
            {
                Repositories = m.Repositories.SetItem(path, tab with
                {
                    Staged = staged,
                    Selection = [],
                    Cursor = null
                })
            };
        return ShowFile(m, path, shown, staged);
    }
    static (FlintModel, Cmd<Msg>) LoadPatch(FlintModel m, string path, FileChange? file, bool staged)
    {
        var tab = m.Repositories[path];
        var request = tab.Request + 1;
        var detail = tab.Page == Page.History ? tab.Detail : null;
        m = m with
        {
            Repositories = m.Repositories.SetItem(path, tab with
            {
                Request = request,
                Match = 0
            })
        };
        if (detail is null && file is null)
            return Pure(m);
        return (m, Cmd<Msg>.Debounce<IGitService>("patch:" + path, TimeSpan.Zero,
            async (services, token) =>
            {
                var patch = await services.Patch(path, detail, file, staged, token).ConfigureAwait(false);
                return new Msg.PatchLoaded(path, request, patch, Diff.Parse(patch));
            },
            error => new Msg.Failed(error.Message, path, request)));
    }
    /// <summary>Arrow, Home and End movement in a file section. Shift extends from the fixed anchor.</summary>
    static (FlintModel, Cmd<Msg>) StepFile(FlintModel m, int delta, bool extend, bool staged)
    {
        if (m.Current is not { } tab)
            return Pure(m);
        var keys = Rows(tab, staged).Select(r => r.Key).ToImmutableArray();
        if (keys.IsEmpty)
            return Pure(m);
        var at = tab.Staged == staged ? keys.IndexOf(tab.Cursor ?? tab.File ?? "") : -1;
        var index = delta == int.MinValue ? 0 : delta == int.MaxValue ? keys.Length - 1 : Math.Clamp(at < 0 && delta < 0 ? 0 : at + delta, 0, keys.Length - 1);
        var key = keys[index];
        m = Edit(m, r => r with { RevealFiles = true });
        if (extend && tab.Staged == staged && tab.Anchor is not null)
            return SelectFile(m, new(key, staged, Range: true));
        var (next, command) = SelectFile(m, new(key, staged));
        // Moving onto a folder selects it without toggling, as in the original's keyboard model.
        return key.EndsWith('/') ? (Edit(next, r => r with { Collapsed = tab.Collapsed }), command) : (next, command);
    }
    static RepoTab SelectAll(RepoTab tab) => tab with
    {
        Selection = [.. Rows(tab, tab.Staged).Select(r => r.Key)]
    };
    static FlintModel OpenMenu(FlintModel model, Msg.OpenMenu menu)
    {
        if (menu.Kind == MenuKind.Files && model.Current is { } tab && (!tab.Selected.Contains(menu.Target) || tab.Staged != menu.Staged))
            model = Edit(model, r => r with { Selection = [menu.Target], Staged = menu.Staged, Anchor = menu.Target, Cursor = menu.Target });
        return model with
        {
            Menu = new(menu.At, menu.Kind, menu.Target, menu.Staged),
            Palette = PaletteMode.Closed
        };
    }
}
