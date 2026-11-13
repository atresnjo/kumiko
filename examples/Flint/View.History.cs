using Flint.Git;
using Kumiko.UI;
using static Flint.Palette;

namespace Flint;

public sealed partial class FlintView
{
    /// <summary>The original CommitsView: a fixed-height virtual commit list, a drag handle, then the detail pane.</summary>
    void HistoryView(RepoTab tab, float sidebarSpace)
    {
        var commits = tab.Snapshot.Commits;
        using (C.Div(key: "history").FillWidth().Height(MathF.Min(tab.HistoryHeight, MathF.Max(120, C.Size.Height - 260))).Padding(new Edges(4, 4, 0, 4)))
        {
            if (commits.IsEmpty)
                Empty(tab.Snapshot.Head.Unborn ? "No commits yet" : "Loading history…");
            else
            {
                var selected = commits.FindIndex(c => c.Sha == tab.Sha);
                var lanes = 1;
                foreach (var row in tab.Snapshot.Graph)
                    lanes = Math.Max(lanes, row.Width);
                var graphWidth = MathF.Min(lanes, MaxLanes) * LaneWidth + 10;
                var path = tab.Snapshot.Path;
                var more = tab.Snapshot.HasMore && !tab.LoadingMore;
                C.UniformList("commits", commits.Length, RowHeight, (_, index) =>
                {
                    CommitRow(tab, index, graphWidth);
                    // Load further history as the list nears its end, like the original's onVisibleRange.
                    if (more && (index == Math.Max(0, commits.Length - 300) || index == commits.Length - 1))
                        Ui.Dispatch(new Msg.LoadMore(path));
                }, revealRow: tab.RevealHistory && selected >= 0 ? selected : null, rowKey: index => StableId(commits[index].Sha));
                if (tab.RestoreScroll)
                    RestoreScroll(tab);
                else if (tab.RevealHistory)
                    Ui.Dispatch(new Msg.Revealed(path, true));
            }
        }
        Ui.ResizeHandle("Resize history", tab.HistoryHeight, static height => new Msg.ResizeHistory(height),
            new(Orientation: Kumiko.Components.Orientation.Horizontal, Min: 120, Max: 1400), key: "history-resize");
        CommitDetail(tab, sidebarSpace);
        if (!C.IsEditing)
            HistoryKeys();
    }
    /// <summary>
    /// Sets the offset once this tab's list exists in the accessibility tree; the leaving tab's list is a different node,
    /// so the action fails until then and one more frame is requested.
    /// </summary>
    void RestoreScroll(RepoTab tab)
    {
        if (C.Accessibility.FirstOrDefault(n => n.Role == Role.List && n.Key == "commits") is { } list
            && C.PerformAccessibilityAction(list.Id, new(AccessibilityAction.SetRange, Number: tab.HistoryScroll)))
            Ui.Dispatch(new Msg.Revealed(tab.Snapshot.Path, true));
        else
            C.FrameIn(TimeSpan.Zero);
    }
    void HistoryKeys()
    {
        var delta = C.Shortcut(Key.Down, Modifiers.None) ? 1 : C.Shortcut(Key.Up, Modifiers.None) ? -1
            : C.Shortcut(Key.PageDown, Modifiers.None) ? 25 : C.Shortcut(Key.PageUp, Modifiers.None) ? -25
            : C.Shortcut(Key.Home, Modifiers.None) ? int.MinValue : C.Shortcut(Key.End, Modifiers.None) ? int.MaxValue : 0;
        if (delta != 0)
            Ui.Dispatch(new Msg.StepCommit(delta));
    }
    void CommitRow(RepoTab tab, int index, float graphWidth)
    {
        var commit = tab.Snapshot.Commits[index];
        var selected = commit.Sha == tab.Sha;
        var ink = selected ? Color.White : Text;
        using var row = C.Div(key: "commit").FillWidth().Height(RowHeight).Row().ItemsCenter().Padding(new Edges(0, 10, 0, 0)).Rounded(4);
        var hit = Ui.Button(row, "Commit: " + commit.Subject, new Msg.SelectCommit(commit.Sha), true);
        if (selected)
            row.Background(Accent);
        else if (hit.Hovered)
            row.Background(Color.Hex(0xf2f2f6));
        if (hit.Focused && C.FocusVisible)
            row.Border(Accent, 2);
        if (index < tab.Snapshot.Graph.Length)
            GraphCell(tab.Snapshot.Graph[index], graphWidth);
        using (C.Div(key: "subject").Flex().MinWidth(0).Row().ItemsCenter().Gap(4).Padding(new Edges(0, 8, 0, 0)).Clip())
        {
            foreach (var reference in commit.Refs)
                RefBadge(reference, reference.Kind + ":" + reference.Name);
            Subject(commit.Subject, ink);
        }
        using (C.Div(key: "author").Width(AuthorWidth).Row().ItemsCenter().Gap(6).Clip())
        {
            AvatarView(commit.Avatar, 15, 3);
            Label(commit.Author, "name", 13, ink, selected ? Weight.Semibold : Weight.Regular);
        }
        using (C.Div(key: "sha").Width(ShaWidth).Clip())
            Label(commit.Sha[..Math.Min(7, commit.Sha.Length)], "text", 12, selected ? SelectedMuted : Muted, mono: true);
        using (C.Div(key: "date").Width(DateWidth).Clip())
            Label(ShortDate(commit.Time, tab.Snapshot.LoadedAt), "text", 13, ink, selected ? Weight.Semibold : Weight.Regular);
    }
    /// <summary>Emphasises a conventional-commit prefix such as feat(ui)!: like the original Subject.</summary>
    void Subject(string text, Color color)
    {
        var prefix = ConventionalPrefix(text);
        C.RichText(text, prefix > 0 ? [new(0, prefix, Weight: Weight.Semibold)] : [], new(Size: 13, Color: color), key: "text");
    }
    /// <summary>Length of a `type(scope)!:` prefix, or 0. Equivalent to the original /^([\w-]+(?:\([^)]*\))?!?:)/.</summary>
    static int ConventionalPrefix(string text)
    {
        var i = 0;
        while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] is '_' or '-'))
            i++;
        if (i == 0)
            return 0;
        if (i < text.Length && text[i] == '(')
        {
            var close = text.IndexOf(')', i);
            if (close < 0)
                return 0;
            i = close + 1;
        }
        if (i < text.Length && text[i] == '!')
            i++;
        return i < text.Length && text[i] == ':' ? i + 1 : 0;
    }
    /// <summary>The original RefBadge: yellow local/HEAD, white remote with a cloud, blue tag; a check marks the current branch.</summary>
    void RefBadge(RefLabel reference, NodeKey key, bool medium = false)
    {
        var local = reference.Kind is RefKind.Local or RefKind.Head;
        using var badge = C.Div(key: key).Row().ItemsCenter().Gap(3).Height(medium ? 18 : 16).MaxWidth(320).Padding(new Edges(0, 5, 0, 4)).Rounded(3).Clip()
            .Border(local ? YellowBorder : reference.Kind == RefKind.Tag ? TagBorder : BorderStrong)
            .Background(local ? YellowBadge : reference.Kind == RefKind.Tag ? TagBadge : Canvas);
        if (reference.Current)
            C.Icon(Icons.Check, 10, Text, key: "current");
        if (reference.Kind == RefKind.Remote)
            C.Icon(Icons.Cloud, 10, Muted, key: "cloud");
        if (reference.Kind == RefKind.Tag)
            C.Icon(Icons.Tag, 10, Color.Hex(0x3e63dd), key: "tag");
        Label(reference.Name, "name", medium ? 12 : 11, Text, reference.Current ? Weight.Semibold : Weight.Regular);
    }
    /// <summary>The original local 5x5 identicon on #f0f0f2, drawn as horizontal runs of cells.</summary>
    void AvatarView(Avatar avatar, float size, float radius)
    {
        using var box = C.Div(key: "avatar").Width(size).Height(size).Rounded(radius).Background(Color.Hex(0xf0f0f2)).Clip()
            .Semantics(Role.Image, "Avatar");
        var cell = size / 6;
        var color = Color.Hex(avatar.Color);
        for (var y = 0; y < 5; y++)
        {
            var x = 0;
            while (x < 5)
            {
                if (!On(avatar, x, y))
                {
                    x++;
                    continue;
                }
                var start = x;
                while (x < 5 && On(avatar, x, y))
                    x++;
                using (C.Div(key: y * 8 + start).Absolute().Offset((start + .5f) * cell, (y + .5f) * cell).Width((x - start) * cell).Height(cell).Background(color))
                {
                }
            }
        }
    }
    static bool On(Avatar avatar, int x, int y) => (avatar.Cells >> (y * 3 + (x < 3 ? x : 4 - x)) & 1) != 0;
    const float Mid = RowHeight / 2, Stroke = 2, Elbow = 5;
    static float LaneX(int lane) => lane * LaneWidth + LaneWidth / 2 + 4;
    /// <summary>One graph row from straight quads, rounded elbows and a node dot, as in the original GraphCell.</summary>
    void GraphCell(GraphRow graph, float width)
    {
        using var cell = C.Div(key: "graph").Width(width).Height(RowHeight).Clip();
        var node = LaneX(graph.Lane);
        foreach (var segment in graph.Top)
        {
            if (segment.Lane >= MaxLanes)
                continue;
            var color = Lane(segment.Color);
            var x = LaneX(segment.Lane);
            if (segment.Other == segment.Lane)
            {
                Quad("t" + segment.Lane, x - Stroke / 2, 0, Stroke, Mid, color);
                continue;
            }
            // Converge into the node: down, round the corner, across.
            var right = segment.Lane > segment.Other;
            Quad("tv" + segment.Lane, x - Stroke / 2, 0, Stroke, Mid - Elbow, color);
            Corner("tc" + segment.Lane, right ? x - Elbow : x - Stroke / 2, Mid - Elbow, right ? -1 : 0, -1, color);
            var a = right ? node : x + Elbow;
            var b = right ? x - Elbow : node;
            Quad("th" + segment.Lane, a, Mid - Stroke / 2, MathF.Max(0, b - a), Stroke, color);
        }
        foreach (var segment in graph.Bottom)
        {
            if (segment.Lane >= MaxLanes)
                continue;
            var color = Lane(segment.Color);
            var x = LaneX(segment.Lane);
            if (segment.Other == segment.Lane)
            {
                Quad("b" + segment.Lane, x - Stroke / 2, Mid, Stroke, RowHeight - Mid, color);
                continue;
            }
            // Branch out of the node: across, round the corner, down.
            var right = segment.Lane > segment.Other;
            var a = right ? node : x + Elbow;
            var b = right ? x - Elbow : node;
            Quad("bh" + segment.Lane, a, Mid - Stroke / 2, MathF.Max(0, b - a), Stroke, color);
            Corner("bc" + segment.Lane, right ? x - Elbow : x - Stroke / 2, Mid - Stroke / 2, right ? -1 : 0, 0, color);
            Quad("bv" + segment.Lane, x - Stroke / 2, Mid + Elbow, Stroke, RowHeight - Mid - Elbow, color);
        }
        if (graph.Lane < MaxLanes)
        {
            var color = Lane(graph.Color);
            var size = graph.Merge ? 8f : 10f;
            using var dot = C.Div(key: "dot").Absolute().Offset(node - size / 2, Mid - size / 2).Width(size).Height(size).Rounded(size / 2)
                .Background(graph.Merge ? Canvas : color).Border(graph.Merge ? color : Canvas, 2);
        }
    }
    void Quad(NodeKey key, float x, float y, float width, float height, Color color)
    {
        using var quad = C.Div(key: key).Absolute().Offset(x, y).Width(width).Height(height).Background(color);
    }
    /// <summary>A quarter of a rounded ring: a clipped 6x6 window onto a 12x12 bordered circle, shifted to one quadrant.</summary>
    void Corner(NodeKey key, float x, float y, int shiftX, int shiftY, Color color)
    {
        const float Size = Elbow + Stroke / 2;
        using var window = C.Div(key: key).Absolute().Offset(x, y).Width(Size).Height(Size).Clip();
        using var ring = C.Div(key: "ring").Absolute().Offset(shiftX * Size, shiftY * Size).Width(Size * 2).Height(Size * 2).Rounded(Size).Border(color, Stroke);
    }
}
