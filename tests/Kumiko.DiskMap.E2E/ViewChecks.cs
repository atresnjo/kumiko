using System.Collections.Immutable;
using DiskMap.Core;
using Kumiko.UI;

namespace DiskMap.E2E;

/// <summary>
/// Pointer and keyboard behavior through Kumiko input on the demo tree, a real scan through the subscription, the
/// Trash flow against a recording desk, and light and dark artifacts.
/// </summary>
internal static class ViewChecks
{
    public static void Run(Harness h)
    {
        Navigation(h);
        Marks(h);
        Trash(h);
        Scanning(h);
    }

    /// <summary>Clicks a tile near its top-left corner, on its header when it is a frame.</summary>
    static (float X, float Y) At(Tester t, Tile tile)
    {
        var map = t.Get(Role.Group, "Treemap").Rect;
        return (map.X + tile.X + 12, map.Y + tile.Y + 9);
    }

    static void Navigation(Harness h)
    {
        var (program, desk) = Harness.Demo();
        using var _ = program;
        using var t = new Tester(program, new(1360, 860, DevMode: true, Theme: Theme.DarkTheme));
        t.Context.ReduceMotion = true;
        t.Settle(3);
        Model M() => program.Model;
        h.Check(M().Width > 0 && !M().Layout.Tiles.IsEmpty, "the view reports its size and the map is laid out for it");
        h.Artifact(t, "home-dark");
        var cargo = M().Layout.Tiles.First(t => t.Item.Name == ".cargo");
        var (x, y) = At(t, cargo);
        t.Click(x, y);
        h.Check(M().Selected?.Name == ".cargo" && M().Trail.Length == 1, "clicking a folder's header selects it");
        h.Check(t.HasText(".cargo") && t.ContainsText("~/.cargo"), "the sidebar shows the selected folder and its path");
        t.Click(x, y);
        h.Check(M().Folder!.Value.Name == ".cargo" && M().Selected is null, "clicking the selected folder opens it");
        h.Check(M().Glide && M().Layout.Tiles[0].Item.Name == "targets", "opening glides the map to the folder's contents");
        t.Key(Key.Escape);
        h.Check(M().Trail.Length == 1 && M().Selected?.Name == ".cargo", "Escape goes back and selects the folder it came from");
        var back = M().Items.IndexOf(M().Selected!.Item);
        t.Key(Key.Down);
        h.Check(M().Selected?.Item == M().Items[back + 1], "Down selects the next listed item");
        t.Key(Key.Escape);
        h.Check(M().Selected is null, "Escape at the root clears the selection");
        t.Key(Key.Down);
        h.Check(M().Selected?.Item == M().Items[0], "Down without a selection selects the first listed item");
        t.Key(Key.Down);
        t.Key(Key.Up);
        h.Check(M().Selected?.Item == M().Items[0], "Up steps back through the list");
        t.Key(Key.Enter);
        h.Check(M().Folder!.Value.Name == M().Trail[^1].Name && M().Trail.Length == 2, "Enter opens the selected folder");
        program.Send(new Msg.Open([".zsh_history"]));
        t.Settle();
        h.Check(M().Trail.Length == 2, "an item that is not a child of the open folder opens nothing");
        program.Send(new Msg.Crumb(0));
        t.Settle();
        program.Send(new Msg.Open([".zsh_history"]));
        t.Settle();
        h.Check(M().Trail.Length == 1 && M().Selected?.Name == ".zsh_history", "opening a file only selects it");
        t.Key(Key.Escape);
        t.Key(Key.Escape);
        t.Key(Key.Escape);
        h.Check(M().Trail.Length == 1 && M().Selected is null && M().Loaded, "Escape at the root clears the selection and stays");
        // Rows and tiles select the same item.
        program.Send(new Msg.Open([".cargo", "targets"]));
        t.Settle();
        var row = t.Get(Role.ListItem, "shared-deps");
        row.Click();
        h.Check(M().Selected?.Name == "shared-deps" && M().Layout.Tiles.Any(x => x.Item.Name == "shared-deps"), "selecting a list row selects its tile");
        h.Check(t.HasText("shared-deps") && t.ContainsText("~/.cargo/targets/shared-deps"), "the breadcrumb, sidebar and map agree on the place");
        h.Artifact(t, "targets-dark");
        t.Keystroke('c', Modifiers.Primary);
        h.Check(t.Context.Clipboard == "/Users/demo/.cargo/targets/shared-deps", $"⌘C copies the absolute path ({t.Context.Clipboard})");
        // The context menu reveals through the desk.
        var tile = M().Layout.Tiles.First(x => x.Item.Name == "gpui-kit-af01a901");
        var (mx, my) = At(t, tile);
        t.RightClick(mx, my);
        h.Check(M().Menu?.Item.Name == "gpui-kit-af01a901", "right-clicking a tile opens its menu");
        h.Artifact(t, "menu-dark");
        t.Get(Role.MenuItem, "Reveal in Finder").Click();
        Harness.Until(t, () => desk.Calls.Contains("reveal:/Users/demo/.cargo/targets/gpui-kit-af01a901"), "reveal");
        h.Check(M().Menu is null, "Reveal in Finder reveals the item and closes the menu");
        // Filter and metric.
        program.Send(new Msg.Crumb(0));
        program.Send(new Msg.SetFilter("DERIVED"));
        t.Settle();
        h.Check(M().Items.IsEmpty && M().Folder!.Value.Name == "demo", "the filter matches names in the open folder, never hiding the folder itself");
        program.Send(new Msg.SetFilter("LIB"));
        t.Settle();
        h.Check(M().Items.Select(i => i.Name).SequenceEqual(["Library"]), "the filter ignores case");
        program.Send(new Msg.SetFilter(""));
        t.Keystroke('3');
        h.Check(M().Metric == Metric.Files && M().Items[0].Files >= M().Items[1].Files, "3 switches to file counts and reorders the list");
        t.Keystroke('1');
        using var light = new Tester(program, new(1360, 860, DevMode: true, Theme: Theme.Light));
        light.Context.ReduceMotion = true;
        program.Send(new Msg.Open(["Library", "Developer"]));
        light.Settle(3);
        h.Artifact(light, "developer-light");
        h.Check(light.HasText("Developer") && light.HasText("Xcode"), "a folder two levels down opens in one step");
    }

