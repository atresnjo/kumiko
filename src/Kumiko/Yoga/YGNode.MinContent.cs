namespace Facebook.Yoga;

internal static partial class YGNodeAPI
{
    public static void YGNodeSetMinContentMeasureFunc(Node node, YGMinContentMeasureFunc? value) => node.SetMinContentMeasureFunc(value);
    public static bool YGNodeHasMinContentMeasureFunc(Node node) => node.HasMinContentMeasureFunc();
    public static void YGNodeSetMinContentWidth(Node node, float value) => node.SetMinContentWidth(new(value));
    public static void YGNodeSetMinContentHeight(Node node, float value) => node.SetMinContentHeight(new(value));
    public static float YGNodeGetMinContentWidth(Node node) => node.GetMinContentWidth().Unwrap();
    public static float YGNodeGetMinContentHeight(Node node) => node.GetMinContentHeight().Unwrap();
}
