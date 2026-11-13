namespace Kumiko.UI;

internal static partial class Layout
{
    /// <summary>
    /// Mirrors right-to-left subtrees after arrangement. Wherever the reading direction changes, the
    /// subtree's descendants are reflected about its box; nested changes compose, so a left-to-right island
    /// inside a mirrored window is reflected back.
    /// </summary>
    public static void Mirror(Context c, int index, bool parentRtl, bool windowRtl)
    {
        ref var n = ref c.Tree[index];
        var rtl = n.Style.Flow switch
        {
            FlowDirection.LeftToRight => false,
            FlowDirection.RightToLeft => true,
            _ => index == 0 ? windowRtl : parentRtl
        };
        n.RightToLeft = rtl;
        if (rtl != parentRtl)
        {
            var axis = 2 * n.Rect.X + n.Rect.Width;
            Reflect(c, index, axis);
        }
        for (var child = n.FirstChild; child >= 0; child = c.Tree[child].Next)
            Mirror(c, child, rtl, windowRtl);
    }

    static void Reflect(Context c, int index, float axis)
    {
        for (var child = c.Tree[index].FirstChild; child >= 0; child = c.Tree[child].Next)
        {
            ref var n = ref c.Tree[child];
            n.Rect = n.Rect with
            {
                X = axis - n.Rect.X - n.Rect.Width
            };
            Reflect(c, child, axis);
        }
    }
}
