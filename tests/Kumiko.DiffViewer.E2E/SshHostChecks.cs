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
    public static void Run(Harness h, string scratch)
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
            rows.All(r => r.Icon == Hugeicons.CloudServer), "Command+K lists the SSH hosts with their address");
        h.Artifact(t, "ssh-hosts");
        t.TypeText("devb");
        t.Settle();
        h.Check(M().Quick!.Items[0].Id == "host:devbox", "typing a host's name ranks it first");
        t.Key(Key.Enter);
        t.Settle();
        var picked = M().Quick;
        h.Check(picked is { Query: "devbox:~/" } && picked.Items[0].Message is Msg.OpenProject { Path: "devbox:~/" },
            "choosing a host keeps the palette open with host:~/ filled in");
        h.Check(picked!.Items.Length == 2 && picked.Items[1].Id == "project:devbox:/home/me/code/billing",
            "the host's recent checkouts are offered beside the typed path");
        t.TypeText("code");
        t.Settle();
        h.Check(M().Quick!.Query == "devbox:~/code" && M().Quick!.Items.Length == 2, $"typing continues the path after the host ({M().Quick!.Query})");
        h.Artifact(t, "ssh-host-path");
        t.Key(Key.Enter);
        h.Check(M() is { Quick: null, Request: { Kind: SourceKind.WorkingTree, Host: "devbox", Path: "~/code" } }, "Enter opens the path on that host");
    }
}
