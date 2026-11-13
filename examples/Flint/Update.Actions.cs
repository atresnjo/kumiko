using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Flint.Git;
using Flint.Services;
using Kumiko.Runtime;

namespace Flint;

public sealed partial class FlintApp
{
    static (FlintModel, Cmd<Msg>) Request(FlintModel m, GitRequest request)
    {
        if (m.Current is not { } tab)
            return Pure(Notify(m, "Open a repository first (⌘P)", ToastKind.Error));
        var path = tab.Snapshot.Path;
        if (request.Action == GitAction.RemoveRemote)
            return Pure(m with
            {
                Pending = new($"Remove remote \"{request.Value}\"?",
                    "This removes the remote configuration and its remote-tracking branches. Local branches and the remote repository are kept.",
                    path, request, "Remove Remote", Destructive: true),
                Menu = null
            });
        if (request.Action == GitAction.Commit)
        {
            if (string.IsNullOrWhiteSpace(tab.Subject))
                return Pure(Notify(m with
                {
                    Error = "Commit subject is empty"
                }, "Commit subject is empty", ToastKind.Error));
            if (!tab.Amend && tab.Snapshot.Staged.IsEmpty)
                return Pure(Notify(m with
                {
                    Error = "Nothing staged to commit"
                }, "Nothing staged to commit", ToastKind.Error));
            request = request with
            {
                Value = tab.Subject,
                Body = tab.Body,
                Amend = tab.Amend
            };
        }
        if (request.Action is GitAction.Stage or GitAction.Unstage or GitAction.Revert && request.Files is null && tab.Page == Page.Changes)
            return RunSelection(m, request.Action, request.Action == GitAction.Revert ? tab.Staged : request.Action == GitAction.Unstage);
        if (request.Action is GitAction.DeleteWorktree or GitAction.DeleteWorktreeAndBranch)
            return (m with
            {
                Menu = null
            }, AskWorktree(path, request, tab.Snapshot.Worktrees.FirstOrDefault(w => w.Path == request.Value)));
        if (request.Action == GitAction.Revert)
            return Pure(m with
            {
                Pending = RevertDialog(path, request),
                Palette = PaletteMode.Closed
            });
        return Execute(m, path, request);
    }
    /// <summary>Stage/unstage, stash or revert the selected rows; the selection moves to the nearest remaining file.</summary>
    static (FlintModel, Cmd<Msg>) RunSelection(FlintModel m, GitAction action, bool staged)
    {
        if (m.Current is not { } tab)
            return Pure(m);
        var keys = tab.Staged == staged && !tab.Selected.IsEmpty ? tab.Selected : tab.File is { } shown && tab.Staged == staged ? [shown] : [];
        var files = FileTree.Files(staged ? tab.Snapshot.Staged : tab.Snapshot.Unstaged, keys);
        if (files.IsEmpty)
            return Pure(m);
        var request = new GitRequest(action, files, Staged: staged);
        if (action == GitAction.Revert)
            return Pure(m with
            {
                Pending = RevertDialog(tab.Snapshot.Path, request)
            });
        var rows = Rows(tab, staged);
        var last = rows.FindIndex(r => keys.Contains(r.Key));
        for (var i = 0; i < rows.Length; i++)
            if (keys.Contains(rows[i].Key))
                last = i;
        var next = rows.Skip(last + 1).FirstOrDefault(r => !r.Folder && !files.Any(f => f.Path == r.Key)).Key
            ?? rows.Take(Math.Max(0, rows.FindIndex(r => keys.Contains(r.Key)))).LastOrDefault(r => !r.Folder).Key;
        m = Edit(m, r => r with { Selection = next is null ? [] : [next], Anchor = next, Cursor = next, File = next ?? r.File });
        return Execute(m, tab.Snapshot.Path, request);
    }
    static Dialog RevertDialog(string path, GitRequest request)
    {
        var files = request.SelectedFiles;
        var untracked = files.Count(f => f.Status == '?');
        var what = files.Length == 1 ? files[0].Path : Plural(files.Length, "file");
        return new($"Revert {what}?", string.Join(' ', new[]
        {
            request.Staged ? "Staged and unstaged changes go back to the last commit." : "Unstaged changes go back to the staged version.",
            untracked > 0 ? $"{Plural(untracked, "new file")} will be deleted." : "",
            "A copy is kept in Flint's trash folder."
        }.Where(s => s.Length > 0)), path, request, "Revert", Destructive: true);
    }
    static string Plural(int count, string word) => $"{count} {word}{(count == 1 ? "" : "s")}";
    /// <summary>Counts uncommitted changes in the worktree before asking, as the original does.</summary>
    static Cmd<Msg> AskWorktree(string path, GitRequest request, Worktree? tree) => Cmd<Msg>.Run<IGitService>("ask-worktree", async (services, token) =>
    {
        var name = System.IO.Path.GetFileName(request.Value);
        var dirty = tree is null || tree.Prunable ? 0 : await DirtyCount(services, request.Value, token).ConfigureAwait(false);
        var branch = request.Action == GitAction.DeleteWorktreeAndBranch ? tree?.Branch ?? "" : "";
        var message = string.Join(' ', new[]
        {
            dirty > 0 ? $"It has {Plural(dirty, "uncommitted change")} that will be lost." : dirty < 0 ? "Its changes could not be read." : "It has no uncommitted changes.",
            branch.Length > 0 ? $"The branch {branch} is deleted too." : tree?.Branch is { Length: > 0 } kept ? $"The branch {kept} is kept." : "",
            tree?.Locked == true ? "It is locked; the lock is removed." : ""
        }.Where(s => s.Length > 0));
        return new Msg.Ask(new($"Delete worktree \"{name}\"?", message, path, request,
            branch.Length > 0 ? "Delete Worktree & Branch" : "Delete Worktree", Destructive: true));
    }, static e => new Msg.Failed(e.Message));
    static async Task<int> DirtyCount(IGitService git, string path, CancellationToken token)
    {
        try
        {
            var status = await git.Status(path, token).ConfigureAwait(false);
            return status.Staged.Length + status.Unstaged.Length;
        }
        catch (IOException)
        {
            return -1;
        }
    }
    static string Label(GitAction action) => action switch
    {
        GitAction.Commit => "Committing…",
        GitAction.Fetch => "Fetching…",
        GitAction.Pull => "Pulling…",
        GitAction.Push => "Pushing…",
        GitAction.Stash => "Stashing…",
        GitAction.PopStash => "Applying stash…",
        GitAction.Checkout or GitAction.CheckoutRemote or GitAction.Detach => "Checking out…",
        GitAction.NewBranch => "Creating branch…",
        GitAction.DeleteWorktree or GitAction.DeleteWorktreeAndBranch => "Deleting worktree…",
        _ => "Working…"
    };
    /// <summary>One operation per repository at a time; its load generation supersedes earlier loads.</summary>
    static (FlintModel, Cmd<Msg>) Execute(FlintModel m, string path, GitRequest request)
    {
        if (m.Operations.ContainsKey(path))
            return Pure(m);
        var workspace = m.ActiveWorkspace;
        var account = m.Workspaces.FirstOrDefault(w => w.Repositories.Contains(path))?.Account ?? m.Workspace.Account;
        var limit = m.Repositories.Lookup(path)?.Snapshot.Limit ?? 4000;
        var generation = m.Generation + 1;
        var quiet = request.Action is GitAction.Stage or GitAction.Unstage or GitAction.StageAll or GitAction.UnstageAll;
        var removed = request.Action is GitAction.DeleteWorktree or GitAction.DeleteWorktreeAndBranch ? request.Value : null;
        var next = request is { Action: GitAction.Commit, ThenPush: true } ? new GitRequest(GitAction.Push) : null;
        if (removed is not null)
            m = Forget(m, removed);
        m = m with
        {
            Running = m.Operations.SetItem(path, Label(request.Action)),
            Generation = generation,
            Loads = m.Generations.SetItem(path, generation),
            Error = "",
            Palette = PaletteMode.Closed,
            Menu = null
        };
        return (m, Cmd<Msg>.Run<IGitService>("git-" + request.Action, async (services, token) =>
        {
            var notice = await services.Execute(path, request, account, token).ConfigureAwait(false);
            return new Msg.Loaded(workspace, await services.Load(path, limit, token).ConfigureAwait(false), path, true, quiet ? "" : notice,
                generation, removed, next, request.Action);
        }, error =>
        {
            var output = error is ProcessException process ? process.Output : error.Message;
            return new Msg.ActionFailed(path, request, error.Message, output, request.Action is GitAction.Commit or GitAction.Push && HookFailure().IsMatch(output));
        }));
    }
    [GeneratedRegex("hook|husky|lefthook|pre-commit|pre-push|commit-msg|lint-staged|nano-staged", RegexOptions.IgnoreCase)]
    private static partial Regex HookFailure();
    /// <summary>
    /// Short errors become a toast. Long commit/push output opens a dialog with all of it; a hook failure offers an
    /// explicit retry of this one request with --no-verify. The repository is revalidated either way.
    /// </summary>
    static (FlintModel, Cmd<Msg>) ActionFailed(FlintModel m, Msg.ActionFailed failed)
    {
        m = m with
        {
            Running = m.Operations.Remove(failed.Path),
            Error = failed.Error
        };
        var what = failed.Request.Action == GitAction.Commit ? "Commit" : "Push";
        var hook = failed.Hook;
        if (failed.Request.Action is GitAction.Commit or GitAction.Push && (hook || failed.Output.Split('\n').Length > 3))
            m = m with
            {
                Pending = new(hook ? $"{what} blocked by a git hook" : $"{what} failed",
                    hook ? $"A {(what == "Commit" ? "pre-commit / commit-msg" : "pre-push")} hook exited with an error. Fix the issue below, or skip hooks for this {what.ToLowerInvariant()} (git --no-verify)."
                        : $"git {what.ToLowerInvariant()} exited with an error.",
                    failed.Path, hook ? failed.Request with
                    {
                        NoVerify = true
                    } : null, hook ? $"{what} Without Hooks" : "OK", "Close", Details: failed.Output)
            };
        else
            m = Notify(m, failed.Error, ToastKind.Error, failed.Output);
        if (!m.Repositories.ContainsKey(failed.Path))
            return Pure(m);
        return Load(m, failed.Path, m.Repositories[failed.Path].Snapshot.Limit, TimeSpan.Zero);
    }
    static (FlintModel, Cmd<Msg>) CreateWorkspace(FlintModel m, string name)
    {
        var id = "workspace-" + (m.Sequence + 1);
        while (m.Workspaces.Any(w => w.Id == id))
        {
            m = m with
            {
                Sequence = m.Sequence + 1
            };
            id = "workspace-" + (m.Sequence + 1);
        }
        m = m with
        {
            Workspaces = m.Workspaces.Add(new(id, string.IsNullOrWhiteSpace(name) ? "Workspace" : name.Trim(), [])),
            ActiveWorkspace = id,
            Sequence = m.Sequence + 1
        };
        return ShowPalette(m, PaletteMode.Open, null);
    }
    static (FlintModel, Cmd<Msg>) SelectWorkspace(FlintModel m, string id)
    {
        if (!m.Workspaces.Any(w => w.Id == id))
            return Pure(m);
        m = m with
        {
            ActiveWorkspace = id,
            Palette = PaletteMode.Closed
        };
        var (activated, command) = Activate(m);
        var commands = ImmutableArray.CreateBuilder<Cmd<Msg>>();
        commands.Add(command);
        foreach (var path in activated.Workspace.Repositories)
            if (activated.Repositories.TryGetValue(path, out var tab) && !activated.Operations.ContainsKey(path))
            {
                (activated, var refresh) = Load(activated, path, tab.Snapshot.Limit, TimeSpan.FromMilliseconds(32));
                commands.Add(refresh);
            }
        return (activated, Cmd<Msg>.Batch([.. commands]));
    }
    static FlintModel RenameWorkspace(FlintModel m, string id, string name) => string.IsNullOrWhiteSpace(name)
        ? m with
        {
            Palette = PaletteMode.Closed
        } : EditWorkspace(m, id, w => w with { Name = name.Trim() }) with
        {
            Palette = PaletteMode.Closed
        };
    /// <summary>Removes the grouping only; the repositories on disk are untouched.</summary>
    static (FlintModel, Cmd<Msg>) DeleteWorkspace(FlintModel m, string id)
    {
        if (m.Workspaces.Length <= 1)
            return Pure(Notify(m, "Cannot delete the last workspace", ToastKind.Error));
        if (m.Workspaces.FirstOrDefault(w => w.Id == id) is not { } workspace)
            return Pure(m);
        var remaining = m.Workspaces.Remove(workspace);
        var used = remaining.SelectMany(w => w.Repositories).ToImmutableHashSet(StringComparer.Ordinal);
        m = Notify(m with
        {
            Workspaces = remaining,
            Repositories = m.Repositories.RemoveRange(m.Repositories.Keys.Where(p => !used.Contains(p))),
            Armed = null,
            Highlight = 0
        }, $"Deleted workspace \"{workspace.Name}\"", ToastKind.Info);
        return m.ActiveWorkspace == id ? SelectWorkspace(m, remaining[0].Id) : Pure(m);
    }
    static FlintModel SetAccount(FlintModel m, string id, string login)
    {
        var name = m.Workspaces.FirstOrDefault(w => w.Id == id)?.Name ?? "Workspace";
        m = EditWorkspace(m, id, w => w with { Account = login }) with
        {
            Palette = PaletteMode.Closed
        };
        return Notify(m, login.Length > 0 ? $"{name} now pushes as {login}" : $"{name} uses gh's active account", ToastKind.Success);
    }
}
