using System.Runtime.CompilerServices;
using Kumiko.UI;

namespace Kumiko.Components;


/// <summary>
/// Entry point for Kumiko.Components. Components take their value and report changes through a callback,
/// so they work in Kumiko views (dispatch a message) and legacy views (assign a field) alike.
/// </summary>
public readonly partial struct Kit(Context context)
{
    static readonly ConditionalWeakTable<Context, ScopeStack> scopes = new();

    public Context Context => context;

    /// <summary>The innermost <see cref="Use"/> theme, else the kit for the context's core theme.</summary>
    public KitTheme Theme
    {
        get
        {
            if (scopes.TryGetValue(context, out var stack) && stack.Frame == context.FrameId && stack.Themes.Count > 0)
                return stack.Themes[^1];
            return KitTheme.Of(context.Theme);
        }
    }

    /// <summary>
    /// Applies a kit theme to the window: built-in widgets and components follow it from this frame on.
    /// Repeated calls with an equal theme are free.
    /// </summary>
    public void Apply(KitTheme theme)
    {
        var core = theme.Theme;
        if (!ReferenceEquals(context.Theme, core))
            context.Theme = core;
    }

    /// <summary>
    /// Restyles components built inside the returned scope, e.g. a compact toolbar or a brand panel.
    /// Built-in Kumiko widgets keep the window theme.
    /// </summary>
    public ThemeScope Use(KitTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        var stack = scopes.GetOrCreateValue(context);
        if (stack.Frame != context.FrameId)
        {
            stack.Frame = context.FrameId;
            stack.Themes.Clear();
        }
        stack.Themes.Add(theme);
        return new(stack, stack.Themes.Count);
    }

    /// <summary>A platform shortcut label: "⇧⌘F" on macOS, "Ctrl+Shift+F" elsewhere.</summary>
    public static string ShortcutLabel(string key, Modifiers modifiers = Modifiers.Primary)
    {
        var mac = OperatingSystem.IsMacOS();
        if ((modifiers & Modifiers.Primary) != 0)
            modifiers = (modifiers & ~Modifiers.Primary) | (mac ? Modifiers.Meta : Modifiers.Control);
        if (mac)
        {
            var symbols = (modifiers.HasFlag(Modifiers.Control) ? "⌃" : "") + (modifiers.HasFlag(Modifiers.Alt) ? "⌥" : "") +
                (modifiers.HasFlag(Modifiers.Shift) ? "⇧" : "") + (modifiers.HasFlag(Modifiers.Meta) ? "⌘" : "");
            return symbols + key;
        }
        var parts = new List<string>(5);
        if (modifiers.HasFlag(Modifiers.Control))
            parts.Add("Ctrl");
        if (modifiers.HasFlag(Modifiers.Alt))
            parts.Add("Alt");
        if (modifiers.HasFlag(Modifiers.Shift))
            parts.Add("Shift");
        if (modifiers.HasFlag(Modifiers.Meta))
            parts.Add("Win");
        parts.Add(key);
        return string.Join('+', parts);
    }

    internal Div Box(NodeKey key, string source, int line) => context.Div(key, source, line);

    internal ToneColors Colors(Tone tone, KitTheme t) => tone switch
    {
        Tone.Primary => t.Primary,
        Tone.Secondary => t.Secondary,
        Tone.Danger => t.Danger,
        Tone.Success => t.Success,
        Tone.Warning => t.Warning,
        Tone.Info => t.Info,
        _ => new(t.Background, t.Foreground, t.Foreground) { Hover = t.Accent, Active = Color.Mix(t.Accent, t.Foreground, .06f) }
    };

    internal TextStyle Text(float size, Color color, Weight weight = Weight.Regular) => new(Size: size, Weight: weight, Color: color);

    internal void Hand(Interaction hit)
    {
        if (hit.Hovered)
            context.Cursor = Cursor.Hand;
    }

    /// <summary>Draws the focus ring as a border; shadcn uses a ring outside the control.</summary>
    /// <summary>The focus ring, shown for keyboard focus only.</summary>
    internal void Ring(Div box, Interaction hit, KitTheme t)
    {
        if (hit.Focused && context.FocusVisible)
            box.Border(t.Ring, t.Metrics.FocusWidth);
    }

    internal static float Clamp01(float value) => Math.Clamp(float.IsFinite(value) ? value : 0, 0, 1);

    internal sealed class ScopeStack
    {
        public long Frame = -1;
        public readonly List<KitTheme> Themes = [];
    }
}

/// <summary>Ends a <see cref="Kit.Use"/> theme scope. Dispose with using.</summary>
public readonly struct ThemeScope : IDisposable
{
    readonly Kit.ScopeStack? stack;
    readonly int depth;

    internal ThemeScope(Kit.ScopeStack stack, int depth)
    {
        this.stack = stack;
        this.depth = depth;
    }

    public void Dispose()
    {
        if (stack is not null && stack.Themes.Count >= depth)
            stack.Themes.RemoveRange(depth - 1, stack.Themes.Count - depth + 1);
    }
}
