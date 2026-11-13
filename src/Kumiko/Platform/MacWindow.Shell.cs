using Kumiko.UI;
using O = Kumiko.Platform.ObjC;

namespace Kumiko.Platform;

internal sealed unsafe partial class MacWindow
{
    bool childAttached;
    Point? trafficLightPosition;
    public void Show()
    {
        if (disposed)
            return;
        using var operation = NativeOperation();
        renderer?.Redraw();
        var parent = window.Options.Parent;
        if (!window.Options.Modal && parent is not null && !childAttached)
        {
            childAttached = true;
            Presentation.Attach(parent.NativeHandle, Handle);
        }

        if (!disposed)
            Presentation.Show(Handle, parent?.NativeHandle ?? 0, window.Options.Modal);
    }

    public void Hide()
    {
        if (disposed)
            return;
        using var operation = NativeOperation();
        DetachChild();
        if (!disposed)
            Presentation.Hide(Handle);
    }

    void DetachChild()
    {
        if (!childAttached || window.Options.Parent is not { } parent)
            return;
        childAttached = false;
        Presentation.Detach(parent.NativeHandle, Handle);
    }

    public void SetTitle(string title)
    {
        if (disposed)
            return;
        using var operation = NativeOperation();
        using var pool = new AutoreleasePool();
        O.V(Handle, "setTitle:", O.TempString(title));
        if (!disposed)
            ApplyTrafficLights();
    }

    public void Resize(float width, float height)
    {
        if (disposed)
            return;
        using var operation = NativeOperation();
        O.V(Handle, "setContentSize:", new NativeSize(width, height));
        DidResize();
    }

    /// <summary>Height of the primary display, which anchors AppKit's bottom-left screen coordinates.</summary>
    static double PrimaryTop() => O.R(O.O(O.O(O.Class("NSScreen"), "screens"), "objectAtIndex:", (nuint)0), "frame").Size.Height;
    public WindowState? State
    {
        get
        {
            using var pool = new AutoreleasePool();
            if ((IsMaximized || IsFullScreen) && normalState is { } normal)
                return normal with
                {
                    Maximized = IsMaximized,
                    FullScreen = IsFullScreen
                };
            var frame = O.R(Handle, "frame");
            var content = O.R(Handle, "contentRectForFrameRect:", frame);
            return new((float)frame.Origin.X, (float)(PrimaryTop() - frame.Origin.Y - frame.Size.Height), (float)content.Size.Width, (float)content.Size.Height, IsMaximized, IsFullScreen);
        }
    }

    public void Restore(WindowState state)
    {
        if (disposed)
            return;
        using var operation = NativeOperation();
        using var pool = new AutoreleasePool();
        var size = O.R(Handle, "frameRectForContentRect:", new NativeRect(0, 0, state.Width, state.Height)).Size;
        var frame = new NativeRect(state.X, PrimaryTop() - state.Y - size.Height, size.Width, size.Height);
        O.V(Handle, "setFrame:display:", frame, IsVisible ? (byte)1 : (byte)0);
        DidResize();
    }

    public void Minimize()
    {
        if (disposed)
            return;
        using var operation = NativeOperation();
        O.V(Handle, "miniaturize:", (nint)0);
    }

    bool zooming;
    public bool BeginMove()
    {
        if (disposed)
            return false;
        using var pool = new AutoreleasePool();
        // Web drag regions arrive after the mousedown; AppKit still holds that press as the current event.
        nint e = O.O(O.O(O.Class("NSApplication"), "sharedApplication"), "currentEvent");
        if (e == 0 || (O.N(O.Class("NSEvent"), "pressedMouseButtons") & 1) == 0 || O.N(e, "type") is not (1 or 6))
            return false;
        O.V(Handle, "performWindowDragWithEvent:", e);
        return true;
    }

    public void ToggleMaximize()
    {
        if (disposed || zooming)
            return;
        using var operation = NativeOperation();
        zooming = true;
        try
        {
            Presentation.ToggleMaximize(Handle);
        }
        finally
        {
            zooming = false;
        }
    }
    WindowState? normalState;
    nint materialView;
    MacWindowProgress? progress;
    nint attention;
    public nint NativeHandle => Handle;
    public bool IsFocused => !disposed && O.B(Handle, "isKeyWindow") != 0;
    public bool IsMaximized => !disposed && Presentation.IsMaximized(Handle);
    public bool IsMinimized => !disposed && O.B(Handle, "isMiniaturized") != 0;
    public bool IsFullScreen => !disposed && Presentation.IsFullScreen(Handle);

    public Rect Bounds
    {
        get
        {
            var r = O.R(Handle, "frame");
            return new((float)r.Origin.X, (float)(PrimaryTop() - r.Origin.Y - r.Size.Height), (float)r.Size.Width, (float)r.Size.Height);
        }
    }

    public Size ContentSize
    {
        get
        {
            var r = O.R(Handle, "contentRectForFrameRect:", O.R(Handle, "frame"));
            return new((float)r.Size.Width, (float)r.Size.Height);
        }
    }

    public void SetBounds(Rect bounds)
    {
        if (disposed)
            return;
        using var operation = NativeOperation();
        O.V(Handle, "setFrame:display:", new NativeRect(bounds.X, PrimaryTop() - bounds.Y - bounds.Height, bounds.Width, bounds.Height), (byte)0);
        DidResize();
    }

