using System.Collections.Immutable;
using Kumiko.Icons;
using Kumiko.UI;

namespace CRMLite;

/// <summary>The activity timeline: day groups, a kind icon rail, headlines with stage changes and note cards.</summary>
internal static partial class Parts
{
    internal static void Timeline(Context c, Palette p, PartScale s, ImmutableArray<Activity> items, DateTime now, NodeKey key)
    {
        var culture = c.Culture;
        var today = DateOnly.FromDateTime(now);
        using var list = c.Div(key: key).FillWidth().Column().Gap(Space.S5);
        if (items.IsEmpty)
        {
            Label(c, s, "Nothing here yet.", s.Timeline.Text, p.Secondary, key: "empty");
            return;
        }
        foreach (var day in items.GroupBy(a => DateOnly.FromDateTime(a.At)))
        {
            using var group = c.Div(key: day.Key.DayNumber).FillWidth().Column().Gap(Space.S2);
            Label(c, s, Format.Day(day.Key, today, culture), s.Timeline.Heading, p.Faint, Weight.Medium, "heading");
            var entries = day.ToImmutableArray();
            for (var i = 0; i < entries.Length; i++)
                Entry(c, p, s, entries[i], i < entries.Length - 1);
        }
    }

    static string IconOf(ActivityKind kind) => kind switch
    {
        ActivityKind.Email => Hugeicons.Mail01,
        ActivityKind.Note => Hugeicons.Note01,
        ActivityKind.Meeting => Hugeicons.Calendar03,
        ActivityKind.Call => Hugeicons.Call,
        ActivityKind.StageChange => Hugeicons.ArrowLeftRight,
        _ => Hugeicons.AddCircle
    };

    internal static string Verb(ActivityKind kind) => kind switch
    {
        ActivityKind.Email => "emailed",
        ActivityKind.Note => "added a note",
        ActivityKind.Meeting => "met",
        ActivityKind.Call => "called",
        ActivityKind.StageChange => "moved the deal",
        _ => "created"
    };

    static void Entry(Context c, Palette p, PartScale s, Activity a, bool connected)
    {
        var t = s.Timeline;
        using var row = c.Div(key: a.Id).Row().FillWidth().Items(Align.Stretch).Gap(Space.S3);
        using (c.Div(key: "rail").Width(t.Rail).Column().Items(Align.Center).Gap(4))
        {
            using (c.Div(key: "icon").Height(t.Line).Center())
                c.Icon(IconOf(a.Kind), t.Icon, a.Kind == ActivityKind.Note ? p.Stale : p.Faint, key: "glyph");
            if (connected && a.Body is not null)
            {
                using (c.Div(key: "thread").Width(1).Grow().Background(p.Border))
                {
                }
            }
        }
        using var content = c.Div(key: "content").Flex().MinWidth(0).Column().Gap(6);
        // Wrapping headlines keep the time in a column of its own; a headline that stays on one line ends with it.
        using (c.Div(key: "head").Row().FillWidth().Items(Align.Start).Gap(8))
        {
            using (var line = c.Div(key: "line").Flex().MinWidth(0).Row().Wrap().ItemsCenter().MinHeight(t.Line))
            {
                if (t.Wrap)
                    line.ColumnGap(5).RowGap(2);
                else
                    line.Gap(5);
                Label(c, s, a.Actor, t.Text, p.Text, Weight.Medium, "actor");
                Label(c, s, a.Kind == ActivityKind.Created ? $"created {a.Title}" : Verb(a.Kind), t.Text, p.Faint, key: "verb");
                if (a is { Kind: ActivityKind.StageChange, From: { } from, To: { } to })
                {
                    StageTag(c, p, s, from, "from");
                    using (c.Div(key: "change").Row().ItemsCenter().Gap(5))
                    {
                        c.Icon(Hugeicons.ArrowRight01, t.Arrow, p.Faint, key: "arrow");
                        StageTag(c, p, s, to, "to");
                    }
                }
                else if (a.Kind != ActivityKind.Created && a.Title.Length > 0)
                    Label(c, s, a.Title, t.Text, p.Text, Weight.Medium, "title", wrap: t.Wrap);
                if (!t.Wrap)
                {
                    c.Spacer(key: "space");
                    Label(c, s, Format.Time(a.At, c.Culture), t.Time, p.Faint, key: "time");
                }
            }
            if (t.Wrap)
            {
                using (c.Div(key: "when").Height(t.Line).Center())
                    Label(c, s, Format.Time(a.At, c.Culture), t.Time, p.Faint, key: "time");
            }
        }
        if (a.Detail is { } detail)
            Label(c, s, detail, t.Detail, p.Faint, key: "detail", wrap: t.Wrap);
        if (a.Body is not { } body)
            return;
        var note = a.Kind == ActivityKind.Note;
        using (c.Div(key: "body").FillWidth().Padding(t.Body).Rounded(t.Radius)
            .Background(note ? p.NoteFill : p.Page).Border(note ? p.NoteBorder : p.Border))
            Label(c, s, body, t.Text, note ? p.NoteText : p.Secondary, key: "text", wrap: true, leading: 1.35f);
    }
}
