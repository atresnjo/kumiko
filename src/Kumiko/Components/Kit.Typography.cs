using System.Runtime.CompilerServices;
using Kumiko.UI;

namespace Kumiko.Components;

/// <summary>shadcn typography roles, scaled from the kit's control font size.</summary>
public readonly partial struct Kit
{
    /// <summary>Page title with an accessibility heading role.</summary>
    public void H1(string text, NodeKey key = default, [CallerFilePath] string source = "", [CallerLineNumber] int line = 0) =>
        context.Heading(text, Theme.FontSize * 2.15f, key, source, line);

    public void H2(string text, NodeKey key = default, [CallerFilePath] string source = "", [CallerLineNumber] int line = 0) =>
        context.Heading(text, Theme.FontSize * 1.7f, key, source, line);

    public void H3(string text, NodeKey key = default, [CallerFilePath] string source = "", [CallerLineNumber] int line = 0) =>
        context.Heading(text, Theme.FontSize * 1.43f, key, source, line);

    public void H4(string text, NodeKey key = default, [CallerFilePath] string source = "", [CallerLineNumber] int line = 0) =>
        context.Heading(text, Theme.FontSize * 1.15f, key, source, line);

    /// <summary>Wrapped body copy.</summary>
    public void P(string text, NodeKey key = default, [CallerFilePath] string source = "", [CallerLineNumber] int line = 0) =>
        context.Text(text, Text(Theme.FontSize, Theme.Foreground) with
        {
            Wrap = true,
            Leading = 1.5f
        }, key, source, line);

    /// <summary>Larger muted introduction under a title.</summary>
    public void Lead(string text, NodeKey key = default, [CallerFilePath] string source = "", [CallerLineNumber] int line = 0) =>
        context.Text(text, Text(Theme.FontSize + 3, Theme.MutedForeground) with
        {
            Wrap = true
        }, key, source, line);

    public void Large(string text, NodeKey key = default, [CallerFilePath] string source = "", [CallerLineNumber] int line = 0) =>
        context.Text(text, Text(Theme.FontSize + 2, Theme.Foreground, Weight.Semibold), key, source, line);

    public void Small(string text, NodeKey key = default, [CallerFilePath] string source = "", [CallerLineNumber] int line = 0) =>
        context.Text(text, Text(Theme.FontSize - 2, Theme.Foreground, Weight.Medium), key, source, line);

    /// <summary>Secondary text in the muted foreground.</summary>
    public void Muted(string text, NodeKey key = default, [CallerFilePath] string source = "", [CallerLineNumber] int line = 0) =>
        context.Text(text, Text(Theme.FontSize, Theme.MutedForeground), key, source, line);

    /// <summary>Inline monospace code on a muted chip.</summary>
    public void Code(string text, NodeKey key = default, [CallerFilePath] string source = "", [CallerLineNumber] int line = 0)
    {
        var t = Theme;
        using (Box(key, source, line).Px(5).Py(2).Rounded(t.RadiusSmall).Background(t.Muted))
            context.Text(text, new(Size: t.FontSize - 1, Weight: Weight.Medium, Color: t.Foreground, Mono: true), key: "code");
    }
}