    static void Marks(Harness h)
    {
        var (program, _) = Harness.Demo(1000, 700);
        using var _p = program;
        using var t = new Tester(program, new(1360, 860, DevMode: true));
        t.Context.ReduceMotion = true;
        t.Settle(3);
        Model M() => program.Model;
        program.Send(new Msg.Select(["Movies"]));
        t.Settle();
        t.Key(Key.Space);
        program.Send(new Msg.Select(["Music"]));
        t.Settle();
        t.Key(Key.Space);
        h.Check(M().Marked.SetEquals(["/Users/demo/Movies", "/Users/demo/Music"]), "Space marks the selected items by path");
        h.Check(t.HasText("2 marked"), "the subbar counts marked items");
        h.Artifact(t, "marked-light");
        t.Key(Key.Space);
        h.Check(M().Marked.Count == 1, "Space again unmarks");
        // A rescan without Movies drops its mark.
        var without = Item.Folder("demo", 0, 0, M().Tree!.Value.Children.Where(c => c.Name != "Movies"));
        program.Send(new Msg.Rescan());
        t.Settle();
        program.Send(new Msg.Scanned(M().Generation, [], without, TimeSpan.FromSeconds(1)));
        t.Settle();
        h.Check(M().Marked.IsEmpty && M().Tree!.Value.Child("Movies") is null, "marks of items a rescan removed are dropped");
    }

