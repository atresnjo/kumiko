using System.Collections.Immutable;
using DiffViewer.Core;
using DiffViewer.Sources;
using Kumiko.Icons;

namespace DiffViewer;

/// <summary>A palette row: a project, worktree or command, and the message it runs.</summary>
internal sealed record QuickItem(string Id, string Label, string Detail, string Group, string Icon, Msg Message, string Hint = "");

/// <summary>
/// The ⌘K palette, Flint's Quick Launch for this app: worktrees of the open project, recent projects, pull requests
/// of the gh accounts, the open project's commits, every discovered project and commands. A leading <c>&gt;</c>
/// lists commands only, <c>#</c> pull requests only and <c>@</c> commits only. Items are rebuilt by updates when the
/// query or a source changes, so frames only read them.
/// </summary>
internal sealed record QuickOpen(string Query = "", int Selected = 0)
{
    public ImmutableArray<QuickItem> Items { get; init; } = [];

    public const int Limit = 200;
    /// <summary>Rows the palette shows at once, a window around the highlight.</summary>
    public const int Rows = 14;

    /// <summary>The first row of the window the palette shows.</summary>
    public static int FirstVisible(int selected, int count) => Math.Clamp(selected - Rows / 2, 0, Math.Max(0, count - Rows));
    /// <summary>Commits listed before anything is typed; typing or <c>@</c> searches all of them.</summary>
    const int RecentCommits = 10;

    public static ImmutableArray<QuickItem> Build(Model model, string query)
    {
        var scope = query.Length > 0 && query[0] is '>' or '#' or '@' ? query[0] : ' ';
        var text = (scope == ' ' ? query : query[1..]).Trim();
        IEnumerable<QuickItem> pool = scope switch
        {
            '>' => Commands(model),
            '#' => PullItems(model),
            '@' => CommitItems(model),
            _ when text.Length == 0 => [.. ProjectItems(model, true), .. PullItems(model), .. CommitItems(model).Take(RecentCommits),
                .. ProjectItems(model, false), .. Commands(model)],
            _ => [.. PathItem(model, text), .. ProjectItems(model, true), .. ProjectItems(model, false), .. PullItems(model),
                .. CommitItems(model), .. Commands(model)]
        };
        if (text.Length == 0)
            return [.. pool.Take(Limit)];
        if (text.StartsWith('/') || text.StartsWith('~'))
            return [.. pool.Take(1)];
        return [.. pool.Select(item => (Item: item, Score: Score(text, item)))
            .Where(r => r.Score >= 0).OrderByDescending(r => r.Score).Take(Limit).Select(r => r.Item)];
    }

    static double Score(string query, QuickItem item)
    {
        var label = Fuzzy(query, item.Label);
        var hint = item.Hint.Length > 0 ? Fuzzy(query, item.Hint) - 100 : -1;
        var detail = item.Detail.Length > 0 ? Fuzzy(query, item.Detail) - 300 : -1;
        var score = Math.Max(label, Math.Max(hint, detail));
        // Recent projects win ties against other projects with the same name.
        return score < 0 ? score : score + (item.Group == "Recent" ? 25 : 0);
    }

    /// <summary>A typed absolute or home-relative path opens as a project.</summary>
    static IEnumerable<QuickItem> PathItem(Model model, string text)
    {
        if (!(text.StartsWith('/') || text.StartsWith('~')))
            yield break;
        var path = text.StartsWith('~') && model.Store is { } store ? store.Home + text[1..] : text;
        yield return new("path:" + path, "Open " + text, "", "Open", Hugeicons.FolderOpen, new Msg.OpenProject(path));
    }

    /// <summary>
    /// Worktrees of the open project and recent projects (<paramref name="near"/>), or every other project, each
    /// listed once. With an empty query the groups are listed in order; with a query they are ranked together.
    /// </summary>
    static IEnumerable<QuickItem> ProjectItems(Model model, bool near)
    {
        var current = model.ProjectPath;
        var family = model.Projects.FirstOrDefault(p => p.Path == current)?.Family ?? current;
        var siblings = model.Projects.Where(p => p.Family == family).ToImmutableArray();
        var worktrees = siblings.Length > 1 ? siblings.Select(p => Item(model, p, "Worktrees")) : [];
        var recent = model.Recent.Select(path => Item(model, model.Projects.FirstOrDefault(p => p.Path == path) ?? new(path, Path.GetFileName(path), ""), "Recent"));
        var shown = worktrees.Concat(recent).DistinctBy(item => item.Id, StringComparer.Ordinal).ToImmutableArray();
        if (near)
            return shown;
        var listed = shown.Select(item => item.Id).ToImmutableHashSet(StringComparer.Ordinal);
        return model.Projects.Select(p => Item(model, p, "Projects")).Where(item => !listed.Contains(item.Id));
    }

