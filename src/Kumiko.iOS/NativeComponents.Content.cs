using System.Collections.Immutable;
using Kumiko.UI;

namespace Kumiko.iOS;

public static partial class NativeComponents
{
    /// <summary>A UIProgressView with progress in [0, 1]. Use NativeActivityIndicator for indeterminate work.</summary>
    public static void NativeProgress(this Context context, string label, float value, NodeKey key = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        if (!float.IsFinite(value) || value is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(value));
        RequireRenderer(context);
        using var progress = context.Div(key).FillWidth().Height(8).Semantics(Role.Group, label)
            .Native(new NativeProgressContent(label, value));
    }

    /// <summary>A UIActivityIndicatorView. UIKit owns its animation and stops it when running is false.</summary>
    public static void NativeActivityIndicator(this Context context, string label, bool running = true, NodeKey key = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        RequireRenderer(context);
        using var activity = context.Div(key).Width(44).Height(44).Semantics(Role.Group, label)
            .Native(new NativeActivityContent(label, running));
    }

    /// <summary>A UIPageControl. Pages are zero-based; the count must be positive.</summary>
    public static void NativePageControl<TMessage>(this Ui<TMessage> ui, string label, int count, int page,
        Func<int, TMessage> onChange, bool disabled = false, NodeKey key = default)
    {
        if (count <= 0 || page < 0 || page >= count)
            throw new ArgumentOutOfRangeException(nameof(page));
        using var control = Input(ui, new NativePageContent(label, count, page), label, Role.Group, 44, disabled, key);
        Change(ui, control, onChange, disabled, next => next >= 0 && next < count);
    }

    /// <summary>A UIImageView displaying an SF Symbol. A null accessible name marks it decorative.</summary>
    public static void NativeSymbol(this Context context, string symbol, float size = 28,
        string? accessibleName = null, NodeKey key = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        Image(context, new(accessibleName, symbol, default), size, size, key);
    }

    /// <summary>A UIImageView displaying an SF Symbol with an explicit tint.</summary>
    public static void NativeSymbol(this Context context, string symbol, Color tint, float size = 28,
        string? accessibleName = null, NodeKey key = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        Image(context, new(accessibleName, symbol, default, tint), size, size, key);
    }

    /// <summary>A UIImageView decoding immutable image bytes with aspect-fit scaling. Keep bytes in the model.</summary>
    public static void NativeImage(this Context context, ImmutableArray<byte> data, float width, float height,
        string? accessibleName = null, NodeKey key = default)
    {
        if (data.IsDefaultOrEmpty)
            throw new ArgumentException("Supply encoded image bytes.", nameof(data));
        Image(context, new(accessibleName, null, data), width, height, key);
    }

    static void Image(Context context, NativeImageContent content, float width, float height, NodeKey key)
    {
        RequireRenderer(context);
        if (!float.IsFinite(width) || !float.IsFinite(height) || width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        using var image = context.Div(key).Width(width).Height(height).Semantics(Role.Image, content.Label).Native(content);
    }

    /// <summary>A semantic UIKit separator using the system separator color.</summary>
    public static void NativeSeparator(this Context context, NodeKey key = default)
    {
        RequireRenderer(context);
        using var separator = context.Div(key).FillWidth().Height(1).Native(new NativeSeparatorContent());
    }

    /// <summary>A UIButton with a system UIMenu. Commands dispatch stable IDs and can be disabled or destructive.</summary>
    public static void NativeMenu<TMessage>(this Ui<TMessage> ui, string label, ImmutableArray<NativeCommand> items,
        Func<string, TMessage> onSelect, bool disabled = false, NodeKey key = default) =>
        Commands(ui, label, items, onSelect, NativeCommandKind.Menu, disabled, key);

    /// <summary>A UINavigationBar whose leading/trailing commands send IDs. Navigation state belongs to the model.</summary>
    public static void NativeNavigationBar<TMessage>(this Ui<TMessage> ui, string title, ImmutableArray<NativeCommand> items,
        Func<string, TMessage> onSelect, bool disabled = false, NodeKey key = default) =>
        Commands(ui, title, items, onSelect, NativeCommandKind.Navigation, disabled, key);

    /// <summary>A UIToolbar with evenly spaced, message-bound native commands.</summary>
    public static void NativeToolbar<TMessage>(this Ui<TMessage> ui, string label, ImmutableArray<NativeCommand> items,
        Func<string, TMessage> onSelect, bool disabled = false, NodeKey key = default) =>
        Commands(ui, label, items, onSelect, NativeCommandKind.Toolbar, disabled, key);

    static void Commands<TMessage>(Ui<TMessage> ui, string label, ImmutableArray<NativeCommand> items,
        Func<string, TMessage> onSelect, NativeCommandKind kind, bool disabled, NodeKey key)
    {
        if (items.IsDefault || items.Any(item => item is null || string.IsNullOrWhiteSpace(item.Id) || string.IsNullOrWhiteSpace(item.Label)) ||
            items.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != items.Length ||
            kind == NativeCommandKind.Menu && items.IsEmpty)
            throw new ArgumentException("Supply unique nonempty command IDs.", nameof(items));
        if (kind == NativeCommandKind.Navigation)
            for (var i = 0; i < ui.Context.Count; i++)
                if (ui.Context.Tree[i].NativeContent is NativeNavigationContent)
                    throw new InvalidOperationException("A stack page already owns its navigation bar.");
        using var control = Input(ui, new NativeCommandsContent(label, items, kind), label,
            kind == NativeCommandKind.Menu ? Role.Button : Role.Group, 44, disabled, key);
        Change(ui, control, onSelect, disabled, id => items.Any(item => item.Id == id && item.Enabled));
    }
}
