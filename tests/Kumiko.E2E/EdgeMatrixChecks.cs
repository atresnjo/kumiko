using System.Text.Json;
using System.Text.Json.Serialization;
using Kumiko.UI;

internal static class EdgeMatrixChecks
{
    internal static void Run(string output, Action<bool, string> check)
    {
        var results = new List<EdgeGeometry>();
        var windowRtl = false;
        using var live = new Tester(c => EdgeMatrixView.Render(c, windowRtl), new(1000, 1800));
        for (var step = 0; step < 4; step++)
        {
            windowRtl = step % 2 != 0;
            live.Settle();
            var name = $"edge-matrix-{step}-{(windowRtl ? "rtl" : "ltr")}";
            live.Screenshot(Path.Combine(output, name + ".png"));
            File.WriteAllText(Path.Combine(output, name + ".txt"), live.Dump());
            for (var panel = 0; panel < 2; panel++)
            {
                var rtl = panel == 0 ? windowRtl : !windowRtl;
                foreach (var property in EdgeMatrixView.Properties)
                    foreach (var typed in new[] { false, true })
                        foreach (var side in EdgeMatrixView.Sides)
                        {
                            var left = side == LayoutEdge.Left || side == (rtl ? LayoutEdge.End : LayoutEdge.Start);
                            Verify($"{panel}-{property}-{typed}-{side}", property == "Inset" ? left ? 20 : 64 : left ? 20 : 0,
                                property == "Inset" ? 20 : 84);
                        }
                foreach (var property in new[] { "Padding", "Margin", "Border" })
                    Verify($"{panel}-legacy-{property}", rtl ? 8 : 20, 76);
                Verify($"{panel}-legacy-Symmetric", 12, 80);
                foreach (var side in EdgeMatrixView.Sides)
                {
                    var left = side == LayoutEdge.Left || side == (rtl ? LayoutEdge.End : LayoutEdge.Start);
                    Verify($"{panel}-percent-{side}", left ? 26 : 58, 20);
                    Verify($"{panel}-auto-{side}", left ? 84 : 0, 20);
                    Verify($"{panel}-relative-{side}", left ? 62 : 22, 20);
                }
                foreach (var property in EdgeMatrixView.Properties)
                    foreach (var side in EdgeMatrixView.Sides)
                    {
                        var left = side == LayoutEdge.Left || side == (rtl ? LayoutEdge.Start : LayoutEdge.End);
                        Verify($"{panel}-boundary-{property}-{side}", property == "Inset" ? left ? 20 : 64 : left ? 20 : 0,
                            property == "Inset" ? 20 : 84);
                    }
                foreach (var property in new[] { "Margin", "Inset" })
                    foreach (var side in EdgeMatrixView.Sides)
                    {
                        var left = side is LayoutEdge.Left or LayoutEdge.Start;
                        Verify($"{panel}-fixed-{property}-{side}", property == "Inset" ? left ? 20 : 64 : left ? 20 : 0,
                            property == "Inset" ? 20 : 84);
                    }
                foreach (var property in new[] { "Padding", "Margin", "Border" })
                    Verify($"{panel}-fallback-{property}", 30, rtl ? 54 : 66);
            }
            using var fresh = new Tester(c => EdgeMatrixView.Render(c, windowRtl), new(1000, 1800));
            var expected = fresh.Context.Nodes.ToArray().Select(node => (node.Key, node.Rect)).ToArray();
            var actual = live.Context.Nodes.ToArray().Select(node => (node.Key, node.Rect)).ToArray();
            results.Add(new(name, "incremental matches fresh", default, default, actual.SequenceEqual(expected)));

            void Verify(string key, float x, float width)
            {
                var stage = Box(live, key + "-stage");
                var rect = Box(live, key + "-target");
                var wanted = new Rect(stage.X + x, stage.Y, width, 20);
                results.Add(new(name, key, rect, wanted, Near(rect, wanted)));
            }
        }
        File.WriteAllText(Path.Combine(output, "edge-matrix.json"), JsonSerializer.Serialize(results, EdgeMatrixJson.Default.ListEdgeGeometry));
        foreach (var result in results)
            check(result.Passed, $"{result.Scene}: {result.Key} physical rectangle {result.Actual} matches {result.Expected}");
    }

    static Rect Box(Tester tester, string key) => tester.Context.Nodes.ToArray().Single(n => n.Key == key).Rect;
    static bool Near(Rect a, Rect b) => Math.Abs(a.X - b.X) < .01f && Math.Abs(a.Y - b.Y) < .01f &&
        Math.Abs(a.Width - b.Width) < .01f && Math.Abs(a.Height - b.Height) < .01f;
}
internal sealed record EdgeGeometry(string Scene, string Key, Rect Actual, Rect Expected, bool Passed);
[JsonSerializable(typeof(List<EdgeGeometry>))]
partial class EdgeMatrixJson : JsonSerializerContext;
