using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Flint;
using Flint.Git;
using Kumiko.Runtime;
using Kumiko.UI;

internal static class PhysicalEdges
{
    internal static void Run(string output)
    {
        const string path = "/offscreen/edge-review";
        var snapshot = new Snapshot(path, new Head("main", Ahead: 2, Behind: 3), [], [], [], [], [], [], []);
        var model = FlintApp.Init with
        {
            Workspaces = [new("default", "Default", [path], path)],
            Repositories = ImmutableDictionary<string, RepoTab>.Empty.Add(path, new(snapshot)),
            Watching = false,
            Notices = [new(1, "Physical right edge in both reading directions", ToastKind.Success)]
        };
        // No subscriptions or effect dependencies: this scene renders an in-memory model only.
        using var program = new Program<FlintModel, Msg>(model, FlintApp.Update, FlintApp.Root,
            FlintEdgesJson.Default.FlintModel, FlintEdgesJson.Default.Msg);
        using var tester = new Tester(program, new(1440, 900));
        tester.Context.ReduceMotion = true;
        var results = new List<FlintEdgeGeometry>();
        foreach (var culture in new[] { "en-US", "he-IL", "en-US", "he-IL" })
        {
            tester.Context.Culture = CultureInfo.GetCultureInfo(culture);
            tester.Settle();
            var nodes = tester.Context.Nodes.ToArray();
            var toast = nodes.Single(n => n.Key == "toasts").Rect;
            var button = tester.Get(Role.Button, "Push").Rect;
            var buttonIndex = Array.FindIndex(nodes, node => node.Key == "push");
            var glyph = nodes.Single(node => node.Parent == buttonIndex && node.Key == "glyph").Rect;
            var badge = nodes.Where(n => n.Key == "badge").Select(n => n.Rect).Single(rect =>
                rect.X >= button.X && rect.Right <= button.Right && rect.Y >= button.Y && rect.Bottom <= button.Bottom);
            var rtl = tester.Context.Culture.TextInfo.IsRightToLeft;
            var overhangs = rtl ? badge.X < glyph.X && badge.Right < glyph.Right : badge.X > glyph.X && badge.Right > glyph.Right;
            results.Add(new(culture, toast, button, badge, Math.Abs(toast.Right - 1420) < .1f && Math.Abs(toast.Bottom - 880) < .1f,
                overhangs && badge.Y < glyph.Y));
            var name = Path.Combine(output, "flint-physical-edges-" + culture);
            tester.Screenshot(name + ".png");
            File.WriteAllText(name + ".txt", tester.Dump());
        }
        File.WriteAllText(Path.Combine(output, "flint-physical-edges.json"), JsonSerializer.Serialize(results, FlintEdgesJson.Default.ListFlintEdgeGeometry));
        if (results.Any(result => !result.ToastPinned || !result.BadgeMirrors))
            throw new InvalidOperationException("Flint physical toast / logical badge geometry failed; inspect flint-physical-edges.json");
        Console.WriteLine("PASS: Flint physical toast and logical badge across four culture changes");
    }
}
internal sealed record FlintEdgeGeometry(string Culture, Rect Toast, Rect Button, Rect Badge, bool ToastPinned, bool BadgeMirrors);
[JsonSerializable(typeof(FlintModel))]
[JsonSerializable(typeof(Msg))]
[JsonSerializable(typeof(List<FlintEdgeGeometry>))]
partial class FlintEdgesJson : JsonSerializerContext;