    /// <summary>Open pull requests of the gh accounts; a failed search shows its reason as a row that retries.</summary>
    static IEnumerable<QuickItem> PullItems(Model model)
    {
        const string group = "Pull requests";
        if (model.Pulls.Error is { } error)
            yield return new("pulls:error", "Pull requests unavailable", error, group, Hugeicons.Alert02, new Msg.RefreshPulls());
        foreach (var pull in model.Pulls.Items)
        {
            var current = model.Request.Pull?.Url == pull.Url;
            var icon = current ? Hugeicons.Tick02 : pull.Draft ? Hugeicons.GitPullRequestDraft : Hugeicons.GitPullRequest;
            var detail = $"{pull.Repository} · {pull.Author}{(pull.Draft ? " · draft" : "")}";
            yield return new("pull:" + pull.Url, pull.Title, current ? "current · " + detail : detail, group, icon, new Msg.OpenPull(pull),
                "#" + pull.Number.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    /// <summary>The open project's newest commits; the one being shown is marked.</summary>
    static IEnumerable<QuickItem> CommitItems(Model model)
    {
        if (model.CommitsOf != model.ProjectPath)
            yield break;
        foreach (var commit in model.Commits)
        {
            var current = model.Request is { Kind: SourceKind.Revisions } request && request.Target == commit.Sha;
            yield return new("commit:" + commit.Sha, commit.Subject, (current ? "current · " : "") + commit.Author + " · " + commit.When, "Commits",
                current ? Hugeicons.Tick02 : Hugeicons.GitCommit, new Msg.OpenCommit(commit), commit.Short);
        }
    }

    static QuickItem Item(Model model, Project project, string group)
    {
        var current = project.Path == model.ProjectPath;
        var path = model.Store?.Tilde(project.Path) ?? project.Path;
        var icon = current ? Hugeicons.Tick02 : project.Worktree ? Hugeicons.GitFork : Hugeicons.FolderGit;
        return new("project:" + project.Path, project.Name, current ? "current · " + path : path, group, icon, new Msg.OpenProject(project.Path), project.Branch);
    }

    static IEnumerable<QuickItem> Commands(Model model)
    {
        const string group = "Commands";
        var staged = model.Request.Kind == SourceKind.Staged;
        yield return new("cmd:working", "Show Working Tree Changes", staged ? "" : "current", group, Hugeicons.FolderOpen,
            new Msg.SetSource(SourceKind.WorkingTree));
        yield return new("cmd:staged", "Show Staged Changes", staged ? "current" : "", group, Hugeicons.GitCommit, new Msg.SetSource(SourceKind.Staged));
        yield return new("cmd:unified", "Unified View", "", group, Hugeicons.Layout01, new Msg.SetMode(ViewMode.Unified), "⌘1");
        yield return new("cmd:split", "Split View", "", group, Hugeicons.Layout01, new Msg.SetMode(ViewMode.Split), "⌘2");
        yield return new("cmd:whitespace", model.IgnoreWhitespace ? "Show Whitespace Changes" : "Ignore Whitespace", "", group,
            Hugeicons.Search01, new Msg.ToggleWhitespace(), "w");
        yield return new("cmd:file", "Go to File…", "", group, Hugeicons.Search01, new Msg.OpenFinder(), "⌘P");
        yield return new("cmd:search", "Search Lines…", "", group, Hugeicons.Search01, new Msg.OpenSearch(), "⌘F");
        yield return new("cmd:fold", "Fold All Files", "", group, Hugeicons.Layout01, new Msg.SetAllCollapsed(true), "⇧X");
        yield return new("cmd:unfold", "Unfold All Files", "", group, Hugeicons.Layout01, new Msg.SetAllCollapsed(false), "⇧X");
        yield return new("cmd:zen", model.Zen is null ? "Enter Zen Mode" : "Leave Zen Mode", "", group, Hugeicons.FullScreen, new Msg.ToggleZen(), "z");
        yield return new("cmd:sidebar", "Toggle Sidebar", "", group, Hugeicons.SidebarLeft, new Msg.ToggleSidebar(), "⌘B");
        yield return new("cmd:reload", "Reload", "", group, Hugeicons.Refresh, new Msg.Reload(), "⌘R");
        if (model.GitHub is not null)
        {
            yield return new("cmd:pulls", "Pull Requests…", "", group, Hugeicons.GitPullRequest, new Msg.OpenQuick("#"), "#");
            yield return new("cmd:refresh-pulls", "Refresh Pull Requests", model.Pulls.Loading ? "loading" : "", group, Hugeicons.Refresh, new Msg.RefreshPulls());
        }
        yield return new("cmd:commits", "Commits…", "", group, Hugeicons.GitCommit, new Msg.OpenQuick("@"), "@");
        yield return new("cmd:rescan", "Rescan Projects", "", group, Hugeicons.Refresh, new Msg.Rescan());
        yield return new("cmd:help", "Keyboard Shortcuts", "", group, Hugeicons.CommandLine, new Msg.SetHelp(true), "?");
    }

    /// <summary>Flint's subsequence score: contiguous and word-start matches rank higher; -1 means no match.</summary>
    public static double Fuzzy(string query, string target)
    {
        if (query.Length == 0)
            return 0;
        var q = query.ToLowerInvariant();
        var t = target.ToLowerInvariant();
        static bool Boundary(string text, int at) => at == 0 || text[at - 1] is ' ' or '/' or '_' or '-' or '.';
        var direct = t.IndexOf(q, StringComparison.Ordinal);
        if (direct >= 0)
            return 1000 - direct * 2 - (t.Length - q.Length) * .1 + (Boundary(t, direct) ? 200 : 0);
        double score = 0;
        var at = 0;
        var streak = 0;
        foreach (var ch in q)
        {
            if (ch == ' ')
                continue;
            var found = t.IndexOf(ch, at);
            if (found < 0)
                return -1;
            streak = found == at ? streak + 1 : 0;
            score += 10 + streak * 5 + (Boundary(t, found) ? 15 : 0) - Math.Min(found - at, 10);
            at = found + 1;
        }
        return score - t.Length * .1;
    }
}
