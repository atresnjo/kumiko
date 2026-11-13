using Kumiko.Platform;
using Kumiko.Rendering;
using Kumiko.UI;
using Kumiko.Web;

namespace Kumiko;

public sealed record AppOptions(string Name = "Kumiko", bool QuitOnLastWindow = true, bool Hidden = false)
{
    public ActivationPolicy ActivationPolicy
    {
        get; init;
    }
}

public enum ActivationPolicy
{
    Regular,
    Accessory,
    Background
}
public sealed partial class App : IDisposable
{
    internal IAppBackend Backend
    {
        get;
    }

    readonly List<Window> windows = new();
    readonly HashSet<ScheduledAction> schedules = new();
    readonly int thread = Environment.CurrentManagedThreadId;
    bool disposed, desktopActionsMenu;
    [ThreadStatic]
    static App? current;
    readonly App? previous;
    internal static App Current => current ?? throw new InvalidOperationException("This desktop service requires an App on the main thread.");
    public AppOptions Options
    {
        get;
    }
    public IReadOnlyList<Window> Windows => windows;
    public ServiceRegistry Services { get; } = new();
    public Desktop.Menu? Menu
    {
        get; private set;
    }

    WindowStateStore? windowStates;
    /// <summary>
    /// Where windows with <see cref = "WindowOptions.RememberAs"/> keep their placement. Defaults to
    /// <c>&lt;app data&gt;/&lt;app name&gt;/windows</c>; tests should assign a temporary directory.
    /// </summary>
    public WindowStateStore WindowStates
    {
        get => windowStates ??= new(Path.Combine(Desktop.Paths.Get(Desktop.DirectoryKind.AppData), Options.Name, "windows")); set => windowStates = value ?? throw new ArgumentNullException(nameof(value));
    }

    public event Action<Exception>? UnhandledException;
    public App(AppOptions? options = null) : this(options, null)
    {
    }

    internal App(AppOptions? options, Func<App, IAppBackend>? factory)
    {
        Options = options ?? new();
        Backend = factory is not null ? factory(this) : OperatingSystem.IsMacOS() ? new MacApp(this) : OperatingSystem.IsWindows() ? new WindowsApp(this) : OperatingSystem.IsLinux() ? new LinuxApp(this) : throw new PlatformNotSupportedException();
        previous = current;
        current = this;
        InitializeLifecycle();
        HotReload.Register(this);
    }

    internal void CheckThread()
    {
        Runtime.KumikoExecution.CheckUI();
        ObjectDisposedException.ThrowIf(disposed, this);
        if (Environment.CurrentManagedThreadId != thread)
            throw new InvalidOperationException("Native UI must run on the App's main thread. Use App.Dispatch.");
    }

    internal void Report(Exception exception)
    {
        try
        {
            if (UnhandledException is { } handler)
                handler(exception);
            else
                Console.Error.WriteLine(exception);
        }
        catch (Exception reportingError)
        {
            Console.Error.WriteLine(reportingError);
        }
    }

    internal Window NewWindow(WindowOptions options, Action<Context> view)
    {
        CheckThread();
        var window = new Window(this, options, view, null);
        windows.Add(window);
        Menu?.Attach(window);
        PublishWindowCreated(window);
        return window;
    }

    /// <summary>Hosts every model-derived window of one shared program.</summary>
    public void NewWindows(Runtime.IProgram program, WindowOptions? defaults = null) => program.AttachWindows(this, defaults ?? new());
    public Window NewWindow(WindowOptions options, Runtime.IProgram program)
    {
        if (program.UsesWindows)
            throw new InvalidOperationException("Use App.NewWindows for model-driven windows.");
        var window = NewWindow(options, program.View);
        program.Attach(window);
        return window;
    }

    public Window NewPage(WindowOptions options, PageOptions page, Runtime.IProgram program)
    {
        if (program.UsesWindows)
            throw new InvalidOperationException("A web page host cannot own model-driven native windows.");
        var window = NewPage(options, page);
        program.Attach(window);
        return window;
    }

    /// <summary>Opens a window whose whole content is a web page. A page has no Kumiko view, so it needs no program;
    /// the page reaches native features only through services registered on <see cref="Services"/>.</summary>
    public Window NewPage(WindowOptions options, PageOptions page)
    {
        CheckThread();
        var window = new Window(this, options, null, page);
        windows.Add(window);
        Menu?.Attach(window);
        PublishWindowCreated(window);
        return window;
    }

