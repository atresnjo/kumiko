using Kumiko.Platform;
using Kumiko.UI;

namespace Kumiko;

public sealed partial class Window
{
    bool shellInitialized, ready;
    int shellRevision;
    bool? deferredFullScreen, deferredMaximized;
    (bool Focused, bool Visible, bool Maximized, bool Minimized, bool FullScreen, Rect Bounds) lastShell;
    public Desktop.Menu? Menu
    {
        get; private set;
    }

    public void SetMenu(Desktop.Menu menu)
    {
        CheckShell();
        if (OperatingSystem.IsMacOS())
            throw new PlatformNotSupportedException("macOS menu bars belong to the App. Use App.SetMenu.");
        (Menu ?? App.Menu)?.Detach(this);
        Menu = menu;
        menu.Attach(this);
    }

    public event Action<CancelableEvent>? Closing;
    public event Action? Focused;
    public event Action? Blurred;
    public event Action? Shown;
    public event Action? Hidden;
    public event Action? Resized;
    public event Action? Moved;
    public event Action? Maximized;
    public event Action? Unmaximized;
    public event Action? Minimized;
    public event Action? Restored;
    public event Action? EnteredFullScreen;
    public event Action? LeftFullScreen;
    public event Action? ReadyToShow;
    public bool IsFocused
    {
        get
        {
            CheckShell();
            return backend.IsFocused;
        }
    }

    public bool IsMaximized
    {
        get
        {
            CheckShell();
            return backend.IsMaximized;
        }
    }

    public bool IsMinimized
    {
        get
        {
            CheckShell();
            return backend.IsMinimized;
        }
    }

    public bool IsFullScreen
    {
        get
        {
            CheckShell();
            return backend.IsFullScreen;
        }
    }

    /// <summary>Outer frame in screen coordinates.</summary>
    public Rect Bounds
    {
        get
        {
            CheckShell();
            return backend.Bounds;
        }
    }

    public Point Position => new(Bounds.X, Bounds.Y);
    public Size FrameSize => new(Bounds.Width, Bounds.Height);

    public Size ContentSize
    {
        get
        {
            CheckShell();
            return backend.ContentSize;
        }
    }

    public float Opacity => Options.Opacity;
    internal nint NativeHandle => backend.NativeHandle;

    void CheckShell()
    {
        App.CheckThread();
        ObjectDisposedException.ThrowIf(disposed, this);
    }

    internal static void ValidateOptions(App app, WindowOptions o)
    {
        static bool Positive(float value) => float.IsFinite(value) && value > 0;
        if (!Positive(o.Width) || !Positive(o.Height) || !float.IsFinite(o.MinWidth) || !float.IsFinite(o.MinHeight) || !float.IsFinite(o.MaxWidth) || !float.IsFinite(o.MaxHeight) || o.MinWidth < 0 || o.MinHeight < 0 || o.MaxWidth < 0 || o.MaxHeight < 0 || (o.MaxWidth > 0 && o.MaxWidth < o.MinWidth) || (o.MaxHeight > 0 && o.MaxHeight < o.MinHeight) || !float.IsFinite(o.Opacity) || o.Opacity is < 0 or > 1 || (o.X is { } x && !float.IsFinite(x)) || (o.Y is { } y && !float.IsFinite(y)))
            throw new ArgumentOutOfRangeException(nameof(o), "Window geometry, limits and opacity must be finite and consistent.");
        if (o.Parent is { } parent && (parent.App != app || parent.disposed))
            throw new ArgumentException("Parent must be a live window in the same App.", nameof(o));
        if (o.Modal && o.Parent is null)
            throw new ArgumentException("Modal windows require a parent.", nameof(o));
        if (o.TrafficLightPosition is { } point && (!float.IsFinite(point.X) || !float.IsFinite(point.Y)))
            throw new ArgumentOutOfRangeException(nameof(o), "Traffic light coordinates must be finite.");
        if (app.Options.ActivationPolicy == ActivationPolicy.Background && o.Visible)
            throw new InvalidOperationException("Background applications require hidden windows.");
        Platform.WindowSupport.Validate(o);
    }

    void InitializeShell()
    {
        shellInitialized = true;
        lastShell = (backend.IsFocused, backend.IsVisible, backend.IsMaximized, backend.IsMinimized, backend.IsFullScreen, backend.Bounds);
        if (Options.Maximized)
            deferredMaximized = true;
        if (Options.FullScreen)
            deferredFullScreen = true;
        ApplyDeferredState();
    }

    void ApplyDeferredState()
    {
        if (disposed || !IsVisible || App.Options.Hidden)
            return;
        var maximized = deferredMaximized;
        var fullScreen = deferredFullScreen;
        deferredMaximized = deferredFullScreen = null;
        if (maximized is { } zoomed)
            backend.Perform(zoomed ? WindowAction.Maximize : WindowAction.Unmaximize);
        if (!disposed && fullScreen is { } expanded)
            backend.Perform(expanded ? WindowAction.EnterFullScreen : WindowAction.LeaveFullScreen);
    }

    void RaiseShell(Action? handlers, int? revision = null)
    {
        if (handlers is null)
            return;
        foreach (Action handler in handlers.GetInvocationList())
        {
            if (disposed || revision is { } expected && expected != shellRevision)
                return;
            handler();
        }
    }

