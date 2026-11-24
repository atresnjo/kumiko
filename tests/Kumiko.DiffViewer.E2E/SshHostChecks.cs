using DiffViewer.Sources;
using Kumiko.Icons;
using Kumiko.UI;

namespace DiffViewer.E2E;

/// <summary>
/// SSH hosts in the ⌘K palette, read from an SSH configuration in a temporary home. Runs inside RemoteChecks, whose
/// stand-in ssh client keeps the load a chosen host starts away from any real host.
/// </summary>
internal static class SshHostChecks
{
    /// <param name="remoteHome">The stand-in host's home folder, where its repositories are made.</param>
    public static void Run(Harness h, string scratch, string remoteHome)
    {
        var home = Path.Combine(scratch, "ssh-home");
        var ssh = Path.Combine(home, ".ssh");
        Directory.CreateDirectory(Path.Combine(ssh, "config.d"));
        File.WriteAllText(Path.Combine(ssh, "config"), """
            # Hosts for the palette
            Include config.d/*.conf
            Host devbox
              HostName 203.0.113.10
              User me
            HOST build-1 build-2 *.internal !bastion
              user ci
            Host github.com
              IdentityFile ~/.ssh/work
            Host github-work
              Hostname=github.com
            Host *
              ServerAliveInterval 30
            Match host devbox exec "true"
              User nobody
            Host devbox
              User other
            Include ~/.ssh/missing.conf
            """);
        File.WriteAllText(Path.Combine(ssh, "config.d", "staging.conf"), "Host staging\n    HostName staging.example.invalid\n");
        File.WriteAllText(Path.Combine(ssh, "config.d", "notes.txt"), "Host ignored\n");

        var hosts = SshConfig.Hosts(home);
        h.Check(hosts.Select(x => (x.Alias, x.Address)).SequenceEqual([("staging", "staging.example.invalid"), ("devbox", "me@203.0.113.10"),
            ("build-1", "ci@build-1"), ("build-2", "ci@build-2")]),
            $"SSH hosts come from the config and its includes, in order, without patterns, Match blocks or Git hosts ({string.Join(", ", hosts.Select(x => x.Alias + "=" + x.Address))})");
        h.Check(SshConfig.Hosts(Path.Combine(scratch, "nobody")).IsEmpty, "a home without an SSH configuration lists no hosts");

        var store = new ProjectStore(Path.Combine(scratch, "hosts-state"), scratch, []);
        using var program = Harness.Open(new(SourceKind.Demo, scratch), m => m with
        {
            Store = store,
            Cached = true,
            SshHome = home,
            Recent = ["devbox:/home/me/code/billing", "staging:/srv/api"]
        });
        using var t = new Tester(program, new(1280, 820, DevMode: true));
        t.Context.ReduceMotion = true;
        Model M() => program.Model;
        t.Settle();
        t.Keystroke('k', Modifiers.Primary);
        for (int i = 0; i < 2000 && M().SshHosts.IsEmpty; i++)
        {
            Thread.Sleep(5);
            t.Settle(1);
        }
        var rows = M().Quick!.Items.Where(i => i.Group == "SSH hosts").ToArray();
        h.Check(rows.Select(r => r.Label).SequenceEqual(["staging", "devbox", "build-1", "build-2"]) && rows[1].Detail == "me@203.0.113.10" &&
            rows.All(r => r.Icon == Hugeicons.ComputerTerminal01), "Command+K lists the SSH hosts with their address and a terminal icon");
        h.Artifact(t, "ssh-hosts");
        // The host's repositories: two to find, three a search must not report.
        foreach (var folder in new[] { "code/api/.git", "code/node_modules/dep/.git", ".cache/tool/.git", "a/b/c/d/too-deep/.git" })
            Directory.CreateDirectory(Path.Combine(remoteHome, folder));
        t.TypeText("devb");
        t.Settle();
        var query = M().Quick!.Items;
        var groups = query.Select(i => i.Group).Distinct().ToArray();
        h.Check(query[0].Id == "host:devbox" && groups.Length > 1 && groups.All(g => query.SkipWhile(i => i.Group != g).TakeWhile(i => i.Group == g).Count() == query.Count(i => i.Group == g)),
            $"typing a host's name ranks it first, with each group's rows together ({string.Join(", ", groups)})");
        h.Check(t.HasText("SSH HOSTS") && t.HasText("RECENT"), "a query keeps the group headings");
        h.Artifact(t, "ssh-hosts-query");
        t.Key(Key.Enter);
        t.Settle();
        h.Check(M().Quick is { Query: "devbox:~/" }, "choosing a host keeps the palette open with host:~/ filled in");
        for (int i = 0; i < 2000 && M().HostRepos.GetValueOrDefault("devbox") is not { Loading: false }; i++)
        {
            Thread.Sleep(5);
            t.Settle(1);
        }
        var found = M().HostRepos["devbox"];
        var inHome = found.Paths.Where(p => p.StartsWith('~')).ToArray();
        h.Check(found.Error is null && inHome.SequenceEqual(["~/code/api", "~/o'neil app"]),
            $"the host's repositories are found in one search, not in hidden, dependency or deep folders ({string.Join(", ", inHome)})");
        var picked = M().Quick!;
        h.Check(picked.Items[0].Id == "project:devbox:/home/me/code/billing" && picked.Items.Any(i => i.Id == "project:devbox:~/code/api") &&
            picked.Items[^1].Message is Msg.OpenProject { Path: "devbox:~/" } && picked.Selected == 0,
            "the host's recent checkouts and repositories come before the bare path, the first one highlighted");
        h.Artifact(t, "ssh-host-repos");
        t.TypeText("api");
        t.Settle();
        var narrowed = M().Quick!;
        h.Check(narrowed.Query == "devbox:~/api" && narrowed.Items[0].Id == "project:devbox:~/code/api" &&
            !narrowed.Items.Any(i => i.Id.Contains("o'neil", StringComparison.Ordinal) || i.Id.EndsWith("billing", StringComparison.Ordinal)) &&
            narrowed.Items[^1].Message is Msg.OpenProject { Path: "devbox:~/api" },
            $"typing continues the path after the host and narrows its repositories ({narrowed.Query}: {string.Join(", ", narrowed.Items.Select(i => i.Id))})");
        h.Artifact(t, "ssh-host-path");
        t.Key(Key.Enter);
        h.Check(M() is { Quick: null, Request: { Kind: SourceKind.WorkingTree, Host: "devbox", Path: "~/code/api" } }, "Enter opens the highlighted repository on that host");
        h.Check(M().HostRepos.Count == 1, "each host is searched once while the palette is open");
    }
}
