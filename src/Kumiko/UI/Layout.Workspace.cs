using Facebook.Yoga;
using static Facebook.Yoga.YGConfigAPI;
using static Facebook.Yoga.YGNodeAPI;
using YogaNode = Facebook.Yoga.Node;

namespace Kumiko.UI;

internal static partial class Layout
{
    // One managed Yoga tree per Context. Callback state holds only its owner and an index;
    // native payloads are read from the current Kumiko tree and never retained here.
    internal sealed class Workspace
    {
        internal sealed class Entry(Context context, int index, Config config)
        {
            internal readonly Context Context = context;
            internal readonly int Index = index;
            internal readonly YogaNode Node = YGNodeNewWithConfig(config);
            internal Style Style, ParentStyle;
            internal bool Configured, Flexible, RightToLeft, ParentRightToLeft;
            internal string? Text;
            internal TextStyle TextStyle;
            internal SpanSnapshot Spans;
            internal ContentKind Kind;
            internal object? NativeContent;
            internal int LayoutParent = -2;
            internal bool Contents;
            internal TextQueries? Queries;
        }

        static readonly Style RootParent = new();
        readonly Config config = YGConfigNew();
        readonly List<Entry> entries = new();
        int count;
        (string? Family, string? MonoFamily, float Scale) textMetrics;
        bool metricsChanged;
        object? nativeTextMetrics;

        internal Workspace()
        {
            // Keep logical-point geometry; the renderer aligns glyphs to device pixels.
            YGConfigSetPointScaleFactor(config, 0);
            YGConfigSetErrata(config, YGErrata.AbsolutePercentAgainstInnerSize);
            config.RelayoutBoundaries = true;
        }

        internal YogaNode this[int index] => entries[index].Node;

        internal void Prepare(Context c)
        {
            while (entries.Count < c.Count)
            {
                var entry = new Entry(c, entries.Count, config);
                YGNodeSetContext(entry.Node, entry);
                entries.Add(entry);
            }
            var changed = count != c.Count;
            for (var i = 0; i < c.Count; i++)
                changed |= entries[i].LayoutParent != c.Tree[i].LayoutParent ||
                    entries[i].Contents != (c.Tree[i].Style.Layout.Display == DisplayMode.Contents);
            if (changed)
                Relink(c);
            for (var i = c.Count; i < count; i++)
                entries[i].Text = null;
            count = c.Count;
            var metrics = (c.TextSystem.Family, c.TextSystem.MonoFamily, c.TextSystem.Scale);
            var nativeMetrics = c.ControlRenderer?.TextMetricsKey;
            metricsChanged = metrics != textMetrics || !Equals(nativeMetrics, nativeTextMetrics);
            textMetrics = metrics;
            nativeTextMetrics = nativeMetrics;
        }

        int[] heads = [], tails = [], nexts = [];
        bool[] relink = [];

