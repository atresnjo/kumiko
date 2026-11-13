using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Kumiko.Components;
using Kumiko.UI;

namespace Kumiko.iOS;

public static partial class NativeComponents
{
    /// <summary>Begins a native styled container. The caller owns its layout and must dispose it.</summary>
    public static Div NativeSurface(this Context context, NativeSurfaceOptions options, NodeKey key = default,
        [CallerFilePath] string source = "", [CallerLineNumber] int line = 0)
    {
        RequireRenderer(context);
        if (!float.IsFinite(options.Radius) || options.Radius < 0 || !float.IsFinite(options.Elevation) || options.Elevation < 0)
            throw new ArgumentOutOfRangeException(nameof(options));
        return context.Div(key, source, line).Native(new NativeSurfaceContent(options));
    }

    /// <summary>Adds native text with an explicit size, weight and color, scaled by Dynamic Type.</summary>
    public static void NativeLabel(this Context context, string text, TextStyle style, NodeKey key = default,
        [CallerFilePath] string source = "", [CallerLineNumber] int line = 0)
    {
        RequireRenderer(context);
        if (!float.IsFinite(style.Size) || style.Size < 11)
            throw new ArgumentOutOfRangeException(nameof(style));
        context.Text(text, style, key, source, line);
        context.Tree[context.Count - 1].NativeContent = new NativeLabelContent(text, style);
    }

    /// <summary>Adds a message-bound SF Symbol action, chip or illuminated primary button.</summary>
    public static void NativeAction<TMessage>(this Ui<TMessage> ui, string label, TMessage onPress,
        NativeActionOptions options = default, NativePalette? palette = null, NodeKey key = default)
    {
        var c = ui.Context;
        var renderer = RequireRenderer(c);
        if (options.Width is { } width && (!float.IsFinite(width) || width < 44) ||
            !float.IsFinite(options.Height) || options.Height < 0)
            throw new ArgumentOutOfRangeException(nameof(options));
        var content = new NativeActionContent(label, options, palette ?? NativePalette.FocusDark);
        var measured = renderer.MeasureAction(content);
        using var action = c.Div(key).Height(Math.Max(44, options.Height)).Native(content);
        if (options.FillWidth)
            action.FillWidth();
        else
            action.Width(options.Width ?? measured.Width);
        ui.Button(action, label, onPress, !options.Disabled);
    }

    /// <summary>A themed, scrolling multi-selection list. Toggling sends a stable row ID.</summary>
    public static void NativeCheckList<TMessage>(this Ui<TMessage> ui, string label,
        ImmutableArray<NativeListItem> items, ImmutableHashSet<string> selectedIds,
        Func<string, TMessage> onToggle, NativePalette? palette = null, NodeKey key = default)
    {
        var c = ui.Context;
        var renderer = RequireRenderer(c);
        ArgumentNullException.ThrowIfNull(selectedIds);
        ArgumentNullException.ThrowIfNull(onToggle);
        if (items.IsDefault || items.Any(item => item is null || string.IsNullOrWhiteSpace(item.Id)) ||
            items.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != items.Length)
            throw new ArgumentException("Supply initialized rows with unique domain IDs.", nameof(items));
        using var list = c.Div(key).FillWidth().Grow().Semantics(Role.List, label)
            .Native(new NativeListContent(label, items, null, selectedIds, palette ?? NativePalette.FocusDark));
        if (renderer.TakeSelection(list.Id) is { } selected && items.Any(item => item.Id == selected && item.Enabled))
            c.WithSource(list.Id, () => ui.Dispatch(onToggle(selected)));
    }

    /// <summary>A system UITabBar with 2–5 stable, message-bound destinations.</summary>
    public static void NativeTabBar<TMessage>(this Ui<TMessage> ui, ImmutableArray<NativeTabItem> items,
        string selectedId, Func<string, TMessage> onSelect, NativePalette? palette = null, NodeKey key = default)
    {
        var renderer = RequireRenderer(ui.Context);
        ValidateTabs(items, selectedId, onSelect);
        var width = Math.Min(600, ui.Context.Size.Width);
        using var bar = ui.Context.Div(key).FillWidth().MaxWidth(600).Height(renderer.MeasureTabBar(width))
            .Native(new NativeTabBarContent(items, selectedId, palette ?? NativePalette.FocusDark));
        TabSelection(ui, renderer, bar, items, onSelect);
    }

    /// <summary>
    /// The height the system tab bar occupies at the bottom edge of this host, home-indicator area included. Pass it
    /// as <see cref="NativeNavigationAppearance.BottomInset"/> when the bar floats over a navigation stack.
    /// </summary>
    public static float NativeTabBarHeight(this Context context)
    {
        var renderer = RequireRenderer(context);
        // UIKit's measurement already reserves a bottom inset on recent systems; never add the home indicator twice.
        return MathF.Max(renderer.MeasureTabBar(context.Size.Width), 49 + context.SafeAreaInsets.Bottom);
    }