    internal void Remove(Window window)
    {
        windows.Remove(window);
        RefreshDesktopActions();
        if (windows.Count == 0 && !disposing)
        {
            PublishWindowAllClosed();
            if (!quitting && Options.QuitOnLastWindow)
                Quit();
        }
    }

    public void Run()
    {
        CheckThread();
        Backend.Run();
        PublishQuit();
    }

    // Explicitly named window submenus avoid guessing the active document from the last render.
    internal void RefreshDesktopActions()
    {
        if (Menu is { HasDesktopActions: true } template)
        {
            template.BindDesktopActions(ResolveDesktopAction);
            return;
        }
        var entries = windows.Where(w => w.Context?.DesktopMenuItems is not null).Select(w => new Desktop.MenuItem(w.Options.Title, Submenu: w.Context!.DesktopMenuItems)).ToArray();
        if (entries.Length > 0)
        {
            SetMenu(new Desktop.MenuItem(Options.Name, Submenu: entries));
            desktopActionsMenu = true;
        }
        else if (desktopActionsMenu)
            SetMenu();
    }

    public void SetMenu(params Desktop.MenuItem[] items)
    {
        CheckThread();
        // Validate the replacement before retiring the installed menu.
        var next = new Desktop.Menu(this, items);
        Menu?.Dispose();
        Menu = next;
        Menu.Install();
        desktopActionsMenu = false;
        if (Menu.HasDesktopActions)
            Menu.BindDesktopActions(ResolveDesktopAction);
    }

    // A placeholder binds only to an unambiguous window; it never guesses the active document.
    Desktop.MenuItem? ResolveDesktopAction(string id)
    {
        Desktop.MenuItem? match = null;
        foreach (var window in windows)
        {
            if (window.Context?.DesktopMenuItems?.FirstOrDefault(item => item.Id == id) is not { } item)
                continue;
            if (match is not null)
                return null;
            match = item;
        }
        return match;
    }

    internal void InvokeDesktopAction(string id)
    {
        if (ResolveDesktopAction(id) is { Disabled: false, Click: { } click } item)
            click(item);
    }

    public void RunFor(TimeSpan duration)
    {
        CheckThread();
        Backend.RunFor(duration);
    }

    /// <summary>
    /// Queues worker results on the native UI thread. Update state and invalidate the window inside this
    /// callback.
    /// </summary>
    public void Dispatch(Action action)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        Backend.Dispatch(() =>
        {
            if (disposed)
                return;
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Report(ex);
            }
        });
    }

    public IDisposable Schedule(TimeSpan delay, Action action, TimeSpan? period = null)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var scheduled = new ScheduledAction(this, action, period is not null);
        lock (schedules)
            schedules.Add(scheduled);
        scheduled.Start(delay, period ?? Timeout.InfiniteTimeSpan);
        return scheduled;
    }

    sealed class ScheduledAction(App app, Action action, bool repeating) : IDisposable
    {
        Timer? timer;
        int canceled;
        public void Start(TimeSpan delay, TimeSpan period) => timer = new Timer(_ =>
        {
            if (Volatile.Read(ref canceled) != 0)
                return;
            try
            {
                app.Dispatch(() =>
                {
                    if (Volatile.Read(ref canceled) == 0)
                        action();
                    if (!repeating)
                        Dispose();
                });
            }
            catch (ObjectDisposedException)
            {
                Dispose();
            }
        }, null, delay, period);
        public void Dispose()
        {
            if (Interlocked.Exchange(ref canceled, 1) != 0)
                return;
            timer?.Dispose();
            lock (app.schedules)
                app.schedules.Remove(this);
        }
    }

    public void Quit()
    {
        CheckThread();
        RequestQuit();
    }

    internal event Action? Disposing;
    public void Dispose()
    {
        if (disposed || disposing)
            return;
        CheckThread();
        disposing = true;
        void Cleanup(Action release)
        {
            try
            {
                release();
            }
            catch (Exception error)
            {
                Report(error);
            }
        }
        if (Disposing is { } subscribers)
            foreach (Action subscriber in subscribers.GetInvocationList())
                Cleanup(subscriber);
        Cleanup(DisposeLifecycle);
        foreach (var w in windows.ToArray())
            Cleanup(w.Dispose);
        lock (schedules)
            foreach (var timer in schedules.ToArray())
                Cleanup(timer.Dispose);
        if (Menu is { } menu)
            Cleanup(menu.Dispose);
        Cleanup(Backend.Dispose);
        disposed = true;
        if (current == this)
            current = previous;
        HotReload.Unregister(this);
    }
}
