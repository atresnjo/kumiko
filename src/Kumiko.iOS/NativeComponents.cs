using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Kumiko.Components;
using Kumiko.UI;

namespace Kumiko.iOS;

/// <summary>A stable domain identity and the content of one native list row.</summary>
public sealed record NativeListItem(string Id, string Title, string? Detail = null, bool Enabled = true);

internal sealed record NativeListContent(string Label, ImmutableArray<NativeListItem> Items, string? SelectedId,
    ImmutableHashSet<string>? Checks = null, NativePalette? Palette = null);
internal sealed record NativeBackdrop;

/// <summary>Experimental message-bound UIKit components. These require a NativeView host.</summary>
public static partial class NativeComponents
{
    /// <summary>Adds a scrolling UITableView that fills the remaining column height. Selection sends a row ID.</summary>
    public static void NativeList<TMessage>(this Ui<TMessage> ui, string label,
        ImmutableArray<NativeListItem> items, string? selectedId, Func<string, TMessage> onSelect,
        NodeKey key = default, [CallerFilePath] string source = "", [CallerLineNumber] int line = 0)
    {
        var c = ui.Context;
        var renderer = RequireRenderer(c);
        ArgumentNullException.ThrowIfNull(onSelect);
        if (items.IsDefault)
            throw new ArgumentException("Supply an initialized immutable array.", nameof(items));
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            if (item is null || string.IsNullOrWhiteSpace(item.Id) || !ids.Add(item.Id))
                throw new ArgumentException("List rows require unique, nonempty domain IDs.", nameof(items));
        }
        using var list = c.Div(key, source, line).FillWidth().Grow().Semantics(Role.List, label)
            .Native(new NativeListContent(label, items, selectedId));
        if (renderer.TakeSelection(list.Id) is { } selected && items.Any(item => item.Id == selected && item.Enabled))
            c.WithSource(list.Id, () => ui.Dispatch(onSelect(selected)));
    }

    /// <summary>
    /// Begins a system sheet with native dragging, expansion, dismissal, a heading and Close button.
    /// Call only while open and dispose the scope after adding content. UIKit owns presented dimensions;
    /// width bounds the detached content preview.
    /// </summary>
    public static KitScope NativeDrawer<TMessage>(this Ui<TMessage> ui, string title, TMessage onClose,
        float width = 340, NodeKey key = default)
    {
        var c = ui.Context;
        var renderer = RequireRenderer(c);
        if (!float.IsFinite(width) || width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        var panelWidth = Math.Min(width, Math.Max(0, c.Size.Width - Space.S5));
        var height = Math.Min(480, Math.Max(0, c.Size.Height - 24));
#pragma warning disable KUM001 // KitScope owns the overlay, panel and body.
        var overlay = c.Overlay(key.Value is null ? "drawer" : key).TabStop(false)
            .Semantics(Role.Group, title).Native(new NativeBackdrop());
        ui.OnPress(overlay.Control(), onClose);
        var viewport = renderer.SheetSize(overlay.Id, new(panelWidth, height));
        panelWidth = Math.Min(viewport.Width, c.Size.Width);
        height = Math.Min(viewport.Height, Math.Max(0, c.Size.Height - 24));
        var panel = c.Div(key: "panel").Absolute().Offset((c.Size.Width - panelWidth) / 2, c.Size.Height - height)
            .Width(panelWidth).Height(height).Padding(Space.S4).Gap(Space.S4)
            .Semantics(Role.Dialog, title).Native(new NativeSheetContent(null, 480));
        using (c.Div(key: "header").Row().FillWidth().ItemsCenter().Gap(Space.S2))
        {
            using (c.Div(key: "title").Flex())
                c.Heading(title, size: Typography.Title.Size, key: "heading");
            ui.Button("Close", onClose, key: "close");
        }
        var body = c.Div(key: "body").FillWidth().Grow().Gap(Space.S3);
#pragma warning restore KUM001
        return new(overlay, panel, body);
    }

    static UIKitControls RequireRenderer(Context context) => context.ControlRenderer as UIKitControls ??
        throw new InvalidOperationException("Native components require a Kumiko.iOS.NativeView host.");
}
