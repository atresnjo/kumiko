using System.Runtime.CompilerServices;
using Kumiko.UI;

namespace Kumiko.iOS;

internal sealed record NativePressableContent(string Label, bool Haptics, float PressedScale = 1, bool Timed = false,
    bool ReduceMotion = false, bool PressOnTouchDown = false);

public static partial class NativeComponents
{
    /// <summary>
    /// A composed button whose message receives the native action's monotonic timestamp in milliseconds.
    /// Optional scale feedback retargets from the presented transform and respects Reduce Motion.
    /// Press-on-touch-down activation suppresses a second action on finger release.
    /// </summary>
    public static Div NativePressable<TMessage>(this Ui<TMessage> ui, string label, Func<long, TMessage> onPress,
        float pressedScale = 1, bool enabled = true, bool haptics = false, bool pressOnTouchDown = false, NodeKey key = default,
        [CallerFilePath] string source = "", [CallerLineNumber] int line = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        if (!float.IsFinite(pressedScale) || pressedScale is < .5f or > 1)
            throw new ArgumentOutOfRangeException(nameof(pressedScale));
        var renderer = RequireRenderer(ui.Context);
        var content = new NativePressableContent(label, haptics, pressedScale, Timed: true,
            ReduceMotion: ui.Context.ReduceMotion, PressOnTouchDown: pressOnTouchDown);
        var surface = ui.Context.Div(key, source, line).MinWidth(44).MinHeight(44)
            .Semantics(Role.Button, label).Native(content);
        renderer.PrepareValue(surface.Id, content, !enabled);
        Change(ui, surface, onPress, !enabled);
        return surface;
    }

    /// <summary>
    /// Begins a message-bound UIButton container for native images, symbols and labels. Dispose the scope.
    /// Optional selection haptics run only in a visible host.
    /// </summary>
    public static Div NativePressable<TMessage>(this Ui<TMessage> ui, string label, TMessage onPress,
        bool enabled = true, bool haptics = false, NodeKey key = default,
        [CallerFilePath] string source = "", [CallerLineNumber] int line = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        RequireRenderer(ui.Context);
        var surface = ui.Context.Div(key, source, line).MinWidth(44).MinHeight(44)
            .Native(new NativePressableContent(label, haptics));
        ui.Button(surface, label, onPress, enabled);
        return surface;
    }
}
