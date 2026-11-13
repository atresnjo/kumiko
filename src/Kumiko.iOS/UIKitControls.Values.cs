using Kumiko.UI;
using UIKit;

namespace Kumiko.iOS;

internal sealed partial class UIKitControls
{
    internal readonly record struct PendingValue<T>(T Value);
    readonly Dictionary<ulong, Queue<object>> values = new();

    internal void PrepareValue(ulong id, object content, bool disabled)
    {
        if (disabled || current.TryGetValue(id, out var previous) && !SameKind(previous.Content, content))
        {
            values.Remove(id);
            navigationCommits.Remove(id);
        }
    }

    internal PendingValue<T>? TakeValue<T>(ulong id)
    {
        if (!values.TryGetValue(id, out var queue) || !queue.TryDequeue(out var value))
            return null;
        var committed = navigationCommits.Contains(id);
        if (queue.Count == 0)
        {
            values.Remove(id);
            navigationCommits.Remove(id);
        }
        else
            RequestFrame();
        return value is T typed && (Active(id) || committed && current.ContainsKey(id)) ? new(typed) : null;
    }

    bool Active(ulong id) => current.TryGetValue(id, out var node) && node.Enabled && !node.Inert;

    void QueueValue(ulong id, object value)
    {
        if (!Active(id))
            return;
        if (!values.TryGetValue(id, out var queue))
            values.Add(id, queue = new());
        queue.Enqueue(value);
        RequestFrame();
    }

    void PruneValues()
    {
        foreach (var id in values.Keys.Where(id => !Active(id) && !navigationCommits.Contains(id)).ToArray())
            values.Remove(id);
    }

    static bool SameKind(object before, object after) => before.GetType() == after.GetType() && (before, after) switch
    {
        (NativeChoiceContent left, NativeChoiceContent right) => left.Wheel == right.Wheel,
        (NativeCommandsContent left, NativeCommandsContent right) => left.Kind == right.Kind,
        _ => true
    };

    sealed class ValueOwner : IDisposable
    {
        readonly UIControl control;
        readonly UIAction action;
        bool stopped;

        internal ValueOwner(UIKitControls renderer, UIControl control, ulong id, Func<object?> read)
        {
            this.control = control;
            action = UIAction.Create(_ => renderer.Guard(() =>
            {
                if (!stopped && renderer.Active(id) && read() is { } value)
                    renderer.QueueValue(id, value);
            }));
            control.AddAction(action, UIControlEvent.ValueChanged);
        }

        public void Dispose()
        {
            stopped = true;
            control.RemoveAction(action, UIControlEvent.ValueChanged);
            action.Dispose();
        }
    }
}
