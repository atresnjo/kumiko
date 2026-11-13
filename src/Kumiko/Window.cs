using Kumiko.Platform;
using Kumiko.Rendering;
using Kumiko.UI;
using Kumiko.Web;

namespace Kumiko;

public sealed partial class Window : IDisposable
{
    readonly Action<Context>? view;
    readonly IWindowBackend backend;
    bool pending, disposed, drawing, inputWaiting;
    IDisposable? frameTimer;
    long lastFrame;
    public App App
    {
        get;
    }
    public WindowOptions Options
    {
        get; private set;
    }
    public Context? Context
    {
        get;
    }
    public Page? Page
    {
        get; internal set;
    }
    public bool IsVisible => !disposed && backend.IsVisible;
    public string Renderer => backend.Renderer;

    public event Action? Closed;
    internal Window(App app, WindowOptions options, Action<Context>? view, PageOptions? page)
    {
        App = app;
        ValidateOptions(app, options);
        Options = options;
        this.view = view;
        if (view is not null)
            Context = new()
            {
                DevMode = options.DevMode,
                Size = new(options.Width, options.Height),
                Theme = options.Theme ?? (app.Options.Hidden ? UI.Theme.Light : Desktop.Appearance.Theme),
                ApplicationIcons = options.Icons ?? new(bundle => Rendering.Image.ApplicationIcon(bundle))
            };
        if (Context is not null && !app.Options.Hidden)
            Context.ReduceMotion = Desktop.Appearance.ReduceMotion;
        backend = app.Backend.CreateWindow(this, page);
        if (options.RememberAs is { } name && app.WindowStates.Load(name) is { } remembered)
            Restore(remembered);
        InitializeShell();
        Invalidate();
    }

    /// <summary>The window's normal placement in screen coordinates, or null where the platform cannot report it.</summary>
    public WindowState? State
    {
        get
        {
            App.CheckThread();
            if (disposed || backend.State is not { } state)
                return null;
            return state with
            {
                FullScreen = deferredFullScreen ?? state.FullScreen,
                Maximized = deferredMaximized ?? state.Maximized
            };
        }
    }

    /// <summary>Moves and sizes the window to <paramref name = "state"/>, fitted to the connected screens.</summary>
    public void Restore(WindowState state)
    {
        App.CheckThread();
        ArgumentNullException.ThrowIfNull(state);
        if (disposed)
            return;
        var fitted = state.Fit(Desktop.Screens.All);
        backend.Restore(fitted);
        if (disposed)
            return;
        deferredFullScreen = state.FullScreen;
        deferredMaximized = state.Maximized;
        ApplyDeferredState();
        if (disposed)
            return;
        if (Context is not null && backend.State is { } applied && !OperatingSystem.IsWindows())
            Context.Size = new(applied.Width, applied.Height);
        Invalidate();
    }

    /// <summary>Re-reads system appearance, contrast, text size and motion settings after the system reports a change.</summary>
    internal void SystemPreferencesChanged()
    {
        if (disposed || App.Options.Hidden || Context is null)
            return;
        Desktop.Appearance.NotifyChanged();
        ApplyPreferences(Desktop.Appearance.ReduceMotion, Desktop.Appearance.Theme);
    }

    /// <summary>Applies system preferences; an explicit window theme is never replaced.</summary>
    internal void ApplyPreferences(bool reduceMotion, Theme theme)
    {
        if (Context is null)
            return;
        Context.ReduceMotion = reduceMotion;
        if (Options.Theme is null)
            Context.Theme = theme;
        Invalidate();
    }

    public void Invalidate()
    {
        if (disposed || pending || Context is null)
            return;
        pending = true;
        App.Dispatch(() =>
        {
            if (disposed || !pending)
                return;
            pending = false;
            Draw();
        });
    }

    internal void Draw()
    {
        if (disposed || drawing || backend is null || Context is null || view is null)
            return;
        pending = false;
        drawing = true;
        try
        {
            Context.TextSystem.Scale = Context.Scale;
            Context.TextSystem.Family = Context.Theme.Font;
            Context.TextSystem.MonoFamily = Context.Theme.MonoFont;
            long now = System.Diagnostics.Stopwatch.GetTimestamp();
            float dt = lastFrame == 0 ? 1 / 60f : (float)Math.Min(.1, System.Diagnostics.Stopwatch.GetElapsedTime(lastFrame, now).TotalSeconds);
            Context.Frame(view, dt);
            var theme = Context.Theme;
            if (Options.Transparent || Options.Material != WindowMaterial.None)
                Context.Theme = theme with
                {
                    Background = Color.Transparent
                };
            try
            {
                PresentWebViews();
                backend.Present(Context);
            }
            finally
            {
                Context.Theme = theme;
            }

            PublishReady();
            App.RefreshDesktopActions();
            lastFrame = Context.Animating ? now : 0;
            frameTimer?.Dispose();
            frameTimer = null;
            // Animation follows the display's refresh (120 Hz on ProMotion) where the backend can pace frames.
            // A backstop redraw covers a link that stops delivering; normally the next refresh disposes it first.
            var delay = !IsVisible ? null : Context.Animating ? backend.RequestFrame() ? Backstop : TimeSpan.FromSeconds(1 / 60d) : Context.NextFrame;
            if (delay is { } d)
                frameTimer = App.Schedule(d, Invalidate);
        }
        finally
        {
            drawing = false;
        }
    }

