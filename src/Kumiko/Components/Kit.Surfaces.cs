using System.Runtime.CompilerServices;
using Kumiko.UI;

namespace Kumiko.Components;

public readonly partial struct Kit
{
    /// <summary>A bordered card surface. Dispose with using; chain Div methods to adjust it.</summary>
    public Div Card(NodeKey key = default, [CallerFilePath] string source = "", [CallerLineNumber] int line = 0)
    {
        var t = Theme;
        var card = Box(key, source, line)
            .Column()
            .Padding(20)
            .Gap(16)
            .Background(t.Card)
            .Border(t.Border)
            .Rounded(t.RadiusLarge * 1.5f);
        if (t.Shadows)
            card.Shadow(3, 1, Color.Black.Alpha(t.IsDark ? .25f : .05f));
        return card;
    }

    /// <summary>Card title with optional muted description.</summary>
    public void CardHeader(
        string title,
        string? description = null,
        NodeKey key = default,
        [CallerFilePath] string source = "",
        [CallerLineNumber] int line = 0
    )
    {
        var t = Theme;
        using var header = Box(key, source, line).Column().FillWidth().Gap(4);
        context.Text(title, Text(t.FontSize + 2, t.CardForeground, Weight.Semibold), key: "title");
        if (description is not null)
            context.Text(description, Text(t.FontSize, t.MutedForeground) with
            {
                Wrap = true
            }, key: "description");
    }

    /// <summary>A right-aligned action row for cards and dialogs.</summary>
    public Div Footer(NodeKey key = default, [CallerFilePath] string source = "", [CallerLineNumber] int line = 0) =>
        Box(key, source, line).Row().FillWidth().ItemsCenter().Justify(Justify.End).Gap(8);

    /// <summary>A titled, muted container for a group of related settings (gpui-kit GroupBox).</summary>
    public KitScope GroupBox(string? title = null, NodeKey key = default, [CallerFilePath] string source = "",
        [CallerLineNumber] int line = 0)
    {
        var t = Theme;
#pragma warning disable KUM001 // The returned composite scope owns and disposes these Divs.
        var outer = Box(key, source, line).Column().FillWidth().Gap(8);
#pragma warning restore KUM001
        if (title is not null)
            context.Text(title, Text(t.FontSize, t.MutedForeground, Weight.Medium), key: "title");
#pragma warning disable KUM001 // Disposed by the returned KitScope.
        var body = context.Div(key: "body")
            .Column()
            .FillWidth()
            .Gap(12)
            .Padding(16)
            .Rounded(t.RadiusLarge)
            .Background(t.IsDark ? Color.White.Alpha(.03f) : Palette.Neutral50)
            .Border(t.Border);
#pragma warning restore KUM001
        return new(outer, body);
    }

    /// <summary>A bordered callout with a tone icon, title and optional description.</summary>
    public void Alert(
        string title,
        string? description = null,
        AlertProps props = default,
        NodeKey key = default,
        [CallerFilePath] string source = "",
        [CallerLineNumber] int line = 0
    )
    {
        var t = Theme;
        var neutral = props.Tone == Tone.Default;
        var colors = Colors(props.Tone, t);
        var compact = props.Size is ControlSize.XSmall or ControlSize.Small;
        using var box = Box(key, source, line)
            .Row()
            .FillWidth()
            .Gap(12)
            .Padding(Edges.XY(compact ? 12 : 16, compact ? 8 : 12))
            .Rounded(props.Size == ControlSize.Large ? t.RadiusLarge : t.Radius)
            .Background(neutral ? t.Card : Color.Mix(t.Background, colors.Background, t.IsDark ? .08f : .05f))
            .Border(neutral ? t.Border : Color.Mix(t.Background, colors.Background, .35f))
            .Semantics(Role.Alert, title);
        var accent = neutral ? t.Foreground : colors.Text;
        var icon = props.Icon ?? props.Tone switch
        {
            Tone.Success => "checkmark.circle.fill",
            Tone.Warning => "exclamationmark.triangle",
            Tone.Danger => "xmark.octagon",
            _ => "info.circle"
        };
        context.Icon(icon, 16, accent, key: "icon");
        using var text = context.Div(key: "text").Flex().Column().Gap(2);
        context.Text(title, Text(t.FontSize, accent, Weight.Medium), key: "title");
        if (description is not null)
            context.Text(description, Text(t.FontSize, neutral ? t.MutedForeground : accent) with
            {
                Wrap = true
            }, key: "description");
    }

    /// <summary>
    /// An accordion item. Build the item's content only when <see cref="Section.Open"/> is true, then dispose.
    /// </summary>
    public Section AccordionItem(
        string title,
        bool open,
        Action<bool> toggled,
        NodeKey key = default,
        [CallerFilePath] string source = "",
        [CallerLineNumber] int line = 0
    )
    {
        var t = Theme;
#pragma warning disable KUM001 // The returned composite scope owns and disposes these Divs.
        var outer = Box(key, source, line).Column().FillWidth();
#pragma warning restore KUM001
        using (var header = context.Div(key: "trigger")
            .Row()
            .FillWidth()
            .ItemsCenter()
            .Gap(8)
            .Py(14)
            .Rounded(t.RadiusSmall)
            .Semantics(Role.Button, title)
            .Accessibility(new(Expanded: open)))
        {
            var hit = header.Control();
            Ring(header, hit, t);
            Hand(hit);
            var style = Text(t.FontSize, t.Foreground, Weight.Medium);
            using (context.Div(key: "title").Flex())
            {
                if (hit.Hovered)
                    context.RichText(title, [new(0, title.Length, Underline: true)], style, key: "label");
                else
                    context.Text(title, style, key: "label");
            }
            context.Icon(open ? "chevron.up" : "chevron.down", 16, t.MutedForeground, key: "chevron");
            if (hit.Clicked)
                toggled(!open);
        }
#pragma warning disable KUM001 // Disposed by the returned Section.
        Div? content = open ? context.Div(key: "content").Column().FillWidth().Gap(8).Padding(new Edges(0, 0, 14, 0)) : null;
#pragma warning restore KUM001
        return new(context, outer, content, open, t.Border);
    }
}

/// <summary>Closes a composite component scope, innermost first.</summary>
public readonly struct KitScope : IDisposable
{
    readonly Div outer;
    readonly Div? inner;
    readonly Div? body;

    internal KitScope(Div outer, Div? inner = null, Div? body = null)
    {
        this.outer = outer;
        this.inner = inner;
        this.body = body;
    }

    public void Dispose()
    {
        body?.Dispose();
        inner?.Dispose();
        outer.Dispose();
    }
}

/// <summary>An expandable item scope. Add content only when <see cref="Open"/>; dispose to close it.</summary>
public readonly struct Section : IDisposable
{
    readonly Context context;
    readonly Div outer;
    readonly Div? content;
    readonly Color rule;
    readonly bool vertical;

    internal Section(Context context, Div outer, Div? content, bool open, Color rule, bool vertical = false)
    {
        this.context = context;
        this.outer = outer;
        this.content = content;
        this.rule = rule;
        this.vertical = vertical;
        Open = open;
    }

    public bool Open
    {
        get;
    }

    public void Dispose()
    {
        content?.Dispose();
        if (vertical)
        {
            using (context.Div(key: "rule").Width(1).FillHeight().Background(rule))
            {
            }
        }
        else
        {
            using (context.Div(key: "rule").FillWidth().Height(1).Background(rule))
            {
            }
        }
        outer.Dispose();
    }
}
