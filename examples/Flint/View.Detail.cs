using System.Collections.Immutable;
using System.Globalization;
using Flint.Git;
using Kumiko.UI;
using static Flint.Palette;

namespace Flint;

public sealed partial class FlintView
{
    static readonly ImmutableArray<string> Months = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];
    static readonly ImmutableArray<string> MonthNames = ["January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December"];
    /// <summary>The original CommitDetail: Commit and Changes tabs over the selected commit.</summary>
    void CommitDetail(RepoTab tab, float sidebarSpace)
    {
        using var detail = C.Div(key: "details").FillWidth().Flex().MinHeight(0).Background(Canvas);
        using (C.Div(key: "detail-tabs").FillWidth().Height(32).Row().ItemsCenter().Gap(4).Padding(new Edges(0, 0, 0, 10)).Background(Pane))
        {
            DetailTab("Commit", new Msg.SetChangesTab(false), "commit", !tab.ChangesTab);
            DetailTab("Changes", new Msg.SetChangesTab(true), "changes", tab.ChangesTab);
        }
        Rule("detail-rule");
        if (tab.Sha is null)
            Empty("Select a commit");
        else if (tab.Detail is not { } info)
            Empty("Loading…");
        else if (tab.ChangesTab)
            CommitChanges(tab, info, sidebarSpace);
        else
            CommitInfo(tab, info);
    }
    void DetailTab(string label, Msg message, NodeKey key, bool active)
    {
        using var box = C.Div(key: key).Height(22).Px(14).Center().Rounded(5);
        var hit = Ui.Button(box, label, message, true);
        if (active)
            box.Background(Color.Hex(0xe4e4ea));
        else if (hit.Hovered)
            box.Background(Hover);
        if (hit.Focused && C.FocusVisible)
            box.Border(Accent, 2);
        Label(label, "label", 13, active ? Text : MutedStrong, active ? Weight.Semibold : Weight.Regular);
    }
    /// <summary>The message scrolls with the file list; only visible file rows are built.</summary>
    void CommitInfo(RepoTab tab, CommitDetail detail)
    {
        using var info = C.Div(key: "info").FillWidth().Flex().MinHeight(0);
        C.UniformList(detail.Sha, Math.Min(detail.Files.Length, 2000), 23, header: _ => CommitHeader(detail), row: (_, index) =>
        {
            var file = detail.Files[index];
            using var inset = C.Div(key: "inset").FillWidth().Height(22).Padding(new Edges(0, 24, 0, 54));
            FileRow(file, file.Path == tab.File, new Msg.SelectFile(file.Path), MenuKind.CommitFile, "File: " + file.Path);
        }, footer: _ =>
        {
            using var end = C.Div(key: "end").Height(23);
        }, rowKey: index => StableId(detail.Files[index].Path));
    }
    void CommitHeader(CommitDetail detail)
    {
        using var body = C.Div(key: "body").FillWidth().Padding(new Edges(18, 24, 0, 24)).Gap(14);
        using (C.Div(key: "author").Row().Gap(14))
        {
            AvatarView(detail.Avatar, 40, 4);
            using var identity = C.Div(key: "identity").Gap(3).MinWidth(0);
            Label("AUTHOR", "heading", 10, Muted, Weight.Medium);
            using (C.Div(key: "name").Row().ItemsCenter().Gap(8))
            {
                Label(detail.Author, "author", 14, weight: Weight.Semibold);
                Label(detail.Email, "email", 12, Muted);
            }
            Label(LongDate(detail.Time), "date", 12, Muted);
            if (detail.Committer.Length > 0 && detail.Committer != detail.Author)
                Label($"Committed by {detail.Committer} · {LongDate(detail.CommitTime)}", "committer", 11, Muted);
        }
        using (C.Div(key: "meta").Gap(6).Margin(new Edges(0, 0, 0, -10)))
        {
            if (!detail.Refs.IsEmpty)
                using (C.Div(key: "refs").FillWidth().Row())
                {
                    MetaLabel("REFS");
                    using var badges = C.Div(key: "badges").Row().Wrap().Gap(5).Grow().Shrink().MinWidth(0);
                    foreach (var reference in detail.Refs)
                        RefBadge(reference, reference.Kind + ":" + reference.Name, true);
                }
            using (C.Div(key: "sha").Row().ItemsCenter())
            {
                MetaLabel("SHA");
                Label(detail.Sha, "value", 12, mono: true);
            }
            using (C.Div(key: "parents").Row().ItemsCenter())
            {
                MetaLabel("PARENTS");
                using var links = C.Div(key: "links").Row().Gap(8);
                if (detail.Parents.IsEmpty)
                    Label("none", "none", 12, Muted);
                foreach (var parent in detail.Parents)
                {
                    using var link = C.Div(key: parent).Rounded(3);
                    var hit = Ui.Button(link, "Parent " + parent[..7], new Msg.SelectCommit(parent), true);
                    if (hit.Focused && C.FocusVisible)
                        link.Border(Accent, 2);
                    C.RichText(parent[..7], [new(0, 7, Underline: true)], new(Size: 12, Color: Accent, Mono: true), key: "sha");
                }
            }
        }
        Rule("metadata-rule");
        using (C.Div(key: "message").FillWidth().Gap(10).Padding(new Edges(0, 0, 0, 54)))
        {
            C.Text(detail.Subject, new(Size: 15, Weight: Weight.Semibold, Color: Text, Wrap: true), key: "subject");
            if (detail.Body.Length > 0)
                C.Text(detail.Body, new(Size: 12, Color: Text, Wrap: true, Mono: true, Leading: 17 / 14.4f), key: "body");
        }
        Rule("message-rule");
        using var count = C.Div(key: "count").Padding(new Edges(0, 0, 5, 42));
        Label($"{detail.Files.Length} file{(detail.Files.Length == 1 ? "" : "s")} changed", "label", 11, Muted);
    }
    void MetaLabel(string text)
    {
        using var label = C.Div(key: "label").Width(64).Items(Align.End).Padding(new Edges(0, 10, 0, 0));
        Label(text, "text", 10, Muted, Weight.Medium);
    }
    void CommitChanges(RepoTab tab, CommitDetail detail, float sidebarSpace)
    {
        var maximum = MathF.Max(180, C.Size.Width - sidebarSpace - 240 - ResizeThickness);
        var width = Math.Clamp(tab.CommitFilesWidth, 180, maximum);
        using var changes = C.Div(key: "commit-changes").FillWidth().Flex().MinHeight(0).Row();
        using (C.Div(key: "files").Width(width).FillHeight().MinWidth(0))
        {
            using (var all = C.Div(key: "all").Height(26).Row().ItemsCenter().Padding(new Edges(0, 0, 0, 12)).Margin(new Edges(4, 4, 0, 4)).Rounded(4))
            {
                var hit = Ui.Button(all, "All files", new Msg.SelectFile(null), true);
                if (tab.File is null)
                    all.Background(Accent);
                else if (hit.Hovered)
                    all.Background(Hover);
                Label($"All files ({detail.Files.Length})", "label", 12, tab.File is null ? Color.White : Text, Weight.Semibold);
            }
            using (C.Div(key: "list").FillWidth().Flex().MinHeight(0))
                C.UniformList("commit-files", detail.Files.Length, 22, (_, index) =>
                {
                    var file = detail.Files[index];
                    using var inset = C.Div(key: "inset").FillWidth().FillHeight().Px(4);
                    FileRow(file, file.Path == tab.File, new Msg.SelectFile(file.Path), MenuKind.CommitFile, "File: " + file.Path);
                }, rowKey: index => StableId(detail.Files[index].Path));
        }
        Ui.ResizeHandle("Resize commit files", width, static value => new Msg.ResizeCommitFiles(value),
            new(Min: 180, Max: maximum), key: "files-resize");
        DiffView(tab);
    }
    /// <summary>The original flat FileRow: status square, muted directory, basename and rename source.</summary>
    void FileRow(FileChange file, bool selected, Msg message, MenuKind menu, string name)
    {
        using var row = C.Div(key: "file").FillWidth().FillHeight().Row().ItemsCenter().Gap(6).Padding(new Edges(0, 8, 0, 8)).Rounded(4).Clip();
        var hit = Ui.Button(row, name, message, true);
        if (selected)
            row.Background(Accent);
        else if (hit.Hovered)
            row.Background(Hover);
        if (hit.Focused && C.FocusVisible)
            row.Border(Accent, 2);
        var path = file.Path;
        Ui.OnContextMenu(hit, at => new Msg.OpenMenu(at, menu, path));
        StatusSquare(file.Status);
        var slash = file.Path.LastIndexOf('/');
        if (slash >= 0)
            Label(file.Path[..(slash + 1)], "dir", 12, selected ? SelectedMuted : Muted);
        Label(file.Path[(slash + 1)..], "base", 12, selected ? Color.White : Text);
        if (file.OriginalPath is { } original)
            Label("← " + original, "original", 11, selected ? SelectedMuted : Muted);
    }
    void StatusSquare(char status)
    {
        using var square = C.Div(key: "status").Width(14).Height(14).Center().Rounded(3).Background(Status(status)).Semantics(Role.Image, "Status " + status);
        Label(StatusGlyph(status), "glyph", 11, Color.White, Weight.Bold);
    }
    /// <summary>Fork style: "Today at 11:02", "Yesterday at 09:10", "23. Sep 2026 at 12:40", relative to the snapshot's load time.</summary>
    static string ShortDate(long time, long now)
    {
        var date = DateTimeOffset.FromUnixTimeSeconds(time).ToLocalTime();
        var clock = date.ToString("HH:mm", CultureInfo.InvariantCulture);
        if (now > 0)
        {
            var today = DateTimeOffset.FromUnixTimeSeconds(now).ToLocalTime().Date;
            if (date.Date == today)
                return "Today at " + clock;
            if (date.Date == today.AddDays(-1))
                return "Yesterday at " + clock;
        }
        return $"{date.Day}. {Months[date.Month - 1]} {date.Year} at {clock}";
    }
    static string LongDate(long time)
    {
        var date = DateTimeOffset.FromUnixTimeSeconds(time).ToLocalTime();
        var hours = date.Offset.TotalHours;
        return $"{date.Day}. {MonthNames[date.Month - 1]} {date.Year} at {date.ToString("HH:mm:ss", CultureInfo.InvariantCulture)} GMT{(hours >= 0 ? "+" : "")}{hours.ToString(CultureInfo.InvariantCulture)}";
    }
}
