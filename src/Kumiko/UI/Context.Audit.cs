using System.Globalization;

namespace Kumiko.UI;

/// <summary>An accessibility problem in the latest frame: code, node path, explanation and node identity.</summary>
public sealed record AccessibilityFinding(string Code, string Path, string Message, ulong Node);

public sealed partial class Context
{
    /// <summary>Smallest pointer target in logical pixels (WCAG 2.2 success criterion 2.5.8, level AA).</summary>
    public const float MinimumTargetSize = 24;

    static readonly HashSet<Role> StandaloneRoles = [Role.Button, Role.Checkbox, Role.Switch, Role.Slider, Role.TextField, Role.ComboBox];

    /// <summary>
    /// Checks the latest frame for problems that automated inspection can find: unnamed controls
    /// (<c>unnamed-control</c>), pointer targets below 24×24 (<c>target-size</c>), standalone controls that
    /// keyboard users cannot reach (<c>keyboard</c>), indistinguishable sibling controls (<c>duplicate-name</c>)
    /// and text below WCAG contrast (<c>contrast</c>). Disabled, hidden and decorative content is skipped; resize
    /// separators are exempt from target size because the keyboard offers the same function. Passing the audit
    /// does not replace testing with screen readers.
    /// </summary>
    public IReadOnlyList<AccessibilityFinding> AuditAccessibility()
    {
        CheckThread();
        var findings = new List<AccessibilityFinding>();
        void Report(string code, int index, string message) => findings.Add(new(code, PathOf(index), message, Tree[index].Id));
        var (backgrounds, opacities) = Composited();
        var names = new Dictionary<(int Parent, Role Role, string Name), int>();
        for (var i = 0; i < Count && i < accessibility.Count; i++)
        {
            ref readonly var n = ref Tree[i];
            var semantic = accessibility[i];
            if (n.Kind == ContentKind.Text && opacities[i] > 0 && !semantic.Offscreen && n.Text is { Length: > 0 } && !InsideDisabled(i) &&
                TextContrast(i, backgrounds[i], opacities[i]) is { } problem)
                Report("contrast", i, problem);
            if (!n.Style.Control || !n.Interactive || semantic.Offscreen)
                continue;
            if (string.IsNullOrWhiteSpace(semantic.Name))
                Report("unnamed-control", i, $"{n.Role} has no accessible name; give icon-only controls Semantics(role, name).");
            if (n.Role != Role.Separator && Undersized(i) && Crowded(i))
                Report("target-size", i, string.Create(CultureInfo.InvariantCulture,
                    $"Pointer target is {n.Rect.Width:0}×{n.Rect.Height:0} and crowded; make it {MinimumTargetSize:0}×{MinimumTargetSize:0} or space it out."));
            if (n.Style.SkipTab && StandaloneRoles.Contains(n.Role) && !KeyboardReachable(i))
                Report("keyboard", i, $"{n.Role} is not a Tab stop and nothing nearby provides keyboard access.");
            if (semantic.Name.Length > 0 && n.Parent >= 0 && !names.TryAdd((n.Parent, n.Role, semantic.Name), i))
                Report("duplicate-name", i, $"Another {n.Role} beside it is also named {Quote(semantic.Name)}.");
        }
        return findings;
    }

    bool Undersized(int index) => Tree[index].Rect.Width < MinimumTargetSize || Tree[index].Rect.Height < MinimumTargetSize;

