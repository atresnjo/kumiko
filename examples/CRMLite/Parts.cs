using System.Collections.Immutable;
using Kumiko.Components;
using Kumiko.UI;

namespace CRMLite;

/// <summary>
/// The Harbor parts the desktop and phone views both draw. Each takes the platform's <see cref="PartScale"/>; the
/// iOS view opts into exact text (NativeExactText), so a label draws the size and color it asks for on both.
/// </summary>
internal static partial class Parts
{
    internal static void Label(Context c, PartScale s, string text, float size, Color color, Weight weight = Weight.Regular,
        NodeKey key = default, bool wrap = false, float leading = 1) =>
        c.Text(text, new(Size: size, Weight: weight, Color: color, Wrap: wrap, Leading: leading)
        {
            Overflow = wrap ? TextOverflow.Visible : s.Overflow
        }, key);

    /// <summary>A one-point horizontal line, optionally inset from the leading edge.</summary>
    internal static void Rule(Context c, Color color, NodeKey key = default, float inset = 0)
    {
        if (inset <= 0)
        {
            using (c.Div(key: key).FillWidth().Height(1).Background(color))
            {
            }
            return;
        }
        using var line = c.Div(key: key).FillWidth().Height(1).Padding(new Edges(0, 0, 0, inset));
        using (c.Div(key: "line").FillWidth().FillHeight().Background(color))
        {
        }
    }

    internal static void Dot(Context c, Color color, float size, NodeKey key = default)
    {
        using (c.Div(key: key).Width(size).Height(size).Rounded(size / 2).Background(color))
        {
        }
    }

    /// <summary>The company's initial on its brand color, in whichever of white or near-black reads better.</summary>
    internal static void Logo(Context c, PartScale s, Company company, float size, NodeKey key = default)
    {
        var fill = Color.Hex(company.Logo);
        var ink = KitTheme.Contrast(fill, Color.White) >= KitTheme.Contrast(fill, Color.Hex(0x111111)) ? Color.White : Color.Hex(0x111111);
        using (c.Div(key: key).Width(size).Height(size).Center().Rounded(MathF.Round(size * .26f)).Background(fill))
            Label(c, s, company.Name[..1], MathF.Max(s.MinText, MathF.Round(size * .55f)), ink, Weight.Bold, "letter");
    }

    static readonly ImmutableArray<uint> ContactColors = [0x0d9488u, 0x7c3aedu, 0xd97706u, 0x2563ebu, 0xdb2777u, 0x16a34au];

    /// <summary>Initials in a tinted circle, announced as one image named after the person.</summary>
    internal static void Avatar(Context c, Palette p, PartScale s, string name, uint color, float size, NodeKey key = default)
    {
        var (fill, ink) = p.Avatar(color);
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var initials = parts.Length > 1 ? $"{parts[0][0]}{parts[^1][0]}" : name[..1];
        using (c.Div(key: key).Width(size).Height(size).Center().Rounded(size / 2).Background(fill).Semantics(Role.Image, name))
            Label(c, s, initials, MathF.Max(s.MinText, MathF.Round(size * .4f)), ink, Weight.Semibold, "initials");
    }

    internal static void Avatar(Context c, Palette p, PartScale s, Member member, float size, NodeKey key = default) =>
        Avatar(c, p, s, member.Name, member.Color, size, key);

    /// <summary>An avatar for someone outside the team, colored from their name.</summary>
    internal static void Person(Context c, Palette p, PartScale s, string name, float size, NodeKey key = default) =>
        Avatar(c, p, s, name, ContactColors[(name.Length + name[0]) % ContactColors.Length], size, key);

    internal static void StageTag(Context c, Palette p, PartScale s, Stage stage, NodeKey key = default)
    {
        var (dot, fill, ink) = p.Of(stage);
        using var tag = c.Div(key: key).Row().Height(s.Tag.Height).ItemsCenter().Gap(s.Tag.Gap).Px(s.Tag.Padding).Rounded(s.Tag.Radius)
            .Background(fill);
        Dot(c, dot, 6, "dot");
        Label(c, s, stage.ToString(), s.Tag.Text, ink, Weight.Medium, "label");
    }

    extension(Div div)
    {
        /// <summary>
        /// The flat deal card surface. The caller sizes it and adds any platform elevation; lift (0–1) darkens the
        /// border of a hovered card.
        /// </summary>
        internal Div DealSurface(Palette p, CardScale s, float lift = 0) => div.Column()
            .Gap(s.Gap)
            .Padding(s.Padding)
            .Rounded(s.Radius)
            .Background(p.Card)
            .Border(lift > 0 ? Color.Mix(p.Border, p.Text, .12f * MathF.Min(1, lift)) : p.Border);
    }

    /// <summary>The deal card's content inside its <see cref="DealSurface"/>: company, deal, value, close date and owner.</summary>
    internal static void DealCard(Context c, Palette p, PartScale s, Deal deal, Company company, DateTime now)
    {
        var culture = c.Culture;
        using (c.Div(key: "company").Row().FillWidth().ItemsCenter().Gap(8))
        {
            Logo(c, s, company, s.Card.Logo, "logo");
            using (c.Div(key: "name").Flex().MinWidth(0))
                Label(c, s, company.Name, s.Card.Name, p.Text, Weight.Medium, "text");
        }
        Label(c, s, deal.Name, s.Card.Detail, p.Secondary, key: "deal");
        if (s.Card.FixedHeight)
            c.Spacer(key: "push");
        using var foot = c.Div(key: "foot").Row().FillWidth().Height(s.Card.Foot).ItemsCenter().Gap(s.Card.FootGap);
        Label(c, s, Format.Short(deal.Value, culture), s.Card.Name, p.Text, Weight.Semibold, "value");
        var (close, soon) = Format.Close(deal.Close, now, culture);
        Label(c, s, close, s.Card.Detail, soon && deal.Stage != Stage.Won ? p.Stale : p.Faint, key: "close");
        c.Spacer(key: "space");
        Avatar(c, p, s, Crm.Member(deal.OwnerId), s.Card.Avatar, "owner");
    }
}
