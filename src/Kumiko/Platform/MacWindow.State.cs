using System.Runtime.InteropServices;
using O = Kumiko.Platform.ObjC;

namespace Kumiko.Platform;

internal sealed unsafe partial class MacWindow
{
    bool? fullScreenTarget, requestedFullScreen;

    void RequestFullScreen(bool desired)
    {
        requestedFullScreen = desired;
        if (fullScreenTarget is not null)
            return;
        if (IsFullScreen == desired)
        {
            requestedFullScreen = null;
            return;
        }

        if (desired)
            normalState = State;
        fullScreenTarget = desired;
        try
        {
            Presentation.ToggleFullScreen(Handle);
        }
        catch
        {
            fullScreenTarget = requestedFullScreen = null;
            throw;
        }
    }

    void FullScreenCompleted()
    {
        fullScreenTarget = null;
        window.NativeShellChanged();
        if (!disposed && requestedFullScreen is { } desired)
            RequestFullScreen(desired);
    }

    [UnmanagedCallersOnly]
    static void FullScreenWillChange(nint self, nint cmd, nint notification) => Guard(self, w =>
    {
        bool entering = cmd == O.Sel("windowWillEnterFullScreen:");
        w.fullScreenTarget = entering;
        w.requestedFullScreen ??= entering;
    });

    [UnmanagedCallersOnly]
    static void FullScreenFailed(nint self, nint cmd, nint sender) => Guard(self, w =>
    {
        w.fullScreenTarget = w.requestedFullScreen = null;
        w.window.NativeShellChanged();
    });

    void ApplyTrafficLights()
    {
        if (disposed || trafficLightPosition is not { } point)
            return;
        for (nuint i = 0; i < 3; i++)
        {
            var button = O.O(Handle, "standardWindowButton:", i);
            O.V(button, "setFrameOrigin:", new NativePoint(point.X + (double)i * 20, point.Y));
        }
    }
}