    public void Configure(WindowOptions options)
    {
        if (disposed)
            return;
        using var operation = NativeOperation();
        using var pool = new AutoreleasePool();
        O.V(Handle, "setContentMinSize:", new NativeSize(options.MinWidth, options.MinHeight));
        if (disposed)
            return;
        O.V(Handle, "setContentMaxSize:", new NativeSize(options.MaxWidth > 0 ? options.MaxWidth : double.MaxValue, options.MaxHeight > 0 ? options.MaxHeight : double.MaxValue));
        if (disposed)
            return;
        O.V(Handle, "setAlphaValue:", (double)options.Opacity);
        if (renderer is not null)
            O.V(renderer.Layer, "setOpaque:", (byte)(options.Transparent || options.Material != WindowMaterial.None ? 0 : 1));
        O.V(Handle, "setOpaque:", (byte)(options.Transparent || options.Material != WindowMaterial.None ? 0 : 1));
        if (options.Transparent || options.Material != WindowMaterial.None)
            O.V(Handle, "setBackgroundColor:", O.O(O.Class("NSColor"), "clearColor"));
        O.V(Handle, "setHasShadow:", (byte)(options.HasShadow ? 1 : 0));
        O.V(Handle, "setMovable:", (byte)(options.Movable ? 1 : 0));
        O.V(Handle, "setIgnoresMouseEvents:", (byte)(options.IgnoreMouseEvents ? 1 : 0));
        O.V(Handle, "setSharingType:", (nuint)(options.ContentProtection ? 0 : 1));
        O.V(Handle, "setCollectionBehavior:", (nuint)(options.Panel ? 1 | 256 | 64 | (OperatingSystem.IsMacOSVersionAtLeast(13) ? 1 << 18 : 0) : options.VisibleOnAllWorkspaces ? 1 : 0));
        if (options.Panel)
        {
            O.V(Handle, "setFloatingPanel:", (byte)1);
            O.V(Handle, "setBecomesKeyOnlyIfNeeded:", (byte)0);
            O.V(Handle, "setHidesOnDeactivate:", (byte)0);
            O.V(Handle, "setLevel:", (nuint)3);
        }
        trafficLightPosition = options.TrafficLightPosition;
        for (nuint i = 0; i < 3; i++)
        {
            var button = O.O(Handle, "standardWindowButton:", i);
            bool enabled = i == 0 ? options.Closable : i == 1 ? options.Minimizable : options.Maximizable;
            O.V(button, "setEnabled:", (byte)(enabled ? 1 : 0));
        }

        ApplyTrafficLights();

        if (options.Material != WindowMaterial.None && materialView == 0)
        {
            materialView = O.O(O.O(O.Class("NSVisualEffectView"), "alloc"), "initWithFrame:", O.R(view, "bounds"));
            O.V(materialView, "setAutoresizingMask:", (nuint)18);
            O.V(materialView, "setBlendingMode:", (nuint)0);
            O.V(materialView, "setMaterial:", (nuint)(options.Material == WindowMaterial.Sidebar ? 7 : 21));
            var responder = O.O(O.O(Handle, "firstResponder"), "retain");
            try
            {
                O.V(view, "removeFromSuperview");
                O.V(Handle, "setContentView:", materialView);
                O.V(view, "setAutoresizingMask:", (nuint)18);
                O.V(materialView, "addSubview:", view);
                O.V(Handle, "makeFirstResponder:", responder != 0 ? responder : view);
            }
            finally
            {
                O.Release(responder);
            }
        }
    }

    public void Perform(WindowAction action)
    {
        if (disposed)
            return;
        using var operation = NativeOperation();
        switch (action)
        {
            case WindowAction.Focus:
                O.V(Handle, "makeKeyWindow");
                break;
            case WindowAction.Blur:
                throw new PlatformNotSupportedException("macOS cannot blur a window without choosing another focus target. Use Focus on the intended window or Hide.");
            case WindowAction.Maximize:
                if (!IsMaximized)
                {
                    normalState = State;
                    ToggleMaximize();
                }

                break;
            case WindowAction.Unmaximize:
                if (IsMaximized)
                    ToggleMaximize();
                break;
            case WindowAction.Restore:
                if (IsMinimized)
                    O.V(Handle, "deminiaturize:", (nint)0);
                if (IsMaximized)
                    ToggleMaximize();
                break;
            case WindowAction.EnterFullScreen:
                RequestFullScreen(true);
                break;
            case WindowAction.LeaveFullScreen:
                RequestFullScreen(false);
                break;
        }
    }

    public void Progress(double? value)
    {
        if (disposed)
            return;
        if (value is null && progress is null)
            return;
        using var pool = new AutoreleasePool();
        if (progress is null)
        {
            var app = O.O(O.Class("NSApplication"), "sharedApplication");
            progress = new(O.O(app, "dockTile"), O.O(app, "applicationIconImage"));
        }

        progress.Set(value);
    }

    public void Attention(bool enabled)
    {
        var app = O.O(O.Class("NSApplication"), "sharedApplication");
        if (attention != 0)
            O.V(app, "cancelUserAttentionRequest:", attention);
        attention = enabled ? O.O(app, "requestUserAttention:", (nuint)10) : 0;
    }

    [System.Runtime.InteropServices.UnmanagedCallersOnly]
    static void ShellNotification(nint self, nint cmd, nint notification) => Guard(self, w =>
    {
        if (cmd == O.Sel("windowDidEnterFullScreen:") || cmd == O.Sel("windowDidExitFullScreen:"))
            w.FullScreenCompleted();
        else
            w.window.NativeShellChanged();
    });
    void DisposeShell()
    {
        try
        {
            dragSource?.Dispose();
        }
        finally
        {
            Attention(false);
            progress?.Dispose();
            progress = null;
            DetachChild();
            if (window.Options.Parent is { } parent && window.Options.Modal && O.O(Handle, "sheetParent") != 0)
                O.V(parent.NativeHandle, "endSheet:", Handle);
            O.Release(materialView);
        }
    }
}
