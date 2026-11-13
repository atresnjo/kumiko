using System.Collections.Immutable;
using Kumiko.UI;

namespace Kumiko.iOS;

/// <summary>The UIKit role of an alert or confirmation dialog action.</summary>
public enum NativeAlertActionStyle
{
    Default,
    Cancel,
    Destructive
}

/// <summary>A stable alert or confirmation dialog action. At most one action may use Cancel.</summary>
public sealed record NativeAlertAction(string Id, string Label,
    NativeAlertActionStyle Style = NativeAlertActionStyle.Default, bool Enabled = true);

/// <summary>A model-bound alert text field with a stable ID.</summary>
public sealed record NativeAlertField(string Id, string Value, string? Placeholder = null, bool Secure = false);

internal sealed record NativeAlertContent(string? Title, string? Message, ImmutableArray<NativeAlertAction> Actions,
    ImmutableArray<NativeAlertField> Fields, bool ActionSheet, ulong Source);

internal abstract record NativeAlertEvent
{
    internal sealed record Chosen(string Id) : NativeAlertEvent;
    internal sealed record Edited(string Field, string Value) : NativeAlertEvent;
    internal sealed record Dismissed : NativeAlertEvent;
}

public static partial class NativeComponents
{
    /// <summary>
    /// Presents a system alert while declared. A non-cancel action sends onAction(id); the Cancel action
    /// or any dismissal without an action sends onDismiss. Each presentation sends exactly one of these:
    /// clear the declaration in its update. Field edits send onFieldChange(fieldId, text) in order, before
    /// the action that follows them. Declare it after any sheet it covers; one alert or dialog at a time.
    /// </summary>
    public static void NativeAlert<TMessage>(this Ui<TMessage> ui, string title, string? message,
        ImmutableArray<NativeAlertAction> actions, Func<string, TMessage> onAction, TMessage onDismiss,
        ImmutableArray<NativeAlertField> fields = default, Func<string, string, TMessage>? onFieldChange = null,
        NodeKey key = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        fields = fields.IsDefault ? [] : fields;
        if (fields.Any(field => field is null || string.IsNullOrWhiteSpace(field.Id) || field.Value is null) ||
            fields.Select(field => field.Id).Distinct(StringComparer.Ordinal).Count() != fields.Length)
            throw new ArgumentException("Alert fields require unique, nonempty IDs and values.", nameof(fields));
        if (!fields.IsEmpty && onFieldChange is null)
            throw new ArgumentNullException(nameof(onFieldChange), "Alert fields require a change message.");
        Alert(ui, new(title, message, actions, fields, false, 0), title, onAction, onDismiss, onFieldChange,
            key.Value is null ? "alert" : key);
    }

    /// <summary>
    /// Presents a confirmation dialog (UIKit action sheet) while declared, with alert message semantics.
    /// Supply exactly one Cancel action: a regular-width popover hides it and UIKit invokes it for a tap
    /// outside. <paramref name="source"/> is the key of a sibling control in the current scope; the popover
    /// anchors to its native view, or to the host's center when that view is absent.
    /// </summary>
    public static void NativeConfirmationDialog<TMessage>(this Ui<TMessage> ui, string? title, string? message,
        ImmutableArray<NativeAlertAction> actions, NodeKey source, Func<string, TMessage> onAction, TMessage onDismiss,
        NodeKey key = default)
    {
        if (source.Value is null)
            throw new ArgumentException("Supply the key of a sibling source control.", nameof(source));
        if (actions.IsDefault || actions.Count(action => action?.Style == NativeAlertActionStyle.Cancel) != 1)
            throw new ArgumentException("A confirmation dialog requires exactly one Cancel action.", nameof(actions));
        var anchor = ui.Context.NextNodeId(source);
        Alert(ui, new(title, message, actions, [], true, anchor), title ?? message ?? "Confirmation",
            onAction, onDismiss, null, key.Value is null ? "confirmation" : key);
    }

    static void Alert<TMessage>(Ui<TMessage> ui, NativeAlertContent content, string name,
        Func<string, TMessage> onAction, TMessage onDismiss, Func<string, string, TMessage>? onFieldChange, NodeKey key)
    {
        var c = ui.Context;
        var renderer = RequireRenderer(c);
        ArgumentNullException.ThrowIfNull(onAction);
        var actions = content.Actions;
        if (actions.IsDefaultOrEmpty ||
            actions.Any(action => action is null || string.IsNullOrWhiteSpace(action.Id) ||
                string.IsNullOrWhiteSpace(action.Label) || !Enum.IsDefined(action.Style)) ||
            actions.Select(action => action.Id).Distinct(StringComparer.Ordinal).Count() != actions.Length ||
            actions.Count(action => action.Style == NativeAlertActionStyle.Cancel) > 1)
            throw new ArgumentException("Supply unique nonempty action IDs and at most one Cancel action.", nameof(content));
        using var overlay = c.Overlay(key).TabStop(false).Semantics(Role.Dialog, name).Native(content);
        renderer.PrepareValue(overlay.Id, content, false);
        if (renderer.TakeValue<NativeAlertEvent>(overlay.Id) is not { } pending)
            return;
        switch (pending.Value)
        {
            case NativeAlertEvent.Edited edited when onFieldChange is not null && content.Fields.Any(field => field.Id == edited.Field):
                c.WithSource(overlay.Id, () => ui.Dispatch(onFieldChange(edited.Field, edited.Value)));
                break;
            case NativeAlertEvent.Chosen chosen when actions.Any(action => action.Id == chosen.Id && action.Enabled &&
                action.Style != NativeAlertActionStyle.Cancel):
                c.WithSource(overlay.Id, () => ui.Dispatch(onAction(chosen.Id)));
                break;
            case NativeAlertEvent.Chosen or NativeAlertEvent.Dismissed:
                // UIKit has already dismissed the controller; keep the model in sync.
                c.WithSource(overlay.Id, () => ui.Dispatch(onDismiss));
                break;
        }
    }

    /// <summary>
    /// Begins a native container with a UIContextMenuInteraction. Long press or a secondary click opens
    /// the system menu; a command sends its stable ID. Add content inside and dispose the returned scope.
    /// Style nested content rather than this container. A disabled menu does not open.
    /// </summary>
    public static Div NativeContextMenu<TMessage>(this Ui<TMessage> ui, string label, ImmutableArray<NativeCommand> items,
        Func<string, TMessage> onSelect, string? title = null, bool disabled = false, NodeKey key = default)
    {
        var c = ui.Context;
        var renderer = RequireRenderer(c);
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentNullException.ThrowIfNull(onSelect);
        if (items.IsDefaultOrEmpty || items.Any(item => item is null || string.IsNullOrWhiteSpace(item.Id) || string.IsNullOrWhiteSpace(item.Label)) ||
            items.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != items.Length)
            throw new ArgumentException("Supply unique nonempty command IDs.", nameof(items));
        var content = new NativeContextMenuContent(label, title, items, disabled);
        var scope = c.Div(key).Semantics(Role.Group, label).Native(content);
        renderer.PrepareValue(scope.Id, content, disabled);
        if (renderer.TakeValue<string>(scope.Id) is { } chosen && !disabled &&
            items.Any(item => item.Id == chosen.Value && item.Enabled))
            c.WithSource(scope.Id, () => ui.Dispatch(onSelect(chosen.Value)));
        return scope;
    }
}

internal sealed record NativeContextMenuContent(string Label, string? Title, ImmutableArray<NativeCommand> Items, bool Disabled);
