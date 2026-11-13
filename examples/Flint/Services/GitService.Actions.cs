using System.Diagnostics;
using System.Globalization;
using System.Text;
using Flint.Git;

namespace Flint.Services;

public sealed partial class GitService
{
    public async Task<string> Execute(string path, GitRequest request, string account, CancellationToken token)
    {
        var gate = gates.GetOrAdd(path, static _ => new(1, 1));
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            return await Mutate(path, request, account, token).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }
    async Task<string> Mutate(string path, GitRequest request, string account, CancellationToken token)
    {
        var paths = request.SelectedFiles.IsEmpty ? [] : request.SelectedFiles.SelectMany(f => f.OriginalPath is { } original ? new[] { f.Path, original } : [f.Path]).Distinct().ToArray();
        var verify = request.NoVerify ? new[] { "--no-verify" } : [];
        var args = new List<string>();
        var hooks = false;
        string notice;
        switch (request.Action)
        {
            case GitAction.Stage:
                args.AddRange(["add", "-A", "--", .. paths]);
                notice = "Staged";
                break;
            case GitAction.StageAll:
                args.AddRange(["add", "-A"]);
                notice = "Staged all changes";
                break;
            case GitAction.Unstage:
            case GitAction.UnstageAll:
                var unborn = (await Status(path, token).ConfigureAwait(false)).Head.Unborn;
                args.AddRange(unborn ? ["rm", "--cached", "-r", "-q"] : ["reset", "-q", "HEAD"]);
                args.Add("--");
                args.AddRange(request.Action == GitAction.UnstageAll ? ["."] : paths);
                notice = "Unstaged";
                break;
            case GitAction.Commit:
                if (string.IsNullOrWhiteSpace(request.Value))
                    throw new ArgumentException("Commit subject is empty");
                args.AddRange(["commit", "-q", "-m", request.Value.Trim()]);
                if (!string.IsNullOrWhiteSpace(request.Body))
                    args.AddRange(["-m", request.Body.Trim()]);
                if (request.Amend)
                    args.Add("--amend");
                args.AddRange(verify);
                hooks = true;
                notice = request.Amend ? "Commit amended" : "Committed: " + request.Value.Trim();
                break;
            case GitAction.Fetch:
                if (request.Value.Length > 0)
                    ValidateRevision(request.Value);
                args.AddRange(request.Value.Length > 0 ? ["fetch", "--prune", "--tags", "--", request.Value] : ["fetch", "--all", "--prune", "--tags"]);
                notice = request.Value.Length > 0 ? "Fetched " + request.Value : "Fetched";
                break;
            case GitAction.Pull:
                args.AddRange(["pull", "--no-edit", "--autostash"]);
                hooks = true;
                notice = "Pulled";
                break;
            case GitAction.Push:
                // A configured upstream decides the remote; only a branch's first push sets origin as upstream.
                var head = (await Status(path, token).ConfigureAwait(false)).Head;
                if (head.Detached || head.Branch.Length == 0)
                    throw new InvalidOperationException("Check out a branch before pushing.");
                if (head.Upstream.Length > 0)
                    args.AddRange(["push", .. verify]);
                else
                    args.AddRange(["push", .. verify, "-u", await PushRemote(path, token).ConfigureAwait(false), head.Branch]);
                hooks = true;
                notice = "Pushed";
                break;
            case GitAction.Stash:
                args.AddRange(["stash", "push", "-u"]);
                if (paths.Length > 0)
                    args.AddRange(["-m", "Flint: " + (paths.Length == 1 ? paths[0] : paths.Length + " files"), "--", .. paths]);
                notice = paths.Length > 0 ? "Stashed " + (paths.Length == 1 ? paths[0] : paths.Length + " files") : "Changes stashed";
                break;
            case GitAction.PopStash:
                ValidateRevision(request.Value);
                args.AddRange(["stash", "pop", request.Value]);
                notice = "Popped " + request.Value;
                break;
            case GitAction.Checkout:
                ValidateRevision(request.Value);
                args.AddRange(["switch", request.Value]);
                notice = "Switched to " + request.Value;
                break;
            case GitAction.CheckoutRemote:
                ValidateRevision(request.Value);
                var local = await LocalName(path, request.Value, token).ConfigureAwait(false);
                // An existing local branch is reused rather than recreated, matching the original.
                var exists = (await runner.Run("git", path, ["rev-parse", "--verify", "--quiet", "refs/heads/" + local], token, allowDifference: true).ConfigureAwait(false)).Length > 0;
                args.AddRange(exists ? ["switch", local] : ["switch", "--track", "-c", local, request.Value]);
                notice = "Switched to " + local;
                break;
            case GitAction.Detach:
                ValidateRevision(request.Value);
                args.AddRange(["switch", "--detach", request.Value]);
                notice = "HEAD detached at " + request.Value[..Math.Min(7, request.Value.Length)];
                break;
            case GitAction.NewBranch:
                ValidateRevision(request.Value.Trim());
                await Git(path, token, "check-ref-format", "--branch", request.Value.Trim()).ConfigureAwait(false);
                args.AddRange(["switch", "-c", request.Value.Trim()]);
                if (request.Body.Length > 0)
                {
                    ValidateRevision(request.Body);
                    args.Add(request.Body);
                }
                notice = "Switched to new branch " + request.Value.Trim();
                break;
            case GitAction.Revert:
                return await Revert(path, request, token).ConfigureAwait(false);
            case GitAction.AddRemote:
            case GitAction.SetRemoteUrl:
            case GitAction.RenameRemote:
            case GitAction.RemoveRemote:
            case GitAction.SetUpstream:
            case GitAction.RemoveUpstream:
                return await ConfigureRemote(path, request, token).ConfigureAwait(false);
            case GitAction.PruneWorktrees:
                args.AddRange(["worktree", "prune"]);
                notice = "Pruned missing worktrees";
                break;
            case GitAction.DeleteWorktree:
            case GitAction.DeleteWorktreeAndBranch:
                return await RemoveWorktree(path, request, token).ConfigureAwait(false);
            default:
                throw new ArgumentOutOfRangeException(nameof(request));
        }
        var environment = new Dictionary<string, string>();
        // Hooks (linters, tests, formatters) keep the user's locale; parsing commands stay in C.
        if (hooks)
            environment["LC_ALL"] = Environment.GetEnvironmentVariable("LC_ALL") ?? "";
        if (account.Length > 0 && request.Action is GitAction.Fetch or GitAction.Pull or GitAction.Push)
        {
            var auth = await AccountAuth(account, token).ConfigureAwait(false);
            args.InsertRange(0, auth.Config.SelectMany(c => new[] { "-c", c }));
            foreach (var (key, value) in auth.Environment)
                environment[key] = value;
        }
        await runner.Run("git", path, args, token, environment).ConfigureAwait(false);
        return account.Length > 0 && request.Action is GitAction.Fetch or GitAction.Pull or GitAction.Push ? notice + " as " + account : notice;
    }
    async Task<string> PushRemote(string path, CancellationToken token)
    {
        var remotes = (await Git(path, token, "remote").ConfigureAwait(false)).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return remotes.Contains("origin") || remotes.Length != 1 ? "origin" : remotes[0];
    }
    /// <summary>origin/feature/x names local branch feature/x; remotes themselves may contain slashes.</summary>
    async Task<string> LocalName(string path, string remoteBranch, CancellationToken token)
    {
        var remotes = (await Git(path, token, "remote").ConfigureAwait(false)).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var remote = remotes.Where(r => remoteBranch.StartsWith(r + "/", StringComparison.Ordinal)).MaxBy(r => r.Length)
            ?? remoteBranch[..Math.Max(0, remoteBranch.IndexOf('/'))];
        return remoteBranch[(remote.Length + 1)..];
    }
    /// <summary>
    /// Removes a worktree without waiting on its files: rename it aside on the same volume, prune Git's metadata, then
    /// delete the renamed folder in a detached low-priority process, as the original does.
    /// </summary>
    async Task<string> RemoveWorktree(string path, GitRequest request, CancellationToken token)
    {
        var started = Stopwatch.GetTimestamp();
        var trees = Parse.Worktrees(await Git(path, token, "worktree", "list", "--porcelain", "-z").ConfigureAwait(false));
        var target = trees.FirstOrDefault(w => w.Path == request.Value);
        if (target is null || target == trees[0] || target.Bare)
            throw new InvalidOperationException("The main worktree cannot be removed.");
        var main = trees[0].Path;
        if (target.Locked)
            await Git(main, token, "worktree", "unlock", target.Path).ConfigureAwait(false);
        if (!target.Prunable)
        {
            var trash = Path.Combine(Path.GetDirectoryName(target.Path)!, ".flint-trash-" + Path.GetFileName(target.Path) + "-"
                + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString("x", CultureInfo.InvariantCulture));
            try
            {
                Directory.Move(target.Path, trash);
                Discard(trash);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                // Another volume or a permission problem: fall back to Git's slower removal.
                await Git(main, token, "worktree", "remove", "--force", "--force", "--", target.Path).ConfigureAwait(false);
            }
        }
        await Git(main, token, "worktree", "prune").ConfigureAwait(false);
        if (request.Action == GitAction.DeleteWorktreeAndBranch && target.Branch.Length > 0)
            await Git(main, token, "branch", "-D", "--", target.Branch).ConfigureAwait(false);
        var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        return string.Create(CultureInfo.InvariantCulture, $"Deleted worktree {Path.GetFileName(target.Path)} in {elapsed:0}ms");
    }
    static void Discard(string directory)
    {
        if (OperatingSystem.IsWindows())
        {
            _ = Task.Run(() =>
            {
                try
                {
                    Directory.Delete(directory, true);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
            });
            return;
        }
        var start = new ProcessStartInfo("nice") { UseShellExecute = false, RedirectStandardInput = false, CreateNoWindow = true };
        foreach (var argument in new[] { "-n", "15", "rm", "-rf", "--", directory })
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start);
    }
    async Task<string> Revert(string path, GitRequest request, CancellationToken token)
    {
        if (request.SelectedFiles.IsEmpty)
            return "No files selected";
        var backup = Path.Combine(stateDirectory, "trash", Path.GetFileName(path) + "-" + DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH-mm-ss-fff", CultureInfo.InvariantCulture));
        foreach (var file in request.SelectedFiles)
        {
            var source = Path.GetFullPath(Path.Combine(path, file.Path));
            if (!source.StartsWith(path + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                throw new IOException("File is outside the repository.");
            var info = new FileInfo(source);
            if (!info.Exists && info.LinkTarget is null)
                continue;
            var dest = Path.Combine(backup, file.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            if (info.LinkTarget is { } target)
                File.CreateSymbolicLink(dest, target);
            else
                File.Copy(source, dest, true);
        }
        var unborn = (await Status(path, token).ConfigureAwait(false)).Head.Unborn;
        var untracked = request.SelectedFiles.Where(f => f.Status == '?').Select(f => f.Path).ToArray();
        var tracked = request.SelectedFiles.Where(f => f.Status != '?').Select(f => f.Path).ToArray();
        if (!request.Staged)
        {
            if (untracked.Length > 0)
                await runner.Run("git", path, ["clean", "-f", "-q", "--", .. untracked], token).ConfigureAwait(false);
            if (tracked.Length > 0)
                await runner.Run("git", path, ["restore", "--worktree", "--", .. tracked], token).ConfigureAwait(false);
        }
        else
        {
            // Files absent from HEAD (added, or a rename's new side) are removed; others return to HEAD.
            var added = request.SelectedFiles.Where(f => unborn || f.Status is 'A' or 'R' or 'C').Select(f => f.Path).ToArray();
            var restore = unborn ? [] : request.SelectedFiles.Where(f => !added.Contains(f.Path)).Select(f => f.Path)
                .Concat(request.SelectedFiles.Where(f => f.Status == 'R' && f.OriginalPath is not null).Select(f => f.OriginalPath!)).ToArray();
            if (added.Length > 0)
                await runner.Run("git", path, ["rm", "-f", "-q", "-r", "--", .. added], token).ConfigureAwait(false);
            if (restore.Length > 0)
                await runner.Run("git", path, ["restore", "--source=HEAD", "--staged", "--worktree", "--", .. restore], token).ConfigureAwait(false);
        }
        var what = request.SelectedFiles.Length == 1 ? request.SelectedFiles[0].Path : request.SelectedFiles.Length + " files";
        return "Reverted " + what;
    }
    async Task<(string[] Config, Dictionary<string, string> Environment)> AccountAuth(string login, CancellationToken token)
    {
        Directory.CreateDirectory(stateDirectory);
        var credential = (await runner.Run("gh", stateDirectory, ["auth", "token", "--hostname", "github.com", "--user", login], token).ConfigureAwait(false)).Trim();
        if (credential.Length == 0)
            throw new IOException($"GitHub account {login}: gh has no token. Run `gh auth login` for it or pick another account.");
        // The credential is passed in the child environment, never argv, model, cache or event history.
        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes(login + ":" + credential));
        return (["url.https://github.com/.insteadOf=git@github.com:", "url.https://github.com/.insteadOf=ssh://git@github.com/"],
            new()
            {
                ["GIT_CONFIG_COUNT"] = "1",
                ["GIT_CONFIG_KEY_0"] = "http.https://github.com/.extraheader",
                ["GIT_CONFIG_VALUE_0"] = "Authorization: Basic " + basic
            });
    }
}
