using Kumiko.UI;

static class Remotes
{
    public static void Run(Harness h, Repositories fixture)
    {
        void Add()
        {
            h.Tester.Get(Role.Dialog, "Add Remote").Get(Role.Button, "Add Remote").Click();
            h.Idle();
        }
        h.OpenRepository(fixture.Repo);
        var head = Repositories.Git(fixture.Repo, "rev-parse", "HEAD").Trim();
        var status = Repositories.Git(fixture.Repo, "status", "--porcelain");
        h.Click("Add Remote");
        h.Type("Remote name", "origin");
        h.Type("Remote URL", fixture.Origin);
        Add();
        h.Check(Repositories.Git(fixture.Repo, "remote", "get-url", "origin").Trim() == fixture.Origin,
            "Add remote from an empty section writes the repository config");
        h.Menu("Folder: origin", "Edit Remote URL…");
        h.Check(h.Tester.ContainsText(fixture.Origin), "Remote editor loads the current URL");
        h.Type("Remote URL", fixture.Upstream);
        h.Click("Cancel");
        h.Check(Repositories.Git(fixture.Repo, "remote", "get-url", "origin").Trim() == fixture.Origin,
            "Cancelling a remote edit preserves its URL");
        Repositories.Git(fixture.Repo, "remote", "set-url", "--push", "origin", fixture.Origin);
        h.Menu("Folder: origin", "Edit Remote URL…");
        h.Type("Remote URL", fixture.Upstream);
        h.Shot("remote-edit");
        h.Click("Save URL");
        h.Check(Repositories.Git(fixture.Repo, "remote", "get-url", "origin").Trim() == fixture.Upstream
            && Repositories.Git(fixture.Repo, "remote", "get-url", "--push", "origin").Trim() == fixture.Origin,
            "Changing origin updates fetch URL and preserves an explicit push URL");
        Repositories.Git(fixture.Repo, "remote", "add", "unavailable", Path.Combine(fixture.Root, "missing.git"));
        h.Press(Key.R, Modifiers.Primary);
        h.Menu("Folder: origin", "Fetch origin");
        h.Check(h.Model.Error.Length == 0 && h.Tab.Snapshot.Refs.Any(r => r.Name == "origin/main"),
            "Per-remote fetch succeeds without contacting an unavailable second remote");
        Repositories.Git(fixture.Repo, "remote", "remove", "unavailable");
        h.Press(Key.R, Modifiers.Primary);
        h.Type("Filter", "origin/ma");
        h.Check(h.Has(Role.TreeItem, "Remote branch: origin/main"), "Sidebar filtering matches a remote name and branch together");
        h.Type("Filter", "");
        h.Press(Key.Backspace);
        h.Menu("Branch: feature/search", "Set Upstream…", Role.TreeItem);
        h.Shot("remote-upstream");
        h.Tester.Resize(1000, 700);
        h.Tester.Settle();
        h.Shot("remote-upstream-compact");
        h.Tester.Resize(1440, 900);
        h.Tester.Settle();
        h.Click("Track origin/main");
        h.Check(Repositories.Git(fixture.Repo, "rev-parse", "feature/search@{upstream}").Trim()
            == Repositories.Git(fixture.Upstream, "rev-parse", "main").Trim(), "Upstream picker tracks the selected remote branch");
        h.Menu("Folder: origin", "Rename Remote…");
        h.Type("Remote name", "team/origin");
        h.Click("Rename Remote");
        h.Check(h.Tab.Snapshot.Refs.Any(r => r.Name == "feature/search" && r.Upstream == "team/origin/main"),
            "Renaming a remote refreshes branch tracking");
        h.Menu("Folder: team/origin", "Edit Remote URL…");
        h.Check(h.Tester.ContainsText(fixture.Upstream), "Slash-containing remote folder addresses the complete remote name");
        h.Click("Cancel");
        h.Menu("Branch: feature/search", "Remove Upstream", Role.TreeItem);
        h.Check(h.Tab.Snapshot.Refs.Any(r => r.Name == "feature/search" && r.Upstream.Length == 0),
            "Remove upstream clears selected branch tracking");
        h.Menu("Branch: feature/search", "Set Upstream…", Role.TreeItem);
        h.Click("Track team/origin/main");
        Repositories.Git(fixture.Repo, "update-ref", "-d", "refs/remotes/team/origin/main");
        h.Press(Key.R, Modifiers.Primary);
        h.Menu("Branch: feature/search", "Set Upstream…", Role.TreeItem);
        h.Check(h.Tester.ContainsText("(gone)"), "Upstream picker identifies a missing tracking branch");
        h.Click("Cancel");
        h.Menu("Folder: team/origin", "Fetch team/origin");
        h.Click("Add Remote");
        h.Type("Remote name", "team/origin");
        h.Type("Remote URL", fixture.Origin);
        Add();
        h.Check(h.Tester.ContainsText("already exists"), "Duplicate remote names show a recoverable validation error");
        h.Type("Remote name", "--mirror");
        Add();
        h.Check(h.Tester.ContainsText("valid remote name"), "Option-like remote names are rejected");
        h.Type("Remote name", "other");
        h.Type("Remote URL", "");
        h.Press(Key.Backspace);
        Add();
        h.Check(h.Tester.ContainsText("Enter a remote URL"), "Blank remote URLs are rejected");
        h.Click("Cancel");
        h.Menu("Folder: team/origin", "Remove Remote…");
        h.Shot("remote-remove");
        h.Click("Cancel");
        h.Check(Repositories.Git(fixture.Repo, "remote").Trim() == "team/origin", "Cancelling removal keeps the remote");
        h.Menu("Folder: team/origin", "Remove Remote…");
        h.Click("Remove Remote");
        h.Check(Repositories.Git(fixture.Repo, "remote").Trim().Length == 0
            && !h.Tab.Snapshot.Refs.Any(r => r.Kind == "remote")
            && h.Tab.Snapshot.Refs.Any(r => r.Name == "feature/search" && r.Upstream.Length == 0),
            "Confirmed removal refreshes remotes, tracking refs and local branch upstreams");
        h.Check(Repositories.Git(fixture.Repo, "rev-parse", "HEAD").Trim() == head
            && Repositories.Git(fixture.Repo, "branch", "--show-current").Trim() == "main"
            && Repositories.Git(fixture.Repo, "status", "--porcelain") == status,
            "Remote and tracking actions preserve checkout, HEAD and working changes");
        h.Shot("remotes-finished");
    }
}
