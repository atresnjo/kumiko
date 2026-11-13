namespace Kumiko.UI;

public sealed partial class Tester
{
    /// <summary>Dumps the completed layout, one node per line, with invariant geometry and escaped text.</summary>
    public string Dump() => Context.DumpTree();
    /// <summary>Writes diagnostics from the last frame. Enable TesterOptions.DevMode to collect them.</summary>
    public void WriteWarnings(string path) => Context.WriteWarnings(path);
    /// <summary>Finds exactly one visible control by semantic role and accessible name.</summary>
    public Locator Get(Role role, string name) => new(this, role, name);
    public Locator Get(Role role, string name, bool includeOffscreen) => new(this, role, name, null, includeOffscreen);
    /// <summary>Finds the visible control of <paramref name="role"/> at <paramref name="index"/> in tree order, such as the first row.</summary>
    public Locator Nth(Role role, int index, bool includeOffscreen = false) => new(this, role, null, null, includeOffscreen, index);
    /// <summary>A live semantic query. Each operation resolves the current frame and rejects ambiguity.</summary>
    public sealed class Locator
    {
        readonly Tester tester;
        readonly Role role;
        readonly string? name;
        readonly Locator? scope;
        readonly bool includeOffscreen;
        readonly int? index;
        internal Locator(Tester tester, Role role, string? name, Locator? scope = null, bool includeOffscreen = false, int? index = null)
        {
            if (index < 0)
                throw new ArgumentOutOfRangeException(nameof(index));
            this.tester = tester;
            this.role = role;
            this.name = name;
            this.scope = scope;
            this.includeOffscreen = includeOffscreen;
            this.index = index;
            Resolve();
        }

        string Label => index is { } at ? $"at index {at}" : Context.Quote(name ?? "");

        AccessibleNode Resolve()
        {
            var matches = tester.Context.Query(new(role, name, Within: scope?.Id, IncludeOffscreen: includeOffscreen));
            if (index is { } at)
                return at < matches.Count ? matches[at] : throw new InvalidOperationException($"Expected a visible {role} at index {at}; found {matches.Count}.");
            if (matches.Count != 1)
                throw new InvalidOperationException($"Expected one visible {role} named {Label}; found {matches.Count}.");
            return matches[0];
        }

        public ulong Id => Resolve().Id;
        public Rect Rect => Resolve().Rect;
        public bool Enabled => Resolve().Enabled;
        /// <summary>The accessible name, useful after <see cref="Nth"/>.</summary>
        public string Name => Resolve().Name;
        /// <summary>The accessible value, such as a text field's text or a picker's choice.</summary>
        public string? Value => Resolve().Value;
        public Locator Get(Role role, string name, bool includeOffscreen = false) => new(tester, role, name, this, includeOffscreen);
        public Locator Nth(Role role, int index, bool includeOffscreen = false) => new(tester, role, null, this, includeOffscreen, index);

        /// <summary>True when visible text equal to <paramref name="text"/> is drawn inside this control's bounds.</summary>
        public bool HasText(string text)
        {
            ArgumentNullException.ThrowIfNull(text);
            var bounds = Resolve().Rect;
            foreach (ref readonly var node in tester.Context.Nodes)
            {
                if (node.Text != text || node.Rect.Intersect(node.ClipRect) is not { Width: > 0, Height: > 0 } visible)
                    continue;
                if (bounds.Contains(new Point(visible.X + visible.Width / 2, visible.Y + visible.Height / 2)))
                    return true;
            }
            return false;
        }
        public void Invoke() => Perform(new(AccessibilityAction.Invoke));
        public void Fill(string value) => Perform(new(AccessibilityAction.SetValue, Text: value));
        public void Expand() => Perform(new(AccessibilityAction.Expand));
        public void Collapse() => Perform(new(AccessibilityAction.Collapse));
        public void Select() => Perform(new(AccessibilityAction.Select));
        public void Reveal() => Perform(new(AccessibilityAction.Reveal));
        public void Scroll(double pixels) => Perform(new(AccessibilityAction.Scroll, Number: pixels));
        public void Perform(AccessibilityRequest request)
        {
            var result = tester.Context.PerformAction(Resolve().Id, request);
            if (!result.Succeeded)
                throw new InvalidOperationException($"{request.Action} on {role} {Label}: {result.Status}. {result.Detail}");
            tester.Settle();
        }

        /// <summary>Clicks through Kumiko's input pipeline. Rejects disabled or fully occluded controls.</summary>
        public void Click()
        {
            var node = Resolve();
            if (!node.Enabled)
                throw new InvalidOperationException($"{role} {Label} is disabled.");
            if (tester.Context.ControlPoint(node.Id) is { } point)
            {
                tester.Click(point.X, point.Y);
                return;
            }
            throw new InvalidOperationException($"{role} {Label} is covered by another control.");
        }
    }
}

public sealed partial class Context
{
    internal Point? ControlPoint(ulong id)
    {
        int target = hits.FindLastIndex(h => h.Id == id && h.Interactive);
        if (target < 0)
            return null;
        var regions = new List<Rect> { hits[target].Rect };
        for (var i = target + 1; i < hits.Count; i++)
        {
            if (!hits[i].Interactive)
                continue;
            var remaining = new List<Rect>();
            foreach (var rect in regions)
            {
                var overlap = rect.Intersect(hits[i].Rect);
                if (overlap.Width <= 0 || overlap.Height <= 0)
                {
                    remaining.Add(rect);
                    continue;
                }
                if (overlap.X > rect.X)
                    remaining.Add(new(rect.X, rect.Y, overlap.X - rect.X, rect.Height));
                if (overlap.Right < rect.Right)
                    remaining.Add(new(overlap.Right, rect.Y, rect.Right - overlap.Right, rect.Height));
                if (overlap.Y > rect.Y)
                    remaining.Add(new(overlap.X, rect.Y, overlap.Width, overlap.Y - rect.Y));
                if (overlap.Bottom < rect.Bottom)
                    remaining.Add(new(overlap.X, overlap.Bottom, overlap.Width, rect.Bottom - overlap.Bottom));
            }
            regions = remaining;
        }
        if (regions.Count == 0)
            return null;
        var best = regions.MaxBy(r => r.Width * r.Height);
        return new(best.X + best.Width / 2, best.Y + best.Height / 2);
    }
}