    internal void NativeShellChanged()
    {
        if (!shellInitialized || disposed)
            return;
        int revision = ++shellRevision;
        var previous = lastShell;
        var current = (Focused: backend.IsFocused, Visible: backend.IsVisible, Maximized: backend.IsMaximized, Minimized: backend.IsMinimized, FullScreen: backend.IsFullScreen, Bounds: backend.Bounds);
        lastShell = current;
        if (previous.Focused != current.Focused)
            RaiseShell((current.Focused ? Focused : Blurred), revision);
        if (previous.Visible != current.Visible)
            RaiseShell((current.Visible ? Shown : Hidden), revision);
        if (previous.Maximized != current.Maximized)
            RaiseShell((current.Maximized ? Maximized : Unmaximized), revision);
        if (previous.Minimized != current.Minimized)
            RaiseShell((current.Minimized ? Minimized : Restored), revision);
        if (previous.FullScreen != current.FullScreen)
            RaiseShell((current.FullScreen ? EnteredFullScreen : LeftFullScreen), revision);
        if (previous.Bounds.X != current.Bounds.X || previous.Bounds.Y != current.Bounds.Y)
            RaiseShell(Moved, revision);
        if (previous.Bounds.Width != current.Bounds.Width || previous.Bounds.Height != current.Bounds.Height)
            RaiseShell(Resized, revision);
    }

    void PublishReady()
    {
        if (ready)
            return;
        ready = true;
        App.Dispatch(() =>
        {
            if (!disposed)
                RaiseShell(ReadyToShow);
        });
    }

    internal void NativePageReady() => PublishReady();

    void Perform(WindowAction action)
    {
        CheckShell();
        if ((App.Options.Hidden || App.Options.ActivationPolicy == ActivationPolicy.Background) && action is WindowAction.Focus or WindowAction.EnterFullScreen or WindowAction.Maximize or WindowAction.Restore)
            throw new InvalidOperationException("This operation can show or activate a window in a hidden App.");
        backend.Perform(action);
        NativeShellChanged();
    }

    public void Focus() => Perform(WindowAction.Focus);
    public void Blur() => Perform(WindowAction.Blur);
    public void Maximize() => Perform(WindowAction.Maximize);
    public void Unmaximize() => Perform(WindowAction.Unmaximize);
    public void Restore() => Perform(WindowAction.Restore);
    public void EnterFullScreen() => Perform(WindowAction.EnterFullScreen);
    public void LeaveFullScreen() => Perform(WindowAction.LeaveFullScreen);
    public void ToggleFullScreen()
    {
        if (IsFullScreen)
            LeaveFullScreen();
        else
            EnterFullScreen();
    }

    public void SetBounds(Rect bounds)
    {
        CheckShell();
        if (!float.IsFinite(bounds.X) || !float.IsFinite(bounds.Y) || !float.IsFinite(bounds.Width) || !float.IsFinite(bounds.Height) || bounds.Width <= 0 || bounds.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(bounds));
        backend.SetBounds(bounds);
        NativeShellChanged();
    }

    public void SetPosition(Point point) => SetBounds(Bounds with { X = point.X, Y = point.Y });
    public void Center()
    {
        CheckShell();
        var bounds = Bounds;
        var screen = Desktop.Screens.All.MaxBy(s => Math.Max(0, Math.Min(s.Bounds.Right, bounds.Right) - Math.Max(s.Bounds.X, bounds.X)) * Math.Max(0, Math.Min(s.Bounds.Bottom, bounds.Bottom) - Math.Max(s.Bounds.Y, bounds.Y)));
        if (screen is null)
            throw new InvalidOperationException("No connected screen is available.");
        var area = screen.WorkArea;
        SetPosition(new(area.X + (area.Width - bounds.Width) / 2, area.Y + (area.Height - bounds.Height) / 2));
    }

    void Configure(WindowOptions options)
    {
        CheckShell();
        ValidateOptions(App, options);
        backend.Configure(options);
        if (!disposed)
            Options = options;
    }

    public void SetMinimumSize(Size size) => Configure(Options with { MinWidth = size.Width, MinHeight = size.Height });
    public void SetMaximumSize(Size size) => Configure(Options with { MaxWidth = size.Width, MaxHeight = size.Height });
    public void SetOpacity(float opacity) => Configure(Options with { Opacity = opacity });
    public void SetMovable(bool value) => Configure(Options with { Movable = value });
    public void SetMinimizable(bool value) => Configure(Options with { Minimizable = value });
    public void SetMaximizable(bool value) => Configure(Options with { Maximizable = value });
    public void SetClosable(bool value) => Configure(Options with { Closable = value });
    public void SetHasShadow(bool value) => Configure(Options with { HasShadow = value });
    public void SetSkipTaskbar(bool value) => Configure(Options with { SkipTaskbar = value });
    public void SetVisibleOnAllWorkspaces(bool value) => Configure(Options with { VisibleOnAllWorkspaces = value });
    public void SetContentProtection(bool value) => Configure(Options with { ContentProtection = value });
    public void SetIgnoreMouseEvents(bool value) => Configure(Options with { IgnoreMouseEvents = value });
    public void SetProgressBar(double? value)
    {
        CheckShell();
        if (value is { } progress && (!double.IsFinite(progress) || progress is < 0 or > 1))
            throw new ArgumentOutOfRangeException(nameof(value));
        if (App.Options.Hidden && value is not null)
            throw new InvalidOperationException("Hidden apps cannot change the Dock or taskbar progress.");
        backend.Progress(value);
    }

    public void FlashFrame(bool enabled = true)
    {
        CheckShell();
        if (App.Options.Hidden && enabled)
            throw new InvalidOperationException("Hidden apps cannot request desktop attention.");
        backend.Attention(enabled);
    }
}