        /// <summary>
        /// Gives each Yoga parent the children the tree now lists, touching only parents whose children changed.
        /// A virtual list that scrolls a row in shifts later indices, which used to detach and dirty every node and
        /// drop every text measurement; unchanged parents now keep their children and Yoga's layout caches.
        /// </summary>
        void Relink(Context c)
        {
            int n = c.Count;
            if (heads.Length < entries.Count)
            {
                heads = new int[entries.Count];
                tails = new int[entries.Count];
                nexts = new int[entries.Count];
                relink = new bool[entries.Count];
            }
            Array.Fill(heads, -1, 0, n);
            for (var i = 0; i < n; i++)
            {
                var entry = entries[i];
                entry.LayoutParent = c.Tree[i].LayoutParent;
                entry.Contents = c.Tree[i].Style.Layout.Display == DisplayMode.Contents;
                nexts[i] = -1;
                if (entry.LayoutParent < 0 || entry.Contents)
                    continue;
                if (heads[entry.LayoutParent] < 0)
                    heads[entry.LayoutParent] = i;
                else
                    nexts[tails[entry.LayoutParent]] = i;
                tails[entry.LayoutParent] = i;
            }
            for (var p = 0; p < n; p++)
            {
                var node = entries[p].Node;
                var same = true;
                nuint k = 0;
                for (var child = heads[p]; child >= 0 && same; child = nexts[child], k++)
                    same = k < node.GetChildCount() && ReferenceEquals(node.GetChild(k), entries[child].Node);
                relink[p] = !same || k != node.GetChildCount();
            }
            // Release old children first: a moved node must be ownerless before its new parent adopts it.
            for (var p = 0; p < entries.Count; p++)
            {
                if (p < n && !relink[p])
                    continue;
                var node = entries[p].Node;
                for (nuint k = 0; k < node.GetChildCount(); k++)
                    node.GetChild(k)!.SetOwner(null);
                node.ClearChildren();
                if (p >= n)
                    node.SetOwner(null);
            }
            for (var p = 0; p < n; p++)
            {
                if (!relink[p])
                    continue;
                var entry = entries[p];
                if (heads[p] >= 0 && entry.Node.HasMeasureFunc())
                {
                    YGNodeSetMeasureFunc(entry.Node, null);
                    entry.Queries?.Clear();
                }
                entry.Node.MarkDirtyAndPropagate();
                for (var child = heads[p]; child >= 0; child = nexts[child])
                    YGNodeInsertChild(entry.Node, entries[child].Node, entry.Node.GetChildCount());
            }
        }

        internal void Configure(Context c, int index)
        {
            var entry = entries[index];
            ref readonly var node = ref c.Tree[index];
            var outerRtl = node.Parent >= 0 && c.Tree[node.Parent].RightToLeft;
            ref readonly var parent = ref node.LayoutParent >= 0 ? ref c.Tree[node.LayoutParent].Style : ref RootParent;
            if (!entry.Configured || entry.Flexible != node.Flexible || entry.RightToLeft != node.RightToLeft ||
                entry.ParentRightToLeft != outerRtl || !SameLayout(entry.Style, node.Style) ||
                !SameParent(entry.ParentStyle, parent))
            {
                ApplyStyle(c, index, entry.Node);
                entry.Style = node.Style;
                entry.ParentStyle = parent;
                entry.Flexible = node.Flexible;
                entry.RightToLeft = node.RightToLeft;
                entry.ParentRightToLeft = outerRtl;
            }
            var text = node.Kind is ContentKind.Text or ContentKind.Icon;
            // Restructuring detaches measure functions before children are reinserted.
            if (!entry.Configured || entry.Kind != node.Kind || entry.Node.HasMeasureFunc() != text)
            {
                entry.Node.MarkDirtyAndPropagate();
                entry.Queries?.Clear();
                entry.Node.SetMeasureFunc(text ? MeasureText : null);
                entry.Node.SetMinContentMeasureFunc(text ? MeasureMinContentText : null);
                entry.Node.SetBaselineFunc(node.Kind == ContentKind.Text ? TextBaseline : null);
                entry.Kind = node.Kind;
            }
            // A string that answers Yoga's recorded queries identically, such as a counter in tabular
            // digits, keeps every cached size and baseline valid and needs no relayout.
            if (text && (!entry.Configured || entry.TextStyle != node.TextStyle || metricsChanged || !Equals(entry.NativeContent, node.NativeContent) ||
                node.TextStyle.Overflow != TextOverflow.Visible && !entry.Spans.Matches(node.Spans) ||
                !ReferenceEquals(entry.Text, node.Text) && entry.Text != node.Text && entry.Queries?.Answers(c, node) != true))
            {
                YGNodeMarkDirty(entry.Node);
                entry.Queries?.Clear(metricsChanged);
            }
            entry.Text = text ? node.Text : null;
            entry.TextStyle = node.TextStyle;
            if (!entry.Spans.Matches(node.Spans))
                entry.Spans.Set(node.Spans);
            entry.NativeContent = text ? node.NativeContent : null;
            entry.Configured = true;
        }

