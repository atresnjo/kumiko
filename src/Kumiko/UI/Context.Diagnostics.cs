using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Kumiko.UI;
/// <summary>A development warning with stable tree paths and a repairable explanation.</summary>
public sealed record LayoutWarning(string Code, string Path, string Message, string? RelatedPath = null, int Node = -1, int? RelatedNode = null);
public sealed partial class Context
{
    readonly List<LayoutWarning> warnings = new();
    /// <summary>Enables post-layout diagnostics. Disabled by default for production views.</summary>
    public bool DevMode
    {
        get; set;
    }
    /// <summary>Warnings from the most recent completed frame, in tree order.</summary>
    public IReadOnlyList<LayoutWarning> Warnings => warnings;

    internal string DumpTree()
    {
        var result = new StringBuilder();
        for (var i = 0; i < Count; i++)
        {
            ref readonly var n = ref Tree[i];
            int depth = 0;
            for (var p = n.Parent; p >= 0; p = Tree[p].Parent)
                depth++;
            result.Append(' ', depth * 2).Append(n.Kind == ContentKind.None ? "Div" : n.Kind.ToString());
            result.Append(" key=").Append(Quote(n.Key));
            result.Append(CultureInfo.InvariantCulture, $" rect=({n.Rect.X:0.###},{n.Rect.Y:0.###},{n.Rect.Width:0.###},{n.Rect.Height:0.###})");
            result.Append(" text=").Append(Quote(n.Text));
            result.Append(" role=").Append(n.Role).Append(" name=").Append(Quote(accessibility[i].Name));
            result.Append(" flags=[");
            var flags = new List<string>();
            if (n.LayoutHidden)
                flags.Add("hidden");
            if (n.Style.Layout.Display == DisplayMode.Contents)
                flags.Add("contents");
            if (n.Interactive)
                flags.Add("interactive");
            if (n.Disabled)
                flags.Add("disabled");
            if (n.Id == focus)
                flags.Add("focused");
            if (n.Checked)
                flags.Add("checked");
            if (n.Style.Clip || n.Style.Scroll)
                flags.Add("clips");
            if (n.Rect.Intersect(n.ClipRect) != n.Rect)
                flags.Add("clipped");
            if (n.Style.Scroll && Scrolls.TryGetValue(n.Id, out var scroll))
                flags.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"scroll={(scroll.Horizontal ? "x" : "y")}:{scroll.Offset:0.###}/{scroll.Max:0.###}"
                ));
            result.AppendJoin(',', flags).Append("]\n");
        }

        return result.ToString();
    }

    void InspectLayout()
    {
        warnings.Clear();
        var keys = new Dictionary<(ulong, string), int>();
        var sites = new Dictionary<(ulong, string, int), int>();
        var controls = new List<int>();
        var backgrounds = new Color[Count];
        var opacities = new float[Count];
        for (var i = 0; i < Count; i++)
        {
            var n = Tree[i];
            if (n.LayoutHidden)
                continue;
            var parent = n.LayoutParent >= 0 ? Tree[n.LayoutParent] : default;
            var background = n.Parent >= 0 ? backgrounds[n.Parent] : Theme.Background;
            float opacity = n.Style.Opacity * (n.Parent >= 0 ? opacities[n.Parent] : 1);
            opacities[i] = opacity;
            backgrounds[i] = n.Style.Layout.Display == DisplayMode.Contents ? background :
                Composite((n.Style.Background ?? Color.Transparent).Alpha(opacity), background);
            if (n.Key is { } key)
            {
                if (keys.TryGetValue((n.IdentityParent, key), out var other))
                    Warn("duplicate-key", i, $"Sibling key {Quote(key)} is already in use.", other);
                else
                    keys[(n.IdentityParent, key)] = i;
            }
            else if (n.Source is { Length: > 0 } source)
            {
                if (sites.TryGetValue((n.IdentityParent, source, n.SourceLine), out var other))
                    Warn("unkeyed-loop", i, "Repeated elements from the same construction site need stable keys.", other);
                else
                    sites[(n.IdentityParent, source, n.SourceLine)] = i;
            }

            if (n.Style.Layout.Display == DisplayMode.Contents)
                continue;
            if (n.MissingRowKey)
                Warn("unkeyed-row", i, "UniformList needs rowKey for identity when rows change order.");
            if (n.Parent >= 0 && !parent.Style.Clip && !parent.Style.Scroll && Outside(n.Rect, parent.Rect))
                Warn("overflow", i, "Child extends outside a parent that does not clip.", n.Parent);
            if (n.Kind == ContentKind.Text)
            {
                var measured = n.TextStyle.Overflow != TextOverflow.Visible
                    ? TextSystem.Display(n.Text ?? "", n.TextStyle, n.Rect.Width, n.RightToLeft, n.Spans).Size
                    : TextSystem.Measure(n.Text ?? "", n.TextStyle, n.TextStyle.Wrap ? n.Rect.Width : float.PositiveInfinity);
                if (measured.Width > n.Rect.Width + .5f || measured.Height > n.Rect.Height + .5f)
                    Warn("text-overflow", i, "Text extends beyond its layout box.");
                // A natural-sized text leaf may itself overflow its constrained container.
                else if (n.Parent >= 0 && !parent.Style.Clip && !parent.Style.Scroll && Outside(n.Rect, parent.Rect))
                    Warn("text-overflow", i, "Text extends beyond its parent box.", n.Parent);
                // Text in inactive controls is exempt (WCAG 1.4.3), matching AuditAccessibility.
                if (opacity > 0 && n.Text is { Length: > 0 } && !InsideDisabled(i))
                {
                    var fg = Composite((n.TextStyle.Color ?? Theme.Text).Alpha(opacity), backgrounds[i]);
                    float contrast = Contrast(fg, backgrounds[i]);
                    float minimum = n.TextStyle.Size >= 24 || n.TextStyle.Size >= 18.667f && n.TextStyle.Weight >= Weight.Semibold ? 3 : 4.5f;
                    if (contrast < minimum)
                        Warn(
                            "contrast",
                            i,
                            string.Create(CultureInfo.InvariantCulture, $"Text contrast {contrast:0.##}:1 is below {minimum:0.#}:1.")
                        );
                    if (n.Spans is { } spans)
                        foreach (var span in spans)
                            if (span.Color is { } color && Contrast(Composite(color.Alpha(opacity), backgrounds[i]), backgrounds[i]) < minimum)
                                Warn("contrast", i, "Rich text span has insufficient contrast.");
                }
            }

            if (!n.Style.Control)
                continue;
            if (n.Rect.Width <= 0 || n.Rect.Height <= 0)
                Warn("empty-control", i, "Interactive control has zero layout size.");
            if (!n.Interactive || opacity <= 0)
                continue;
            var visible = n.Rect.Intersect(n.ClipRect);
            if (visible is not { Width: > 0, Height: > 0 })
                continue;
            foreach (var other in controls)
            {
                // Nested controls intentionally contain one another (rows, drag handles).
                if (DifferentDebuggerLayers(other, i) || OverlayLayer(other) != OverlayLayer(i) || IsAncestor(other, i) || IsAncestor(i, other))
                    continue;
                var intersection = visible.Intersect(Tree[other].Rect.Intersect(Tree[other].ClipRect));
                if (intersection is { Width: > .5f, Height: > .5f })
                    Warn("overlap", i, "Interactive controls overlap in the visible viewport.", other);
            }

            controls.Add(i);
        }
    }

    bool IsAncestor(int ancestor, int node)
    {
        for (var p = Tree[node].Parent; p >= 0; p = Tree[p].Parent)
            if (p == ancestor)
                return true;
        return false;
    }

    void Warn(
        string code,
        int node,
        string message,
        int related = -1
    ) => warnings.Add(new(
        code,
        PathOf(node),
        message,
        related >= 0 ? PathOf(related) : null,
        node,
        related >= 0 ? related : null
    ));
    static bool Outside(Rect child, Rect parent) => child.X < parent.X - .5f || child.Y < parent.Y - .5f || child.Right > parent.Right + .5f || child.Bottom > parent.Bottom + .5f;
    static Color Composite(Color fg, Color bg) => new(fg.R * fg.A + bg.R * (1 - fg.A), fg.G * fg.A + bg.G * (1 - fg.A), fg.B * fg.A + bg.B * (1 - fg.A));
    static float Contrast(Color a, Color b)
    {
        static float Linear(float v) => v <= .04045f ? v / 12.92f : MathF.Pow((v + .055f) / 1.055f, 2.4f);
        static float Luminance(Color c) => .2126f * Linear(c.R) + .7152f * Linear(c.G) + .0722f * Linear(c.B);
        float x = Luminance(a), y = Luminance(b);
        return (Math.Max(x, y) + .05f) / (Math.Min(x, y) + .05f);
    }

    /// <summary>Writes deterministic, reflection-free diagnostics suitable for NativeAOT.</summary>
    public void WriteWarnings(string path)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!);
        using var stream = File.Create(path);
        using var writer = new Utf8JsonWriter(stream, new()
        {
            Indented = true
        });
        writer.WriteStartObject();
        writer.WriteNumber("version", 1);
        writer.WriteStartArray("warnings");
        foreach (var warning in warnings)
        {
            writer.WriteStartObject();
            writer.WriteString("code", warning.Code);
            writer.WriteString("path", warning.Path);
            writer.WriteString("message", warning.Message);
            writer.WriteNumber("node", warning.Node);
            if (warning.RelatedNode is { } relatedNode)
                writer.WriteNumber("relatedNode", relatedNode);
            if (warning.RelatedPath is { } related)
                writer.WriteString("relatedPath", related);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }
}
