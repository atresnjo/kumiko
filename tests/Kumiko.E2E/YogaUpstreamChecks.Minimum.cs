using Facebook.Yoga;
using static Facebook.Yoga.YGNodeAPI;
using static Facebook.Yoga.YGNodeLayoutAPI;
using static Facebook.Yoga.YGNodeStyleAPI;
using YNode = Facebook.Yoga.Node;

internal static partial class YogaUpstreamChecks
{
    static YGSize Words(YNode node, float width, MeasureMode mode, float height, MeasureMode heightMode) =>
        new()
        {
            Width = mode == MeasureMode.AtMost ? Math.Max(30, Math.Min(90, width)) : 90,
            Height = 16
        };

    static void AutoMinimum(Action<bool, string> check)
    {
        MinContentDeclarations(check);
        foreach (var variant in new[] { "content", "padding", "zero", "hidden", "scroll", "max", "specified", "ratio", "column" })
        {
            var column = variant == "column";
            var root = Root(column ? 200 : 20, column ? 20 : 50, !column);
            var text = Child(root);
            text.SetMeasureFunc(Words);
            YGNodeStyleSetFlexBasis(text, 90);
            YGNodeStyleSetFlexShrink(text, 1);
            var spacer = Child(root, column ? 10 : 10, column ? 10 : 20);
            switch (variant)
            {
                case "padding":
                    YGNodeStyleSetPadding(text, YGEdge.Horizontal, 4);
                    YGNodeStyleSetBorder(text, YGEdge.Horizontal, 1);
                    break;
                case "zero":
                    YGNodeStyleSetMinWidth(text, 0);
                    break;
                case "hidden":
                    YGNodeStyleSetOverflow(text, YGOverflow.Hidden);
                    break;
                case "scroll":
                    YGNodeStyleSetOverflow(text, YGOverflow.Scroll);
                    break;
                case "max":
                    YGNodeStyleSetMaxWidth(text, 20);
                    break;
                case "specified":
                    YGNodeStyleSetWidth(text, 20);
                    break;
                case "ratio":
                    YGNodeStyleSetHeight(text, 10);
                    YGNodeStyleSetAspectRatio(text, 2);
                    break;
                case "column":
                    YGNodeStyleSetPadding(text, YGEdge.Vertical, 4);
                    break;
            }
            Layout(root);
            var expected = variant switch
            {
                "padding" => 40,
                "zero" or "hidden" or "scroll" => 10,
                "max" or "specified" or "ratio" => 20,
                "column" => 24,
                _ => 30
            };
            check(Near(column ? YGNodeLayoutGetHeight(text) : YGNodeLayoutGetWidth(text), expected),
                $"automatic minimum honors {variant} ({expected})");
        }
        var grow = Root(50, 50);
        for (var i = 0; i < 2; i++)
        {
            var item = Child(grow);
            item.SetMeasureFunc(Words);
            YGNodeStyleSetFlex(item, 1);
        }
        Layout(grow);
        check(grow.GetChildren().All(n => Near(YGNodeLayoutGetWidth(n), 30)),
            "zero-basis growing items retain their content minimum");

        // Count genuine leaf measurement requests, not implementation fields.
        var probes = 0;
        var rootNested = Root(20, 50);
        var outer = Child(rootNested);
        YGNodeStyleSetFlexShrink(outer, 1);
        var parent = outer;
        for (var depth = 0; depth < 10; depth++)
        {
            parent = Child(parent);
            YGNodeStyleSetFlexDirection(parent, depth % 2 == 0 ? YGFlexDirection.Column : YGFlexDirection.Row);
            YGNodeStyleSetFlexShrink(parent, 1);
        }
        var leaf = Child(parent);
        leaf.SetMeasureFunc((n, w, wm, h, hm) =>
        {
            if (wm == MeasureMode.AtMost && w == 0 || hm == MeasureMode.AtMost && h == 0)
                probes++;
            return Words(n, w, wm, h, hm);
        });
        Child(rootNested, 10, 20);
        Layout(rootNested);
        check(Near(YGNodeLayoutGetWidth(outer), 30) && probes is > 0 and <= 2,
            $"deep automatic minimum reaches outer item with bounded leaf probes ({probes})");
    }
}

internal static partial class YogaUpstreamChecks
{
    static void MinContentDeclarations(Action<bool, string> check)
    {
        var root = Root(20, 50);
        var item = Child(root);
        item.SetMeasureFunc(Words);
        YGNodeStyleSetFlexBasis(item, 90);
        YGNodeStyleSetFlexShrink(item, 1);
        Child(root, 10, 10);
        var calls = 0;
        var minimum = 40f;
        YGNodeSetMinContentMeasureFunc(item, (_, _, _, _, _) =>
        {
            calls++;
            return new()
            {
                Width = minimum,
                Height = 16
            };
        });
        Layout(root);
        check(Near(YGNodeLayoutGetWidth(item), 40) && calls > 0, "dynamic min-content callback supplies the floor");
        YGNodeSetMinContentWidth(item, 0);
        calls = 0;
        Layout(root);
        check(Near(YGNodeLayoutGetWidth(item), 10) && calls == 0, "static min-content overrides the dynamic probe");
        YGNodeSetMinContentWidth(item, float.NaN);
        minimum = 50;
        YGNodeMarkDirty(item);
        Layout(root);
        check(Near(YGNodeLayoutGetWidth(item), 50), "later layout reprobes changed min-content");
        var cloned = YGNodeClone(item);
        YGNodeRemoveChild(root, item);
        YGNodeInsertChild(root, cloned, 0);
        Layout(root);
        check(Near(YGNodeLayoutGetWidth(cloned), 50), "cloned nodes preserve dynamic min-content declarations");
    }
}
