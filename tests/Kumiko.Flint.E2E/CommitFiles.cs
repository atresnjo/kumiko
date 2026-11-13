using System.Collections.Immutable;
using Flint;
using Flint.Git;
using Kumiko.UI;

/// <summary>
/// An initial commit can add thousands of files. The Commit tab lists them under the message in one scroller,
/// so each scroll frame must build the visible rows only, not every file.
/// </summary>
static class CommitFiles
{
    public static void Run(Harness h, IServiceProvider services, string output)
    {
        var tab = h.Tab;
        ImmutableArray<FileChange> files = [.. Enumerable.Range(0, 2000).Select(i => new FileChange($"examples/CRMLite/Model{i:D4}.cs", 'A'))];
        var detail = tab.Detail! with
        {
            Files = files
        };
        var model = FlintApp.Init with
        {
            Workspaces = [new("default", "Default", [tab.Snapshot.Path], tab.Snapshot.Path)],
            Repositories = ImmutableDictionary<string, RepoTab>.Empty.Add(tab.Snapshot.Path, tab with
            {
                Sha = detail.Sha,
                Detail = detail,
                ChangesTab = false,
                File = null
            }),
            Watching = false
        };
        using var program = FlintApp.CreateProgram(model, services);
        try
        {
            using var tester = new Tester(program, new(1440, 900));
            tester.Context.ReduceMotion = true;
            tester.Settle();
            bool Visible(string path) => tester.Context.Accessibility.Any(n => n.Role == Role.Button && n.Name == "File: " + path && !n.Offscreen);
            var first = tester.Get(Role.Button, "File: " + files[0].Path).Rect;
            tester.Screenshot(Path.Combine(output, "commit-files-top.png"));
            h.Check(tester.Context.Stats.Nodes < 2000, $"A 2,000-file commit builds only visible file rows ({tester.Context.Stats.Nodes} nodes)");
            tester.MoveTo(first.X + 40, first.Y + first.Height / 2);
            tester.Scroll(23 * 1000);
            tester.Settle();
            h.Check(tester.Context.Stats.Nodes < 2000 && !Visible(files[0].Path) && files.Skip(1000).Take(20).Any(f => Visible(f.Path)),
                "Scrolling the commit file list shows the rows in the viewport");
            tester.Scroll(1_000_000);
            tester.Settle();
            h.Check(Visible(files[^1].Path), "The last file of a large commit is reachable");
            tester.Screenshot(Path.Combine(output, "commit-files-2000.png"));
            // The scroll bar sits on the window's right edge: pressing its track at the top jumps there, and
            // dragging the thumb down again reaches the end, without selecting a file under the pointer.
            var top = tester.Get(Role.Button, "Commit").Rect.Bottom + 8;
            tester.Press(1436, top);
            tester.Release(1436, top);
            h.Check(Visible(files[0].Path) && program.Model.Current?.File is null, "Pressing the commit list's scroll track jumps there and clicks no file");
            tester.Press(1436, top + 4);
            tester.MoveTo(1436, 899);
            tester.Release(1436, 899);
            h.Check(Visible(files[^1].Path) && program.Model.Current?.File is null, "Dragging the commit list's scroll thumb reaches the last file");
            tester.Get(Role.Button, "File: " + files[^1].Path).Click();
            tester.Settle();
            h.Check(program.Model.Current?.File == files[^1].Path, "A virtual file row stays clickable");
        }
        finally
        {
            program.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }
}
