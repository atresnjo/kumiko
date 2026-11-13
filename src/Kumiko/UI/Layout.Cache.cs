namespace Kumiko.UI;

internal static partial class Layout
{
    // Paint-only changes must not rerun Yoga or allocate its temporary style lengths.
    static bool SameLayout(in Style a, in Style b) =>
        a.Layout == b.Layout && a.Direction == b.Direction && a.Reverse == b.Reverse && a.Wrap == b.Wrap &&
        a.AlignItems == b.AlignItems && a.AlignSelf == b.AlignSelf && a.AlignContent == b.AlignContent &&
        a.Justify == b.Justify && a.Gap == b.Gap && a.RowGap == b.RowGap && a.ColumnGap == b.ColumnGap &&
        a.Padding == b.Padding && a.Margin == b.Margin && a.FlowBorder == b.FlowBorder && a.Width == b.Width && a.Height == b.Height &&
        a.Basis == b.Basis && a.Grow == b.Grow && a.Shrink == b.Shrink && a.AspectRatio == b.AspectRatio &&
        a.MinWidth == b.MinWidth && a.MinHeight == b.MinHeight && a.MaxWidth == b.MaxWidth && a.MaxHeight == b.MaxHeight &&
        a.Scroll == b.Scroll && a.ScrollHorizontal == b.ScrollHorizontal && a.Clip == b.Clip && a.Absolute == b.Absolute;

    static bool SameParent(in Style a, in Style b) =>
        a.Direction == b.Direction && a.Wrap == b.Wrap && a.AlignItems == b.AlignItems && a.Padding == b.Padding &&
        a.FlowBorder == b.FlowBorder && a.Layout.Padding == b.Layout.Padding && a.Layout.Border == b.Layout.Border;
}
