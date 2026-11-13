using Facebook.Yoga;
using static Facebook.Yoga.YGNodeLayoutAPI;
using static Facebook.Yoga.YGNodeStyleAPI;

internal static partial class YogaUpstreamChecks
{
    static void ScrollSizing(Action<bool, string> check)
    {
        foreach (var enabled in new[] { false, true })
        {
            var root = Root(100, 300, false);
            root.Config.SetExperimentalFeatureEnabled(ExperimentalFeature.FixFlexBasisFitContent, enabled);
            var container = Child(root);
            var child = Child(container);
            YGNodeStyleSetHeightPercent(child, 50);
            Layout(root);
            check(Near(YGNodeLayoutGetHeight(container), 150) && Near(YGNodeLayoutGetHeight(child), 75),
                $"experimental basis preserves non-scroll viewport percentage bounds (enabled={enabled})");
        }
        foreach (var variant in new[] { "grow", "percent", "basis", "padding", "absolute", "row", "wrap", "box" })
        {
            float[]? reference = null;
            foreach (var enabled in new[] { false, true })
            {
                var root = Root(200, 300, false);
                root.Config.SetExperimentalFeatureEnabled(ExperimentalFeature.FixFlexBasisFitContent, enabled);
                YGNodeStyleSetOverflow(root, YGOverflow.Scroll);
                var content = Child(root);
                YGNodeStyleSetFlexGrow(content, 1);
                var wrapper = Child(content);
                var item = Child(wrapper);
                switch (variant)
                {
                    case "grow":
                        YGNodeStyleSetFlexGrow(wrapper, 1);
                        YGNodeStyleSetFlexGrow(item, 1);
                        break;
                    case "percent":
                        YGNodeStyleSetHeightPercent(item, 50);
                        break;
                    case "basis":
                        YGNodeStyleSetFlexBasis(wrapper, 120);
                        break;
                    case "padding":
                        YGNodeStyleSetPaddingPercent(wrapper, YGEdge.Top, 10);
                        break;
                    case "absolute":
                        YGNodeStyleSetPositionType(item, YGPositionType.Absolute);
                        YGNodeStyleSetPositionPercent(item, YGEdge.Bottom, 10);
                        YGNodeStyleSetHeight(item, 10);
                        break;
                    case "row":
                        YGNodeStyleSetFlexDirection(wrapper, YGFlexDirection.Row);
                        break;
                    case "wrap":
                        YGNodeStyleSetFlexWrap(wrapper, YGWrap.Wrap);
                        break;
                    case "box":
                        YGNodeStyleSetPadding(wrapper, YGEdge.All, 10);
                        YGNodeStyleSetBoxSizing(wrapper, YGBoxSizing.ContentBox);
                        break;
                }
                var measured = Child(item);
                var intrinsicHeight = 40f;
                measured.SetMeasureFunc((_, _, _, _, _) => new() { Width = 50, Height = intrinsicHeight });
                var values = new List<float>();
                for (var step = 0; step < 3; step++)
                {
                    if (step == 1)
                    {
                        intrinsicHeight = 60;
                        measured.MarkDirtyAndPropagate();
                        YGNodeStyleSetFlexBasis(wrapper, 140);
                    }
                    if (step == 2)
                    {
                        YGNodeStyleSetBoxSizing(wrapper, YGBoxSizing.BorderBox);
                        YGNodeStyleSetHeight(root, 400);
                    }
                    Layout(root);
                    foreach (var node in new[] { content, wrapper, item, measured })
                    {
                        values.Add(YGNodeLayoutGetWidth(node));
                        values.Add(YGNodeLayoutGetHeight(node));
                        values.Add(YGNodeLayoutGetLeft(node));
                        values.Add(YGNodeLayoutGetTop(node));
                    }
                }
                if (reference is null)
                    reference = values.ToArray();
                else
                    check(values.Select((v, i) => Near(v, reference[i])).All(v => v),
                        $"experimental scroll basis preserves {variant} through intrinsic/basis/box-size mutations");
            }
        }
    }
}
