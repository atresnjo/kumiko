using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Kumiko;
/// <summary>A cancelable native close or quit request. Cancellation is monotonic.</summary>
public sealed class CancelableEvent
{
    internal bool IsApplicationQuit
    {
        get; init;
    }

    public bool DefaultPrevented
    {
        get; private set;
    }

    public void PreventDefault() => DefaultPrevented = true;
}

public sealed partial class App
{
    bool quitting, disposing, quitPublished, stopped;
    bool? active;
    IDisposable? clipboardScope;
    readonly List<IDisposable> signals = new();
    public event Action<bool>? Activated;
    public event Action? DidBecomeActive;
    public event Action? DidResignActive;
    public event Action<Window>? WindowCreated;
    public event Action? WindowAllClosed;
    public event Action<CancelableEvent>? BeforeQuit;
    public event Action<CancelableEvent>? WillQuit;
    public event Action? DidQuit;
    public int ExitCode
    {
        get; private set;
    }

    void InitializeLifecycle()
    {
        clipboardScope = Desktop.Clipboard.Attach(this);
        if (!OperatingSystem.IsWindows())
            foreach (var signal in new[]
            {
                PosixSignal.SIGINT,
                PosixSignal.SIGTERM
            }

            )
                signals.Add(PosixSignalRegistration.Create(signal, context =>
                {
                    context.Cancel = true;
                    try
                    {
                        Dispatch(Quit);
                    }
                    catch (ObjectDisposedException)
                    {
                    }
                }));
        if (OperatingSystem.IsWindows())
            Console.CancelKeyPress += ConsoleInterrupt;
        ParseOpenArguments(Environment.GetCommandLineArgs().Skip(1), Environment.CurrentDirectory, launch: true);
    }

    void ConsoleInterrupt(object? sender, ConsoleCancelEventArgs args)
    {
        args.Cancel = true;
        try
        {
            Dispatch(Quit);
        }
        catch (ObjectDisposedException)
        {
        }
    }

    void DisposeLifecycle()
    {
        if (OperatingSystem.IsWindows())
            Console.CancelKeyPress -= ConsoleInterrupt;
        try
        {
            clipboardScope?.Dispose();
        }
        finally
        {
            try
            {
                instanceLock?.Dispose();
            }
            finally
            {
                foreach (var signal in signals)
                    signal.Dispose();
                signals.Clear();
            }
        }
    }

    internal void PublishWindowCreated(Window window) => WindowCreated?.Invoke(window);
    void PublishWindowAllClosed() => WindowAllClosed?.Invoke();
    internal void NativeActivated() => Activated?.Invoke(windows.Any(w => w.IsVisible));
    internal void NativeActiveChanged(bool value)
    {
        if (active == value)
            return;
        active = value;
        if (value)
            DidBecomeActive?.Invoke();
        else
            DidResignActive?.Invoke();
    }

    int quitGeneration;
    Action<bool>? nativeQuitReply;
    ProcessStartInfo? relaunch;

    internal void RequestNativeQuit(Action<bool> reply)
    {
        CheckThread();
        if (disposing)
        {
            reply(false);
            return;
        }
        nativeQuitReply += reply;
        if (stopped)
            CompleteNativeQuit(true);
        else
            RequestQuit();
    }

    void CompleteNativeQuit(bool approved)
    {
        var reply = nativeQuitReply;
        nativeQuitReply = null;
        if (reply is not null)
            foreach (Action<bool> callback in reply.GetInvocationList())
                try
                {
                    callback(approved);
                }
                catch (Exception error)
                {
                    Report(error);
                }
    }

    void CancelQuit()
    {
        quitting = false;
        quitGeneration++;
        relaunch = null;
        CompleteNativeQuit(false);
    }

    bool CurrentQuit(int generation) => quitting && !stopped && !disposing && !disposed && generation == quitGeneration;

