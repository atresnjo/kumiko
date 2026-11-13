using Kumiko.UI;

namespace Kumiko.iOS;

/// <summary>A versioned request from one identified navigation path.</summary>
public sealed record NativeBackRequest(string Stack, long Revision, int From, int To);
internal sealed record NativeNavigationContent(string Stack, long Revision, long Generation, NativeNavigationAppearance? Appearance = null);

/// <summary>
/// App colors for the native stack: an opaque bar with <paramref name="Background"/> (also behind every page), a title in
/// <paramref name="Title"/>, Back and bar items in <paramref name="Tint"/> and a hairline in <paramref name="Separator"/>.
/// A positive <paramref name="BottomInset"/> is the height of a bar floating over the stack's bottom edge, such as a system
/// tab bar: pages extend beneath it and their scroll views inset their content so it can scroll clear of the bar.
/// </summary>
public sealed record NativeNavigationAppearance(Color Background, Color Title, Color Tint, Color Separator,
    float TitleSize = 17, Weight TitleWeight = Weight.Semibold, float BottomInset = 0);
internal sealed record NativeNavigationPageContent(int Id, string Title, bool Guarded, string BackTitle);

/// <summary>A render-local controlled UIKit stack. Dispose after declaring all identified pages.</summary>
public sealed class NativeStackScope<TMessage> : IDisposable, INavigationStackScope
{
    readonly Ui<TMessage> ui;
    readonly UIKitControls renderer;
    readonly Div layout;
    readonly Func<NativeBackRequest, TMessage> onBack;
    readonly HashSet<int> ids = new();
    readonly List<ulong> pages = new();
    bool disposed;
    internal NativeStackScope(Ui<TMessage> ui, UIKitControls renderer, Div layout, Func<NativeBackRequest, TMessage> onBack)
    {
        this.ui = ui;
        this.renderer = renderer;
        this.layout = layout;
        this.onBack = onBack;
    }
    /// <summary>Build every retained page, in path order. Root has an identity too. Do not nest stacks or navigation bars.</summary>
    public Div Page(int id, string title, bool guarded = false)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        if (id < 0 || !ids.Add(id))
            throw new InvalidOperationException("Navigation page IDs must be unique and nonnegative.");
        var c = ui.Context;
        var size = renderer.NavigationSize(layout.Id, c.Size);
#pragma warning disable KUM001 // Caller owns the page scope.
        var page = c.Div(key: id).Absolute().Width(size.Width).Height(size.Height)
            .MaxWidth(Length.Percent(100)).MaxHeight(Length.Percent(100))
            .Semantics(Role.Group, title).Native(new NativeNavigationPageContent(id, title, guarded, c.Strings.Back));
        pages.Add(page.Id);
        return page;
#pragma warning restore KUM001
    }
    public void Dispose()
    {
        if (disposed)
            return;
        if (ids.Count == 0)
            throw new InvalidOperationException("Navigation requires an identified root page.");
        foreach (var page in pages.Take(pages.Count - 1))
            ui.Context.CoverNativePage(page);
        if (renderer.TakeValue<NativeBackRequest>(layout.Id) is { } back)
            ui.Context.WithSource(layout.Id, () => ui.Dispatch(onBack(back.Value)));
        layout.Dispose();
        disposed = true;
    }
}

public static partial class NativeComponents
{
    /// <summary>
    /// Styles the next navigation stack declared in this frame, including Ui.Stack on iOS. Call it before the stack
    /// every frame; a frame without it restores the system appearance. Pages and controllers are kept.
    /// </summary>
    public static void NativeNavigationAppearance(this Context context, NativeNavigationAppearance appearance)
    {
        ArgumentNullException.ThrowIfNull(appearance);
        if (!float.IsFinite(appearance.TitleSize) || appearance.TitleSize <= 0 || !float.IsFinite(appearance.BottomInset) ||
            appearance.BottomInset < 0)
            throw new ArgumentOutOfRangeException(nameof(appearance), "The title size must be positive and the bottom inset finite and nonnegative.");
        RequireRenderer(context).PendingNavigationAppearance = appearance;
    }

    /// <summary>Renders a model-owned path using UINavigationController. Native user actions return as messages.</summary>
    public static NativeStackScope<TMessage> NativeStack<TMessage>(this Ui<TMessage> ui, string stack, long revision,
        Func<NativeBackRequest, TMessage> onBack, NodeKey key = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stack);
        ArgumentNullException.ThrowIfNull(onBack);
        if (revision < 0)
            throw new ArgumentOutOfRangeException(nameof(revision));
        var renderer = RequireRenderer(ui.Context);
        for (var i = 0; i < ui.Context.Count; i++)
            if (ui.Context.Tree[i].NativeContent is NativeNavigationContent)
                throw new InvalidOperationException("A NativeView supports one navigation stack; nested stacks are unsupported.");
#pragma warning disable KUM001 // NativeStackScope owns the layout.
        var layout = ui.Context.Div(key).SizeFull().Semantics(Role.Group, stack)
            .Native(new NativeNavigationContent(stack, revision, renderer.NavigationGeneration, renderer.TakeNavigationAppearance()));
#pragma warning restore KUM001
        return new(ui, renderer, layout, onBack);
    }
}
