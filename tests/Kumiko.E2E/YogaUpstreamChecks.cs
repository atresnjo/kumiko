using System.Text.Json;
using Facebook.Yoga;
using static Facebook.Yoga.YGNodeAPI;
using static Facebook.Yoga.YGNodeLayoutAPI;
using static Facebook.Yoga.YGNodeStyleAPI;
using YNode = Facebook.Yoga.Node;

// Full engine trees exercise cache history, ownership and measure/layout passes that
// Kumiko's flattened contents tree and disabled rounding cannot reach through views.
internal static partial class YogaUpstreamChecks
{
    internal static void Run(string output, Action<bool, string> check)
    {
        var results = new List<(string Name, bool Passed)>();
        void Expect(bool value, string name) => results.Add((name, value));
        ShrinkAndGrow(Expect);
        Wrapping(Expect);
        ContentsAndFree(Expect);
        OwnerCaches(Expect);
        Rounding(Expect);
        AutoMinimum(Expect);
        ScrollSizing(Expect);
        Views(output, Expect);
        using (var stream = File.Create(Path.Combine(output, "yoga-upstream.json")))
        using (var writer = new Utf8JsonWriter(stream, new()
        {
            Indented = true
        }))
        {
            writer.WriteStartArray();
            foreach (var result in results)
            {
                writer.WriteStartObject();
                writer.WriteString("scenario", result.Name);
                writer.WriteBoolean("passed", result.Passed);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
        }
        foreach (var result in results)
            check(result.Passed, result.Name);
    }

    static YNode Root(float width = 200, float height = 200, bool row = true)
    {
        var config = new Config();
        config.SetPointScaleFactor(0);
        var root = new YNode(config);
        YGNodeStyleSetWidth(root, width);
        YGNodeStyleSetHeight(root, height);
        YGNodeStyleSetFlexDirection(root, row ? YGFlexDirection.Row : YGFlexDirection.Column);
        return root;
    }

    static YNode Child(YNode parent, float width = float.NaN, float height = float.NaN)
    {
        var child = new YNode(parent.Config);
        YGNodeStyleSetWidth(child, width);
        YGNodeStyleSetHeight(child, height);
        YGNodeInsertChild(parent, child, parent.GetChildCount());
        return child;
    }

    static void Layout(YNode root) => YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
    static bool Near(float a, float b) => float.IsFinite(a) && Math.Abs(a - b) < .001f;
    static void ClearFlags(YNode node)
    {
        node.SetHasNewLayout(false);
        foreach (var child in node.GetChildren())
            ClearFlags(child);
    }

    static void ShrinkAndGrow(Action<bool, string> check)
    {
        foreach (var widths in new float[][] { [100.1f, 200.3f, 300.7f], [10000.1f, 30000.7f, 34567.8f] })
        {
            var root = Root(widths[0] < 1000 ? 350 : 1000, 50);
            var minima = widths[0] < 1000 ? new float[] { 90, 190, 290 } : widths.Select(w => w - 50).ToArray();
            for (var i = 0; i < widths.Length; i++)
            {
                var child = Child(root, widths[i]);
                YGNodeStyleSetMinWidth(child, minima[i]);
                YGNodeStyleSetFlexShrink(child, 1);
            }
            Layout(root);
            check(root.GetChildren().Select((n, i) => Near(YGNodeLayoutGetWidth(n), minima[i])).All(x => x),
                $"frozen fractional shrink remains at minima with total {widths.Sum()}");
        }
        foreach (var outlier in new[] { 0, 1, 2 })
        {
            var root = Root(540, 30);
            for (var i = 0; i < 3; i++)
            {
                var child = Child(root, height: 30);
                YGNodeStyleSetFlexGrow(child, 1);
                YGNodeStyleSetFlexShrink(child, 1);
                YGNodeStyleSetMinWidth(child, i == outlier ? 60 : 30);
                YGNodeStyleSetMaxWidth(child, 180);
            }
            Layout(root);
            check(root.GetChildren().All(n => Near(YGNodeLayoutGetWidth(n), 180)),
                $"first-pass growth fills the row with min-width outlier at {outlier}");
        }
    }

    static void Wrapping(Action<bool, string> check)
    {
        foreach (var width in Enumerable.Range(0, 100).Select(i => 84f + i / 100f))
        {
            var root = Root(400, 400, false);
            YGNodeStyleSetAlignItems(root, YGAlign.FlexStart);
            var wrapper = Child(root);
            YGNodeStyleSetAlignSelf(wrapper, YGAlign.FlexStart);
            YGNodeStyleSetBorder(wrapper, YGEdge.All, 1);
            var row = Child(wrapper);
            YGNodeStyleSetFlexDirection(row, YGFlexDirection.Row);
            YGNodeStyleSetFlexWrap(row, YGWrap.Wrap);
            YGNodeStyleSetPadding(row, YGEdge.All, 16);
            var measured = Child(row);
            measured.SetMeasureFunc((_, _, _, _, _) => new() { Width = width, Height = 20 });
            var rigid = Child(row, 20, 20);
            Layout(root);
            check(Near(YGNodeLayoutGetTop(measured), YGNodeLayoutGetTop(rigid)) && Near(YGNodeLayoutGetHeight(row), 52),
                $"fractional content-sized row stays on one line at {width}");
        }
        var wrap = Root(39, 60);
        YGNodeStyleSetFlexWrap(wrap, YGWrap.Wrap);
        var first = Child(wrap, 20, 20);
        var second = Child(wrap, 20, 20);
        Layout(wrap);
        check(YGNodeLayoutGetTop(second) > YGNodeLayoutGetTop(first), "real overflow still wraps");
    }

    static void ContentsAndFree(Action<bool, string> check)
    {
        var root = Root();
        var contents = Child(root);
        var leaf = Child(contents, 10, 10);
        YGNodeStyleSetDisplay(contents, YGDisplay.Contents);
        Layout(root);
        YGNodeStyleSetWidth(leaf, 20);
        Layout(root);
        check(Near(YGNodeLayoutGetWidth(leaf), 20), "attached contents propagates later descendant changes");
        YGNodeStyleSetDisplay(contents, YGDisplay.Flex);
        Layout(root);
        YGNodeStyleSetWidth(leaf, 30);
        Layout(root);
        check(Near(YGNodeLayoutGetWidth(leaf), 30), "restored flex box propagates descendant changes");

        foreach (var hidden in new[] { false, true })
        {
            root = Root(row: false);
            var parent = Child(root);
            YGNodeStyleSetFlexGrow(parent, 1);
            contents = Child(parent);
            YGNodeStyleSetDisplay(contents, hidden ? YGDisplay.None : YGDisplay.Contents);
            Child(contents, 20, 20);
            Layout(root);
            ClearFlags(root);
            YGNodeStyleSetOverflow(root, YGOverflow.Scroll);
            Layout(root);
            check(!contents.HasNewLayout, $"measure-only pass leaves {(hidden ? "hidden" : "contents")} flags clear");
        }
        root = Root();
        var fixedBox = Child(root, 50, 50);
        YGNodeStyleSetPositionType(fixedBox, YGPositionType.Static);
        contents = Child(fixedBox);
        YGNodeStyleSetDisplay(contents, YGDisplay.Contents);
        var absolute = Child(contents, 10, 10);
        YGNodeStyleSetPositionType(absolute, YGPositionType.Absolute);
        YGNodeStyleSetPosition(absolute, YGEdge.Right, 0);
        Layout(root);
        ClearFlags(root);
        YGNodeStyleSetWidth(root, 250);
        Layout(root);
        check(absolute.HasNewLayout && fixedBox.HasNewLayout && contents.HasNewLayout &&
            Near(YGNodeLayoutGetLeft(absolute), 240), "absolute updates through contents remain reachable");

        root = Root(width: float.NaN, row: false);
        YGNodeStyleSetAlignItems(root, YGAlign.FlexStart);
        var removed = Child(root, 80, 20);
        Child(root, 20, 20);
        Layout(root);
        YGNodeFree(removed);
        Layout(root);
        check(Near(YGNodeLayoutGetWidth(root), 20), "freeing a child refreshes parent intrinsic size");

        root = Root();
        var target = Child(root, 100, 50);
        YGNodeStyleSetPadding(target, YGEdge.All, 10);
        var source = Root(100, 50, false);
        YGNodeStyleSetPadding(source, YGEdge.All, 10);
        YGNodeStyleSetBoxSizing(source, YGBoxSizing.ContentBox);
        Layout(root);
        YGNodeCopyStyle(target, source);
        Layout(root);
        check(Near(YGNodeLayoutGetWidth(target), 120), "copying box-sizing alone updates border-box geometry");
    }

    static void OwnerCaches(Action<bool, string> check)
    {
        foreach (var measured in new[] { false, true })
            foreach (var boundary in new[] { false, true })
            {
                var root = Root(200, 200, false);
                root.Config.RelayoutBoundaries = boundary;
                var box = Child(root, 100, 100);
                YGNodeStyleSetPositionType(box, YGPositionType.Absolute);
                YGNodeStyleSetPaddingPercent(box, YGEdge.All, 10);
                var inner = Child(box, 10, 10);
                if (measured)
                {
                    YGNodeRemoveChild(box, inner);
                    box.SetMeasureFunc((_, _, _, _, _) => new() { Width = 10, Height = 10 });
                }
                Layout(root);
                YGNodeStyleSetWidth(root, 300);
                Layout(root);
                check(Near(YGNodeLayoutGetPadding(box, YGEdge.Left), 30) &&
                    (measured || Near(YGNodeLayoutGetLeft(inner), 30)),
                    $"percentage padding follows resized owner (measure={measured}, boundary={boundary})");
                if (!measured)
                {
                    YGNodeStyleSetWidth(inner, 15);
                    Layout(root);
                    check(Near(YGNodeLayoutGetLeft(inner), 30), $"boundary repeats latest owner size (boundary={boundary})");
                }
            }
        foreach (var height in new[] { false, true })
        {
            var root = Root(100, 100, !height);
            var child = Child(root, 10, 10);
            if (height)
                YGNodeStyleSetMinHeightPercent(child, 50);
            else
                YGNodeStyleSetMinWidthPercent(child, 50);
            Layout(root);
            YGNodeStyleSetWidth(root, 200);
            YGNodeStyleSetHeight(root, 200);
            Layout(root);
            check(Near(height ? YGNodeLayoutGetHeight(child) : YGNodeLayoutGetWidth(child), 100),
                $"percentage minimum follows owner {(height ? "height" : "width")}");
        }
    }

    static void Rounding(Action<bool, string> check)
    {
        foreach (var top in new[] { 1023.9f, 1024.1f, 2047.9f, 4096.1f })
        {
            var root = Root(100, 5000, false);
            root.Config.SetPointScaleFactor(3);
            var text = Child(root);
            text.SetMeasureFunc((_, _, _, _, _) => new() { Width = 30, Height = 44 });
            YGNodeStyleSetPositionType(text, YGPositionType.Absolute);
            YGNodeStyleSetPosition(text, YGEdge.Top, top);
            Layout(root);
            check(YGNodeLayoutGetHeight(text) == 44, $"far-off text retains exact pixel height at {top}");
        }
        var owner = Root();
        owner.Config.SetPointScaleFactor(1);
        var shared = Child(owner, 20, 20);
        shared.SetLayoutPosition(.4f, PhysicalEdge.Left);
        var clone = YGNodeClone(owner);
        PixelGrid.RoundLayoutResultsToPixelGrid(clone, 0, 0);
        check(shared.Layout.Position(PhysicalEdge.Left) == .4f, "rounding skips non-owned shared subtrees");
    }
}
