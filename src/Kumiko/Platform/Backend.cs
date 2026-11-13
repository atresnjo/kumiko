using Kumiko.Rendering;
using Kumiko.UI;
using Kumiko.Web;

namespace Kumiko.Platform;

internal interface IAppBackend : IDisposable
{
    IWindowBackend CreateWindow(Window window, PageOptions? page);
    void Dispatch(Action action);
    void Run();
    void RunFor(TimeSpan duration);
    void Quit();
    void Activate();
}

internal interface IWindowBackend : IDisposable
{
    string Renderer => OperatingSystem.IsMacOS() ? "Metal" : "Software";

    bool IsVisible
    {
        get;
    }

    nint NativeHandle
    {
        get;
    }

    UI.Rect Bounds
    {
        get;
    }

    UI.Size ContentSize
    {
        get;
    }

    bool IsFocused
    {
        get;
    }

    bool IsMaximized
    {
        get;
    }

    bool IsMinimized
    {
        get;
    }

    bool IsFullScreen
    {
        get;
    }

    void SetBounds(UI.Rect bounds);
    void Configure(WindowOptions options);
    void Perform(WindowAction action);
    void Progress(double? value);
    void Attention(bool enabled);
    void StartDrag(Desktop.TransferData data, UI.Rect bounds);
    void Show();
    void Hide();
    void SetTitle(string title);
    void Resize(float width, float height);
    void Minimize();
    void ToggleMaximize();
    /// <summary>Starts a native move loop when the primary pointer button is physically held; otherwise returns false.</summary>
    bool BeginMove();
    void Present(Context context);
    /// <summary>
    /// Asks for <see cref="Window.DisplayFrame"/> at the display's next refresh. False when the backend has no
    /// display-synchronized frames for this window (hidden, occluded or unsupported); callers then draw themselves.
    /// </summary>
    bool RequestFrame() => false;
    Pixels Capture(Context context);
    /// <summary>The window's normal placement, or null when the platform cannot report it.</summary>
    WindowState? State => null;

    /// <summary>Applies a placement already fitted to the connected screens.</summary>
    void Restore(WindowState state)
    {
    }
}

internal enum WindowAction
{
    Focus,
    Blur,
    Maximize,
    Unmaximize,
    Restore,
    EnterFullScreen,
    LeaveFullScreen
}