    static void Trash(Harness h)
    {
        var (program, desk) = Harness.Demo(1000, 700);
        using var _p = program;
        using var t = new Tester(program, new(1360, 860, DevMode: true, Theme: Theme.DarkTheme));
        t.Context.ReduceMotion = true;
        t.Settle(3);
        Model M() => program.Model;
        var before = M().Tree!.Value;
        var target = Items.At(before, "Library/Caches/Homebrew");
        program.Send(new Msg.Open(["Library", "Caches"]));
        program.Send(new Msg.Select(["Homebrew"]));
        t.Settle();
        t.Key(Key.Backspace, Modifiers.Primary);
        h.Check(M().Confirm is { Paths.Length: 1 } && desk.Calls.Count == 0, "⌘⌫ asks before anything moves");
        h.Artifact(t, "confirm-dark");
        t.Key(Key.Escape);
        h.Check(M().Confirm is null && M().Folder!.Value.Child("Homebrew") is not null, "Escape cancels and keeps the item");
        t.Key(Key.Backspace, Modifiers.Primary);
        t.Get(Role.Button, "Move to Trash").Click();
        Harness.Until(t, () => M().Notice is not null, "trash result");
        h.Check(desk.Calls.SequenceEqual(["trash:/Users/demo/Library/Caches/Homebrew"]), "confirming trashes exactly the selected item");
        var after = M().Tree!.Value;
        h.Check(M().Folder!.Value.Child("Homebrew") is null && after.Size == before.Size - target.Size &&
            Items.At(after, "Library").Size == Items.At(before, "Library").Size - target.Size && after.Files == before.Files - target.Files,
            "the item leaves the tree and every ancestor loses its size");
        h.Check(Items.Consistent(after), "totals stay consistent after trashing");
        desk.Refuse.Add("/Users/demo/Library/Caches/pip");
        program.Send(new Msg.Select(["pip"]));
        program.Send(new Msg.AskTrash());
        program.Send(new Msg.ConfirmTrash());
        Harness.Until(t, () => M().Notice?.StartsWith("Could not", StringComparison.Ordinal) == true, "refused trash");
        h.Check(M().Folder!.Value.Child("pip") is not null, "an item the Trash refused stays, with the failure in the status line");
    }

    /// <summary>A real folder scanned through the subscription, from the progress screen to the map.</summary>
    static void Scanning(Harness h)
    {
        using var f = new Fixture("view-scan");
        f.Write("big/movie.mov", 3_000_000);
        f.Write("big/other.bin", 1_000_000);
        f.Write("small/a.txt", 2_000);
        var program = DiskMapApp.CreateProgram(DiskMapApp.Start(f.Root), new Kumiko.Runtime.KumikoServices().Add<IDesk>(new RecordingDesk()));
        using var _p = program;
        using var t = new Tester(program, new(1200, 800, DevMode: true));
        t.Context.ReduceMotion = true;
        Harness.Until(t, () => program.Model.Loaded, "the scan");
        h.Check(program.Model.Scanning is null && program.Model.Tree!.Value.Files == 3 && program.Model.Layout.Tiles.Length > 0,
            "a scan started by the model shows its tree in the map");
        f.Write("small/b.txt", 500_000);
        program.Send(new Msg.Open(["small"]));
        program.Send(new Msg.Rescan());
        Harness.Until(t, () => program.Model.Scanning is null && program.Model.Folder!.Value.Files == 2, "the rescan");
        h.Check(program.Model.Folder!.Value.Name == "small" && program.Model.Tree!.Value.Files == 4 && Items.Consistent(program.Model.Tree!.Value),
            "rescanning a folder replaces it in place and updates its ancestors");
        Harness.Until(t, () => program.Model.Volume is not null, "the volume");
        h.Check(program.Model.Volume!.Total > 0 && program.Model.Volume.Free > 0, "the sidebar learns the volume's capacity");
    }
}