    /// <summary>
    /// The WCAG 2.5.8 spacing exception: an undersized target passes when a 24 px circle on its center overlaps
    /// no other target and no other undersized target's circle. Containing and contained controls are ignored.
    /// </summary>
    bool Crowded(int index)
    {
        var rect = Tree[index].Rect;
        float cx = rect.X + rect.Width / 2, cy = rect.Y + rect.Height / 2, radius = MinimumTargetSize / 2;
        for (var i = 0; i < Count && i < accessibility.Count; i++)
        {
            ref readonly var other = ref Tree[i];
            if (i == index || !other.Style.Control || !other.Interactive || accessibility[i].Offscreen || Related(i, index))
                continue;
            var box = other.Rect.Intersect(other.ClipRect);
            float dx = Math.Max(Math.Max(box.X - cx, 0), cx - box.Right), dy = Math.Max(Math.Max(box.Y - cy, 0), cy - box.Bottom);
            if (dx * dx + dy * dy < radius * radius)
                return true;
            float ox = box.X + box.Width / 2 - cx, oy = box.Y + box.Height / 2 - cy;
            if (Undersized(i) && ox * ox + oy * oy < MinimumTargetSize * MinimumTargetSize)
                return true;
        }
        return false;
    }

    bool Related(int a, int b)
    {
        for (var i = Tree[a].Parent; i >= 0; i = Tree[i].Parent)
            if (i == b)
                return true;
        for (var i = Tree[b].Parent; i >= 0; i = Tree[i].Parent)
            if (i == a)
                return true;
        return false;
    }

    /// <summary>Solid backgrounds and opacities composited down the tree, as text over them is seen.</summary>
    (Color[] Backgrounds, float[] Opacities) Composited()
    {
        var backgrounds = new Color[Count];
        var opacities = new float[Count];
        for (var i = 0; i < Count; i++)
        {
            var n = Tree[i];
            var background = n.Parent >= 0 ? backgrounds[n.Parent] : Theme.Background;
            opacities[i] = n.Style.Opacity * (n.Parent >= 0 ? opacities[n.Parent] : 1);
            backgrounds[i] = Composite((n.Style.Background ?? Color.Transparent).Alpha(opacities[i]), background);
        }
        return (backgrounds, opacities);
    }

    /// <summary>A WCAG 1.4.3 contrast failure for a text node, or null.</summary>
    string? TextContrast(int index, Color background, float opacity)
    {
        var n = Tree[index];
        var minimum = n.TextStyle.Size >= 24 || n.TextStyle.Size >= 18.667f && n.TextStyle.Weight >= Weight.Semibold ? 3 : 4.5f;
        var contrast = Contrast(Composite((n.TextStyle.Color ?? Theme.Text).Alpha(opacity), background), background);
        if (contrast < minimum)
            return string.Create(CultureInfo.InvariantCulture, $"Text contrast {contrast:0.##}:1 is below {minimum:0.#}:1.");
        if (n.Spans is { } spans)
            foreach (var span in spans)
                if (span.Color is { } color && Contrast(Composite(color.Alpha(opacity), background), background) < minimum)
                    return "Rich text span has insufficient contrast.";
        return null;
    }

    /// <summary>Text in inactive controls is exempt from contrast requirements (WCAG 1.4.3).</summary>
    bool InsideDisabled(int index)
    {
        for (var i = index; i >= 0; i = Tree[i].Parent)
            if (Tree[i].Disabled)
                return true;
        return false;
    }

    /// <summary>
    /// A control outside the Tab order is reachable when its composite (a grid, tree, list, menu, combo box or
    /// named group) holds a Tab stop that moves focus within it, as in roving focus or a spin button's steppers.
    /// </summary>
    bool KeyboardReachable(int index)
    {
        var composite = Tree[index].Parent;
        while (composite >= 0 && !(Tree[composite].Role is Role.Grid or Role.Tree or Role.List or Role.Menu or Role.ComboBox ||
            Tree[composite].Role == Role.Group && !string.IsNullOrEmpty(Tree[composite].Name)))
            composite = Tree[composite].Parent;
        if (composite < 0)
            return false;
        bool Reachable(int node)
        {
            for (var child = Tree[node].FirstChild; child >= 0; child = Tree[child].Next)
            {
                ref readonly var n = ref Tree[child];
                if (child != index && n.Interactive && !n.Style.SkipTab || Reachable(child))
                    return true;
            }
            return false;
        }
        return Reachable(composite);
    }
}