        internal bool ResolveFillRatios(Context c)
        {
            var changed = false;
            for (var i = 1; i < c.Count; i++)
            {
                ref readonly var node = ref c.Tree[i];
                ref readonly var s = ref node.Style;
                if (node.LayoutHidden || s.Layout.Display == DisplayMode.Contents || s.Absolute || s.AspectRatio <= 0 || s.Basis.Kind != LengthKind.Auto)
                    continue;
                ref readonly var parent = ref c.Tree[node.LayoutParent].Style;
                var row = parent.Direction == Direction.Row;
                if (parent.Wrap != FlexWrap.NoWrap ||
                    (row ? s.Height : s.Width).Kind != LengthKind.Fill ||
                    (row ? s.Width : s.Height).Kind != LengthKind.Auto ||
                    (row ? node.LayoutMargin.Horizontal : node.LayoutMargin.Vertical) == 0)
                    continue;
                var owner = entries[node.LayoutParent].Node;
                var available = row
                    ? YGNodeLayoutAPI.YGNodeLayoutGetHeight(owner) - c.Tree[node.LayoutParent].LayoutPadding.Vertical - node.LayoutMargin.Vertical
                    : YGNodeLayoutAPI.YGNodeLayoutGetWidth(owner) - c.Tree[node.LayoutParent].LayoutPadding.Horizontal - node.LayoutMargin.Horizontal;
                var size = row ? Math.Clamp(available, s.MinHeight, Math.Max(s.MinHeight, s.MaxHeight))
                    : Math.Clamp(available, s.MinWidth, Math.Max(s.MinWidth, s.MaxWidth));
                var yoga = entries[i].Node;
                var actual = row ? YGNodeLayoutAPI.YGNodeLayoutGetHeight(yoga) : YGNodeLayoutAPI.YGNodeLayoutGetWidth(yoga);
                if (Math.Abs(actual - size) < .01f)
                    continue;
                // The port's stretch/basis path omits main-axis margins when deriving
                // a ratio size. An explicit cross size takes its correct definite-size path.
                SetDimension(yoga, Length.Px(size), !row);
                entries[i].Configured = false;
                changed = true;
            }
            return changed;
        }

        internal void Clear()
        {
            foreach (var entry in entries)
            {
                entry.Node.SetOwner(null);
                entry.Node.ClearChildren();
                YGNodeSetContext(entry.Node, null);
            }
            entries.Clear();
            count = 0;
        }
    }

    public static void Calculate(Context c)
    {
        var workspace = c.LayoutWorkspace;
        workspace.Prepare(c);
        for (var i = c.Count - 1; i >= 0; i--)
        {
            ref var node = ref c.Tree[i];
            var flexible = (node.TextStyle.Wrap || node.TextStyle.Overflow != TextOverflow.Visible) && node.Kind == ContentKind.Text ||
                node.Style.Wrap != FlexWrap.NoWrap;
            for (var child = node.FirstChild; child >= 0; child = c.Tree[child].Next)
                if (!c.Tree[child].Style.Absolute)
                    flexible |= c.Tree[child].Flexible;
            node.Flexible = flexible && node.Style.Width.Kind is not (LengthKind.Pixels or LengthKind.Percent);
            workspace.Configure(c, i);
        }
        YGNodeStyleAPI.YGNodeStyleSetWidth(workspace[0], c.Size.Width);
        YGNodeStyleAPI.YGNodeStyleSetHeight(workspace[0], c.Size.Height);
        YGNodeCalculateLayout(workspace[0], c.Size.Width, c.Size.Height, YGDirection.LTR);
        ResolveBoxMetrics(c);
        var positionsChanged = ResolvePositionOverrides(c);
        if (workspace.ResolveFillRatios(c) || positionsChanged)
        {
            YGNodeCalculateLayout(workspace[0], c.Size.Width, c.Size.Height, YGDirection.LTR);
            ResolveBoxMetrics(c);
        }
        Export(c, 0, default);
    }
}