    /// <summary>
    /// The system tab bar: UIKit's material (Liquid Glass on iOS 26), symbols and badges, with <paramref name="tint"/>
    /// on the selected item. Place it at the bottom edge of an edge-to-edge host, usually floating over the content;
    /// it is <see cref="NativeTabBarHeight"/> high and UIKit keeps its items above the home indicator.
    /// </summary>
    public static void NativeTabBar<TMessage>(this Ui<TMessage> ui, ImmutableArray<NativeTabItem> items,
        string selectedId, Func<string, TMessage> onSelect, Color tint, NodeKey key = default)
    {
        var renderer = RequireRenderer(ui.Context);
        ValidateTabs(items, selectedId, onSelect);
        using var bar = ui.Context.Div(key).FillWidth().Height(ui.Context.NativeTabBarHeight())
            .Native(new NativeTabBarContent(items, selectedId, null, tint));
        TabSelection(ui, renderer, bar, items, onSelect);
    }

    static void ValidateTabs<TMessage>(ImmutableArray<NativeTabItem> items, string selectedId, Func<string, TMessage> onSelect)
    {
        ArgumentNullException.ThrowIfNull(onSelect);
        if (items.IsDefault || items.Length is < 2 or > 5 || items.Any(item => item is null || string.IsNullOrWhiteSpace(item.Id)) ||
            items.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != items.Length ||
            !items.Any(item => item.Id == selectedId))
            throw new ArgumentException("Supply 2–5 unique destinations and a valid selected ID.", nameof(items));
    }

    static void TabSelection<TMessage>(Ui<TMessage> ui, UIKitControls renderer, Div bar, ImmutableArray<NativeTabItem> items,
        Func<string, TMessage> onSelect)
    {
        bar.Semantics(Role.Group, "Navigation");
        if (renderer.TakeSelection(bar.Id) is { } selected && items.Any(item => item.Id == selected))
            ui.Context.WithSource(bar.Id, () => ui.Dispatch(onSelect(selected)));
    }

    /// <summary>A native dial with 60 ticks, scalable countdown text and an optional progress arc.</summary>
    public static void NativeTimerDial(this Context context, string value, string status, string detail,
        float diameter = 264, float progress = 0, NativePalette? palette = null, NodeKey key = default)
    {
        RequireRenderer(context);
        if (!float.IsFinite(diameter) || diameter < 96 || !float.IsFinite(progress))
            throw new ArgumentOutOfRangeException(nameof(diameter));
        using var dial = context.Div(key).Width(diameter).Height(diameter)
            .Semantics(Role.Image, $"{status}: {value}, {detail}")
            .Native(new NativeDialContent(value, status, detail, Math.Clamp(progress, 0, 1), palette ?? NativePalette.FocusDark));
    }

    /// <summary>A clipped, normalized activity strip. Supply chronological intervals in [0, 1].</summary>
    public static void NativeTimeline(this Context context, ImmutableArray<NativeTimelineSegment> segments,
        float marker, NativePalette? palette = null, NodeKey key = default)
    {
        RequireRenderer(context);
        if (segments.IsDefault || !float.IsFinite(marker) || marker is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(marker));
        var end = 0f;
        foreach (var segment in segments)
        {
            if (segment is null || !float.IsFinite(segment.Start) || !float.IsFinite(segment.End) ||
                segment.Start < end || segment.End > 1 || segment.End <= segment.Start)
                throw new ArgumentException("Activity intervals must be ordered, nonoverlapping and within [0, 1].", nameof(segments));
            end = segment.End;
        }
        using var strip = context.Div(key).FillWidth().Height(20).Semantics(Role.Image, "Day activity")
            .Native(new NativeTimelineContent(segments, marker, palette ?? NativePalette.FocusDark));
    }

    /// <summary>Begins a system sheet with native detents, grabber, drag dismissal and caller-owned content.</summary>
    public static KitScope NativeBottomDrawer<TMessage>(this Ui<TMessage> ui, string title, TMessage onClose,
        float height = 380, NativePalette? palette = null, NodeKey key = default)
    {
        var c = ui.Context;
        var renderer = RequireRenderer(c);
        if (!float.IsFinite(height) || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height));
        var p = palette ?? NativePalette.FocusDark;
        var size = Math.Min(height, Math.Max(0, c.Size.Height - 24));
        var width = Math.Min(600, Math.Max(0, c.Size.Width - 16));
#pragma warning disable KUM001 // The returned KitScope owns these three scopes.
        var overlay = c.Overlay(key.Value is null ? "bottom-drawer" : key).TabStop(false)
            .Semantics(Role.Group, title).Native(new NativeBackdrop());
        ui.OnPress(overlay.Control(), onClose);
        var viewport = renderer.SheetSize(overlay.Id, new(width, size));
        width = Math.Min(viewport.Width, c.Size.Width);
        size = Math.Min(viewport.Height, Math.Max(0, c.Size.Height - 24));
        var panel = c.Div(key: "sheet").Absolute().Offset((c.Size.Width - width) / 2, c.Size.Height - size - 8)
            .Width(width).Height(size).Padding(20).Gap(12)
            .Semantics(Role.Dialog, title).Native(new NativeSheetContent(p, height));
        using (c.Div(key: "sheet-header").Row().FillWidth().ItemsCenter().Gap(8))
        {
            using (c.Div(key: "title").Flex())
                c.NativeLabel(title, new(22, Weight.Semibold, p.Text, Wrap: true), key: "title-text");
            ui.NativeAction("Close", onClose, new(Symbol: "xmark", Width: 44, Height: 44), p, key: "sheet-close");
        }
        var body = c.Div(key: "sheet-body").FillWidth().Grow().Gap(8);
#pragma warning restore KUM001
        return new(overlay, panel, body);
    }
}