    void RequestQuit()
    {
        if (quitting || disposing || stopped)
            return;
        quitting = true;
        int generation = ++quitGeneration;
        try
        {
            var before = new CancelableEvent();
            BeforeQuit?.Invoke(before);
            if (!CurrentQuit(generation))
                return;
            if (before.DefaultPrevented)
            {
                CancelQuit();
                return;
            }
            var closing = windows.ToArray();
            foreach (var window in closing)
            {
                bool approved = window.ApprovesQuitClose();
                if (!CurrentQuit(generation))
                    return;
                if (!approved)
                {
                    CancelQuit();
                    return;
                }
            }
            var modelWindows = closing.Where(w => w.QuitCloseRequested is not null && windows.Contains(w)).ToArray();
            int remaining = modelWindows.Length;
            void Complete(bool approved)
            {
                if (!CurrentQuit(generation))
                    return;
                if (!approved)
                {
                    CancelQuit();
                    return;
                }
                if (--remaining == 0)
                    FinishQuit(closing, generation);
            }
            if (remaining == 0)
                FinishQuit(closing, generation);
            else
                foreach (var window in modelWindows)
                {
                    if (!CurrentQuit(generation))
                        break;
                    window.QuitCloseRequested!(Complete);
                }
        }
        catch
        {
            if (!stopped)
                CancelQuit();
            throw;
        }
    }

    void FinishQuit(Window[] approved, int generation)
    {
        if (!CurrentQuit(generation))
            return;
        // A model close can open a confirmation/replacement window. The old
        // approval must never authorize disposing a new window without asking it.
        if (windows.Any(window => !approved.Contains(window)))
        {
            CancelQuit();
            return;
        }
        foreach (var window in windows.ToArray())
        {
            window.Dispose();
            if (!CurrentQuit(generation))
                return;
        }
        if (windows.Count > 0)
        {
            CancelQuit();
            return;
        }
        var will = new CancelableEvent();
        WillQuit?.Invoke(will);
        if (!CurrentQuit(generation))
            return;
        if (will.DefaultPrevented || windows.Count > 0)
        {
            CancelQuit();
            return;
        }
        stopped = true;
        quitting = false;
        try
        {
            Backend.Quit();
            if (nativeQuitReply is not null)
                PublishQuit();
        }
        finally
        {
            CompleteNativeQuit(true);
        }
    }

    void PublishQuit()
    {
        if (quitPublished)
            return;
        quitPublished = true;
        Environment.ExitCode = ExitCode;
        if (DidQuit is { } subscribers)
            foreach (Action subscriber in subscribers.GetInvocationList())
                try
                {
                    subscriber();
                }
                catch (Exception error)
                {
                    Report(error);
                }
        if (relaunch is { } start)
        {
            relaunch = null;
            ReleaseSingleInstanceLock();
            Process.Start(start)?.Dispose();
        }
    }

    /// <summary>Stops the event loop without close/quit events. Returns the code through Run and the process exit status.</summary>
    public void Exit(int code = 0)
    {
        CheckThread();
        ExitCode = code;
        stopped = true;
        quitting = false;
        quitGeneration++;
        relaunch = null;
        quitPublished = true;
        Environment.ExitCode = code;
        Backend.Quit();
        CompleteNativeQuit(true);
    }

    public void Activate()
    {
        CheckThread();
        if (Options.Hidden || Options.ActivationPolicy == ActivationPolicy.Background)
            throw new InvalidOperationException("A hidden or background application cannot activate the desktop.");
        Backend.Activate();
    }

    /// <summary>Starts the current executable with its original arguments after a successful normal quit.</summary>
    public void Relaunch()
    {
        CheckThread();
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("No executable path is available.");
        relaunch = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            WorkingDirectory = Environment.CurrentDirectory
        };
        if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            relaunch.ArgumentList.Add(Path.GetFullPath(Environment.GetCommandLineArgs()[0]));
        foreach (var argument in Environment.GetCommandLineArgs().Skip(1))
            relaunch.ArgumentList.Add(argument);
        Quit();
    }
}
