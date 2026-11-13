using System.Collections.Immutable;
using Kumiko.UI;
using static Flint.Palette;

namespace Flint;

public sealed partial class FlintView
{
    const float DiffRow = 18;
    /// <summary>The original SearchableDiff: a Find bar over a virtual unified diff with word-level emphasis.</summary>
    void DiffView(RepoTab tab)
    {
        using var diff = C.Div(key: "diff").Flex().FillHeight().MinWidth(0).Background(Canvas);
        var matches = tab.Matches;
        if (tab.Finding)
        {
            using (C.Div(key: "find-bar").FillWidth().Row().Wrap().ItemsCenter().Gap(6).Padding(6).Background(Toolbar))
            {
                C.Icon(Icons.Search, 14, Muted, key: "icon");
                Field("query", "Find in diff", tab.Find, static text => new Msg.FindChanged(text), "Find in diff", 220);
                using (C.Div(key: "count").MinWidth(66).Center().Semantics(Role.Text, "Search results"))
                    Label(tab.Find.Length == 0 ? "" : matches.IsEmpty ? "No matches" : $"{Math.Min(tab.Match + 1, matches.Length)} of {matches.Length}", "label", 11,
                        matches.IsEmpty ? Muted : Text);
                Button("Previous", new Msg.StepMatch(-1), "previous", enabled: matches.Length > 0);
                Button("Next", new Msg.StepMatch(1), "next", enabled: matches.Length > 0);
                Button("Done", new Msg.SetFind(false), "done");
            }
            Rule("find-rule");
        }
        else
        {
            using (C.Div(key: "find").FillWidth().Height(28).Row().ItemsCenter().Justify(Justify.End).Padding(new Edges(0, 8, 0, 0)).Background(Toolbar))
                Button("Find ⌘F", new Msg.SetFind(true), "find", name: "Find");
            Rule("find-rule");
        }
        using var body = C.Div(key: "diff-body").FillWidth().Flex().MinHeight(0).Clip();
        if (tab.Lines.IsDefaultOrEmpty)
        {
            Empty(tab.Page == Page.Changes && tab.File is null ? "No local changes"
                : tab.Patch.Length > 0 || tab.Detail is { Files.Length: > 0 } ? "No textual changes (binary or mode change)" : "Loading…");
            return;
        }
        var reveal = tab.RevealDiff && tab.Finding && matches.Length > 0 ? matches[Math.Clamp(tab.Match, 0, matches.Length - 1)].Row : (int?)null;
        C.UniformList("patch:" + (tab.File ?? "all"), tab.Lines.Length, DiffRow,
            (_, index) => DiffLineView(tab, index, matches), revealRow: reveal, rowKey: i => StableId(tab.Lines[i].Id));
        if (tab.RevealDiff)
            Ui.Dispatch(new Msg.Revealed(tab.Snapshot.Path, false));
    }
    void DiffLineView(RepoTab tab, int index, ImmutableArray<DiffMatch> matches)
    {
        var line = tab.Lines[index];
        var header = line.Kind == 'f';
        using var row = C.Div(key: "line").FillWidth().Height(DiffRow).Row().ItemsCenter().Clip()
            .Background(line.Kind switch
            {
                '+' => Added,
                '-' => Removed,
                '@' => Color.Hex(0xf3f6fd),
                'f' => Toolbar,
                'm' or '\\' => Pane,
                _ => Canvas
            });
        if (header)
        {
            using var file = C.Div(key: "file").Flex().Row().ItemsCenter().Gap(6).Padding(new Edges(0, 0, 0, 10));
            C.Icon(Icons.File, 12, Muted, key: "icon");
            Label(line.Text, "path", 12, weight: Weight.Semibold);
            return;
        }
        using (C.Div(key: "old").Width(44).Items(Align.End).Padding(new Edges(0, 6, 0, 0)))
            Label(line.Old?.ToString(C.Culture) ?? "", "number", 11, line.Kind is '+' or '-' ? MutedStrong : Muted, mono: true);
        using (C.Div(key: "new").Width(44).Items(Align.End).Padding(new Edges(0, 6, 0, 0)))
            Label(line.New?.ToString(C.Culture) ?? "", "number", 11, line.Kind is '+' or '-' ? MutedStrong : Muted, mono: true);
        using (C.Div(key: "sign").Width(14))
            Label(line.Kind is '+' or '-' ? line.Kind.ToString() : "", "text", 12, line.Kind == '+' ? Green : Red, mono: true);
        using var code = C.Div(key: "code").Flex().Clip();
        var cut = Cut(line.Text);
        var spans = ImmutableArray.CreateBuilder<TextSpan>();
        void Add(TextSpan span)
        {
            if (span.Start < cut)
                spans.Add(span with
                {
                    End = Math.Min(span.End, cut)
                });
        }
        var words = line.Words;
        for (var i = 0; i + 1 < words.Length; i += 2)
            Add(new(words[i], words[i] + words[i + 1], Background: line.Kind == '+' ? AddedWord : RemovedWord));
        if (tab.Finding && tab.Find.Length > 0)
            for (var i = First(matches, index); i < matches.Length && matches[i].Row == index; i++)
                Add(new(matches[i].Start, matches[i].Start + matches[i].Length, Color: Text,
                        Background: i == tab.Match ? Color.Hex(0xffc670) : Color.Hex(0xffeeb0)));
        var style = new TextStyle(Size: 12, Color: line.Kind is '@' or 'm' or '\\' ? Muted : Text, Mono: true);
        C.RichText(cut < line.Text.Length ? line.Text[..cut] + "…" : line.Text, [.. spans], style, key: "text");
    }
    /// <summary>
    /// Characters of a diff line a row lays out. The diff has no horizontal scrolling and this spans several screens,
    /// so a minified megabyte line costs no more to shape, lay out and paint than an ordinary one.
    /// </summary>
    const int ShownCharacters = 2000;
    /// <summary>Length of the displayed prefix of a line, never splitting a surrogate pair.</summary>
    static int Cut(string text) => text.Length <= ShownCharacters ? text.Length
        : char.IsHighSurrogate(text[ShownCharacters - 1]) ? ShownCharacters - 1 : ShownCharacters;
    /// <summary>Index of the first match on a row; matches are ordered by row.</summary>
    static int First(ImmutableArray<DiffMatch> matches, int row)
    {
        int low = 0, high = matches.Length;
        while (low < high)
        {
            var mid = (low + high) / 2;
            if (matches[mid].Row < row)
                low = mid + 1;
            else
                high = mid;
        }
        return low;
    }
}
