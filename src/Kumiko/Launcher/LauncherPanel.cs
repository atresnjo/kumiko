using Kumiko.Runtime;
using Kumiko.UI;

namespace Kumiko.Desktop;

/// <summary>Owns one initially hidden window. Blur and close dismiss it; disposal releases it.</summary>
public sealed class LauncherPanel : IDisposable
{
    readonly App app;
    readonly ILauncherPanelPresentation presentation;
    readonly bool physicalScreenPixels;
    bool disposed;
    public Window Window
    {
        get;
    }
    public bool IsVisible
    {
        get
        {
            if (disposed)
                return false;
            app.CheckThread();
            return presentation.IsVisible;
        }
    }
    public event Action? Dismissed;

    public LauncherPanel(App app, IProgram program, WindowOptions? options = null) : this(app, program, options, null)
    {
    }

    internal LauncherPanel(App app, IProgram program, WindowOptions? options, ILauncherPanelPresentation? presentation, bool? physicalScreenPixels = null)
    {
        app.CheckThread();
        if (program.UsesWindows)
            throw new ArgumentException("A launcher panel requires a single-window program.", nameof(program));
        this.app = app;
        this.physicalScreenPixels = physicalScreenPixels ?? OperatingSystem.IsWindows();
        Window = app.NewWindow((options ?? new(Width: 680, Height: 480)) with
        {
            Visible = false,
            Panel = true,
            Frameless = true,
            Resizable = false,
            AlwaysOnTop = true,
            SkipTaskbar = !OperatingSystem.IsMacOS(),
            VisibleOnAllWorkspaces = !OperatingSystem.IsWindows(),
            MinWidth = 0,
            MinHeight = 0,
            Minimizable = OperatingSystem.IsLinux(),
            Maximizable = OperatingSystem.IsLinux()
        }, context =>
        {
            var composing = context.IsComposing;
            program.View(context);
            if (!composing && context.Shortcut(Key.Escape, Modifiers.None))
                app.Dispatch(() =>
                {
                    if (!disposed)
                        Hide();
                });
        });
        program.Attach(Window);
        this.presentation = presentation ?? new NativePanelPresentation(Window);
        Window.Blurred += Hide;
        Window.Closing += Closing;
        app.Disposing += Dispose;
    }

    /// <summary>Fits to the pointer's work area (or an explicit display), centered one third down.</summary>
    public void Show(Screen? screen = null)
    {
        Check();
        var displays = screen is null ? Screens.All : [screen];
        var fallback = screen ?? displays.FirstOrDefault();
        var wayland = OperatingSystem.IsLinux() && Environment.GetEnvironmentVariable("XDG_SESSION_TYPE") == "wayland";
        var pointer = screen is null && !wayland ? LauncherPointer.Position() : fallback is null ? default :
            new Point(fallback.WorkArea.X + fallback.WorkArea.Width / 2, fallback.WorkArea.Y + fallback.WorkArea.Height / 2);
        var display = displays.MinBy(s => Distance(s.Bounds, pointer)) ?? throw new InvalidOperationException("No connected display.");
        var scale = physicalScreenPixels ? display.Scale : 1;
        Window.SetBounds(Placement(display.WorkArea, new(Window.Options.Width * scale, Window.Options.Height * scale)));
        presentation.Show();
        Window.Invalidate();
    }

    public void Toggle()
    {
        Check();
        if (IsVisible)
            Hide();
        else
            Show();
    }

    public void Hide()
    {
        Check();
        if (!IsVisible)
            return;
        presentation.Hide();
        Dismissed?.Invoke();
    }

    void Closing(CancelableEvent request)
    {
        if (request.IsApplicationQuit)
            return;
        request.PreventDefault();
        Hide();
    }

    /// <summary>Pure placement in Kumiko screen coordinates, including negative display origins.</summary>
    public static Rect Placement(Rect workArea, Size requested)
    {
        if (!float.IsFinite(workArea.X) || !float.IsFinite(workArea.Y) || !float.IsFinite(workArea.Width) || !float.IsFinite(workArea.Height) ||
            !float.IsFinite(requested.Width) || !float.IsFinite(requested.Height) || workArea.Width <= 0 || workArea.Height <= 0 || requested.Width <= 0 || requested.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(workArea));
        var width = Math.Min(requested.Width, workArea.Width);
        var height = Math.Min(requested.Height, workArea.Height);
        return new(workArea.X + (workArea.Width - width) / 2, workArea.Y + (workArea.Height - height) / 3, width, height);
    }

    static float Distance(Rect bounds, Point point)
    {
        var dx = Math.Max(bounds.X - point.X, Math.Max(0, point.X - bounds.Right));
        var dy = Math.Max(bounds.Y - point.Y, Math.Max(0, point.Y - bounds.Bottom));
        return dx * dx + dy * dy;
    }

    void Check()
    {
        app.CheckThread();
        ObjectDisposedException.ThrowIf(disposed, this);
    }

    public void Dispose()
    {
        if (disposed)
            return;
        app.CheckThread();
        disposed = true;
        app.Disposing -= Dispose;
        Window.Blurred -= Hide;
        Window.Closing -= Closing;
        Window.Dispose();
    }
}

internal interface ILauncherPanelPresentation
{
    bool IsVisible
    {
        get;
    }
    void Show();
    void Hide();
}

internal sealed class NativePanelPresentation(Window window) : ILauncherPanelPresentation
{
    public bool IsVisible => window.IsVisible;
    public void Show()
    {
        window.Show();
        if (OperatingSystem.IsWindows())
            window.Focus();
    }
    public void Hide() => window.Hide();
}