    internal void ResizeFromNative(float width, float height, float scale, bool synchronous = false)
    {
        if (!OperatingSystem.IsMacOS())
            Page?.Place(new(0, 0, width, height), true);
        if (Context is not null)
        {
            if (Context.Size == new Size(width, height) && Context.Scale == scale)
                return;
            Context.Size = new(width, height);
            Context.Scale = scale;
            // AppKit commits layer geometry at the end of this notification. Supply the
            // matching frame now so live resize cannot stretch a previous viewport.
            if (synchronous && backend is not null && !drawing)
                Draw();
            else
                Invalidate();
        }
    }

    /// <summary>
    /// Applies native input and draws. Coalesced input (pointer motion, scrolling) accumulates until the next
    /// display refresh when the backend paces frames, so a 1000 Hz mouse or a fast trackpad costs one frame per
    /// refresh instead of one per event.
    /// </summary>
    internal void Input(Action<Input> update, bool coalesce = false)
    {
        if (disposed || Context is null)
            return;
        update(Context.Input);
        if (App.Menu?.HandleInput(this, Context.Input) == true)
            Context.Input.Clear();
        if (coalesce && backend.RequestFrame())
        {
            if (!inputWaiting)
                inputBackstop = App.Schedule(Backstop, FlushInput);
            inputWaiting = true;
            return;
        }
        DrawInput();
    }

    /// <summary>How long coalesced input or an animation waits for a display refresh before drawing anyway.</summary>
    static readonly TimeSpan Backstop = TimeSpan.FromMilliseconds(100);
    IDisposable? inputBackstop;

    void FlushInput()
    {
        if (inputWaiting && !disposed)
            DrawInput();
    }

    void DrawInput()
    {
        inputBackstop?.Dispose();
        inputBackstop = null;
        inputWaiting = false;
        Draw();
        if (Context?.TakeNativeDrag() is { } drag)
        {
            if (App.Options.Hidden)
                throw new InvalidOperationException("Hidden apps cannot start native drags.");
            backend.StartDrag(drag.Data, drag.Bounds);
        }
    }

    /// <summary>Draws the frame a backend requested with <see cref="IWindowBackend.RequestFrame"/>.</summary>
    internal void DisplayFrame()
    {
        if (inputWaiting)
            DrawInput();
        else
            Draw();
    }

    public void Show()
    {
        CheckShell();
        if (App.Options.Hidden || App.Options.ActivationPolicy == ActivationPolicy.Background)
            throw new InvalidOperationException("A hidden or background application cannot show windows.");
        backend.Show();
        ApplyDeferredState();
        NativeShellChanged();
    }

    public void Hide()
    {
        CheckShell();
        backend.Hide();
        NativeShellChanged();
    }

    public void SetTitle(string title)
    {
        CheckShell();
        Options = Options with
        {
            Title = title
        };
        backend.SetTitle(title);
    }

    public void Resize(float width, float height)
    {
        CheckShell();
        ValidateOptions(App, Options with
        {
            Width = width,
            Height = height
        });
        backend.Resize(width, height);
        NativeShellChanged();
    }

    public void Minimize()
    {
        CheckShell();
        if (App.Options.Hidden)
            throw new InvalidOperationException("Hidden apps cannot minimize native windows.");
        backend.Minimize();
        NativeShellChanged();
    }

    public void ToggleMaximize()
    {
        App.CheckThread();
        if (IsMaximized)
            Unmaximize();
        else
            Maximize();
    }

    internal int MoveRequests
    {
        get; private set;
    }
    internal int MovesStarted
    {
        get; private set;
    }

    /// <summary>Moves the window with the pointer while the primary button is held, as a title bar does.
    /// Web pages request this through elements styled <c>--app-region: drag</c>.</summary>
    internal void BeginMove()
    {
        App.CheckThread();
        if (disposed)
            return;
        MoveRequests++;
        if (backend.BeginMove())
            MovesStarted++;
    }

    internal Action? CloseRequested
    {
        get; set;
    }

    bool requestingClose;
    internal bool CanNativeClose() => !disposed && Options.Closable && CanClose();

    bool CanClose()
    {
        if (disposed || requestingClose)
            return false;
        requestingClose = true;
        try
        {
            var closing = new CancelableEvent();
            if (Closing is { } handlers)
            {
                foreach (Action<CancelableEvent> handler in handlers.GetInvocationList())
                {
                    if (disposed)
                        return false;
                    handler(closing);
                }
            }

            if (disposed || closing.DefaultPrevented)
                return false;
            if (CloseRequested is not { } requested)
                return true;
            requested();
            return false;
        }
        finally
        {
            requestingClose = false;
        }
    }

    public void Close()
    {
        if (disposed)
            return;
        App.CheckThread();
        if (CanClose())
            Dispose();
    }

    public Pixels Capture()
    {
        App.CheckThread();
        if (Context is null)
            throw new InvalidOperationException("Capture requires a native view.");
        Draw();
        var theme = Context.Theme;
        if (Options.Transparent || Options.Material != WindowMaterial.None)
            Context.Theme = theme with
            {
                Background = Color.Transparent
            };
        try
        {
            return backend.Capture(Context);
        }
        finally
        {
            Context.Theme = theme;
        }
    }

    internal void NativeClosed()
    {
        if (!disposed)
            Dispose();
    }

    public void Dispose()
    {
        if (disposed)
            return;
        App.CheckThread();
        if (Options.RememberAs is { } name && State is { } state)
            App.WindowStates.Save(name, state);
        foreach (var child in App.Windows.Where(w => w.Options.Parent == this).ToArray())
            child.Dispose();
        disposed = true;
        frameTimer?.Dispose();
        inputBackstop?.Dispose();
        Page?.Dispose();
        foreach (var page in webViews.Values)
            page.Dispose();
        webViews.Clear();
        backend.Dispose();
        Context?.Dispose();
        App.Remove(this);
        Closed?.Invoke();
    }
}
