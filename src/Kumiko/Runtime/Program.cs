using System.Collections.Concurrent;
using System.Text.Json.Serialization.Metadata;
using Kumiko.UI;

namespace Kumiko.Runtime;

public partial interface IProgram : IDisposable
{
    // External code cannot implement a free-style host behind this interface.
    internal void RequireKumikoRuntime();
    internal bool WaitForWork(TimeSpan timeout);
    internal IDisposable ConnectHost(Action wake);
    internal bool HasPendingWork
    {
        get;
    }
    int PendingCommands
    {
        get;
    }

    bool DevMode
    {
        get; set;
    }
    bool DebugEnabled
    {
        get; set;
    }
    void View(Context context);
    void Attach(Window window);
    void Record(string path);
    void Save(string path);
    void Replay(string path);
    void Step(int step);
    /// <summary>Raised on the UI thread after the selected model changes.</summary>
    event Action? ModelChanged;
    event Action? Disposing;
    void SaveModel(string path);
    bool RestoreModel(string path);
}

public sealed record MessageSource(Role Role, string Name, string? Key, string Path)
{
    public string? Window
    {
        get; init;
    }
}
public sealed record ProgramStep<TModel, TMessage>(int Id, int Parent, long Frame, double Time,
    MessageSource? Source, TMessage? Message, TModel Model, IReadOnlyList<string> Commands)
{
    public string? Window
    {
        get; init;
    }
}

/// <summary>
/// Owns one immutable model and one ordered message history. Construct, render, navigate and dispose
/// on the UI thread. Dispatch may be called by workers. Models must be deeply immutable.
/// </summary>
public sealed partial class Program<TModel, TMessage> : IProgram, IDisposable, IAsyncDisposable where TModel : notnull where TMessage : notnull
{
    void IProgram.RequireKumikoRuntime()
    {
    }

    readonly int thread = Environment.CurrentManagedThreadId;
    readonly Func<TModel, TMessage, (TModel Model, Cmd<TMessage> Command)> update;
    readonly Action<Context, TModel, Dispatch<TMessage>> view;
    readonly Func<TModel, IEnumerable<Sub<TMessage>>> subscriptions;
    readonly JsonTypeInfo<TModel> modelType;
    readonly JsonTypeInfo<TMessage> messageType;
    readonly KumikoServices services;
    readonly List<ProgramStep<TModel, TMessage>> history = [];
    readonly ConcurrentQueue<Envelope> messages = new();
    readonly Dictionary<string, Subscription> running = new(StringComparer.Ordinal);
    CancellationTokenSource cancellation = new();
    Context? building;
    Context? lastContext;
    Window? window;
    StreamWriter? recorder;
    long generation;
    int pendingCommands;
    bool disposed, frozen, draining, subscriptionsStarted, hideAppDispatch;
    readonly AutoResetEvent workAvailable = new(false);
    readonly Dispatch<TMessage> viewDispatch;
    double renderTime;
    sealed record Envelope(TMessage Message, MessageSource? Source, long Generation, Subscription? Subscription, string? Window, long? Frame, double? Time);
    sealed class Subscription
    {
        public volatile bool Active = true;
        public IDisposable? Handle;
        public KumikoServices.ExecutionScope? Scope;
    }

    internal Program(TModel initial, Func<TModel, TMessage, (TModel Model, Cmd<TMessage> Command)> update,
        Action<Context, TModel, Dispatch<TMessage>> view, JsonTypeInfo<TModel> modelType, JsonTypeInfo<TMessage> messageType,
        Func<TModel, IEnumerable<Sub<TMessage>>>? subscriptions = null)
    {
        this.update = update;
        this.view = view;
        this.modelType = modelType;
        this.messageType = messageType;
        this.subscriptions = subscriptions ?? (_ => []);
        services = new();
        history.Add(new(0, -1, 0, 0, null, default, initial, []));
        History = history.AsReadOnly();
        Send = message => Enqueue(message, null, Volatile.Read(ref generation), null);
        viewDispatch = message => DispatchFromView(message, null, building ?? lastContext);
    }

    /// <summary>Supplies effect dependencies without exposing them to the model, Update or view.</summary>
    internal Program(TModel initial, Func<TModel, TMessage, (TModel Model, Cmd<TMessage> Command)> update,
        Action<Context, TModel, Dispatch<TMessage>> view, JsonTypeInfo<TModel> modelType, JsonTypeInfo<TMessage> messageType,
        Func<TModel, IEnumerable<Sub<TMessage>>>? subscriptions, KumikoServices services)
        : this(initial, update, view, modelType, messageType, subscriptions)
    {
        ArgumentNullException.ThrowIfNull(services);
        this.services = services.Snapshot();
    }

    void DispatchFromView(TMessage message, string? key, Context? context)
    {
        if (Environment.CurrentManagedThreadId == thread && hideAppDispatch)
            return;
        if (Environment.CurrentManagedThreadId == thread && frozen && context is not null &&
            context.AccessibilitySource is null && !context.Input.Pressed && !context.Input.Released && !context.Input.Down &&
            context.Input.Key == UI.Key.None && context.Input.Text.Length == 0)
            return;
        var source = Environment.CurrentManagedThreadId == thread ? context?.SourceOfInput() : null;
        var frame = key is not null && Environment.CurrentManagedThreadId == thread ? context?.FrameId : null;
        var time = key is not null && Environment.CurrentManagedThreadId == thread ? context?.Time : null;
        Enqueue(message, source, Volatile.Read(ref generation), null, key, frame, time);
    }

    bool devMode;
    public bool DevMode
    {
        get => devMode;
        set
        {
            CheckThread();
            var previous = devMode;
            devMode = value;
            try
            {
                if (value && windowFactory is not null)
                    SelectWindows(ReadWindows(Model));
            }
            catch
            {
                devMode = previous;
                throw;
            }
        }
    }

    public TModel Model => history[Position].Model;
    public int Position
    {
        get; private set;
    }
    public IReadOnlyList<ProgramStep<TModel, TMessage>> History
    {
        get;
    }
    public Dispatch<TMessage> Send
    {
        get;
    }
    public int PendingCommands => Volatile.Read(ref pendingCommands);
    /// <summary>Unexpected effect/subscription infrastructure errors, delivered on the UI thread.</summary>
    public event Action<Exception>? UnhandledException;
    readonly ConcurrentQueue<Exception> errors = new();

    void CheckThread()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (Environment.CurrentManagedThreadId != thread)
            throw new InvalidOperationException("Kumiko model updates and history navigation require the owning UI thread.");
    }

    void Enqueue(TMessage message, MessageSource? source, long epoch, Subscription? subscription, string? originWindow = null, long? originFrame = null, double? originTime = null)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (Volatile.Read(ref disposed) || epoch != Volatile.Read(ref generation) || subscription is { Active: false })
            return;
        messages.Enqueue(new(message, source is not null && originWindow is not null ? source with
        {
            Window = originWindow
        } : source, epoch, subscription, originWindow, originFrame, originTime));
        if (Environment.CurrentManagedThreadId != thread || building is null)
            Wake();
    }

    void Wake()
    {
        try
        {
            workAvailable.Set();
        }
        catch (ObjectDisposedException) { }
        Volatile.Read(ref hostWake)?.Invoke();
        if (nativeApp is { } app)
        {
            try
            {
                app.Dispatch(() =>
                {
                    if (!disposed)
                    {
                        ProcessMessages();
                        ReconcileWindows();
                        InvalidateWindows();
                    }
                });
            }
            catch (ObjectDisposedException) { }
            return;
        }
        var attached = window;
        if (attached is null)
            return;
        try
        {
            attached.App.Dispatch(() =>
            {
                if (!disposed)
                {
                    if (attached.Page is not null)
                        ProcessMessages();
                    attached.Invalidate();
                }
            });
        }
        catch (ObjectDisposedException)
        {
            // The host can close while an effect is completing.
        }
    }

    bool IProgram.HasPendingWork => PendingCommands > 0 || !messages.IsEmpty || !errors.IsEmpty;

    bool IProgram.WaitForWork(TimeSpan timeout)
    {
        CheckThread();
        if (!messages.IsEmpty || !errors.IsEmpty)
            return true;
        workAvailable.WaitOne(timeout);
        return !messages.IsEmpty || !errors.IsEmpty;
    }

    public void Attach(Window window)
    {
        CheckThread();
        window.App.CheckThread();
        if (this.window is not null || hostWake is not null || nativeApp is not null || windowFactory is not null)
            throw new InvalidOperationException("A program can be attached to only one window.");
        this.window = window;
        window.Closed += Dispose;
        window.Invalidate();
    }

    public void View(Context context) => ViewForWindow(context, null);

    void ViewCore(Context context)
    {
        CheckThread();
        context.CheckThread();
        if (devMode)
            context.DevMode = true;
        if (building is not null)
            throw new InvalidOperationException("An Kumiko view cannot render recursively.");
        lastContext = context;
        var debugHere = debugEnabled && (windowFactory is null || renderingWindow == desiredWindows.FirstOrDefault()?.Info.Key);
        if (debugHere)
            ApplyDebugAction(context, DebugShortcut(context));
        else
            context.ClearDebugger();
        if (frozen)
            context.Time = renderTime;
        Drain(context);
        if (!subscriptionsStarted)
            Reconcile();
        var savedInput = debugHere ? context.HideDebuggerInput() : default;
        hideAppDispatch = savedInput.Saved is not null;
        building = context;
        try
        {
            RenderView(context);
        }
        finally
        {
            building = null;
            hideAppDispatch = false;
            context.RestoreDebuggerInput(savedInput);
        }
        Drain(context);
        if (debugHere)
            ApplyDebugAction(context, Debugger(context));
        if (frozen)
        {
            context.Time = renderTime;
            context.FreezeProgramFrame();
        }
        else
            renderTime = context.Time;
    }

    void Drain(Context context)
    {
        if (draining)
            return;
        draining = true;
        var changed = false;
        try
        {
            while (errors.TryDequeue(out var error))
            {
                if (error is KumikoViolationException)
                    throw error;
                if (UnhandledException is { } handler)
                    handler(error);
                else
                    throw new InvalidOperationException("An Kumiko effect or subscription failed outside its error mapping.", error);
            }
            while (messages.TryDequeue(out var item))
            {
                if (item.Generation != generation || item.Subscription is { Active: false })
                    continue;
                var (model, command) = Transition(Model, item.Message, context.DevMode || devMode);
                var windows = ReadWindows(model);
                var entry = new ProgramStep<TModel, TMessage>(history.Count, Position, item.Frame ?? context.FrameId, item.Time ?? context.Time,
                    item.Source, item.Message, model, command.Effects.Select(e => e.Name).ToArray())
                {
                    Window = item.Window
                };
                WriteEntry(recorder, entry);
                history.Add(entry);
                Position = entry.Id;
                SelectWindows(windows);
                forwardSteps[entry.Parent] = entry.Id;
                if (!retainHistory)
                    RetireHistory();
                frozen = false;
                context.Changed = true;
                changed = true;
                Reconcile();
                foreach (var effect in command.Effects)
                    Start(effect);
            }
        }
        finally
        {
            draining = false;
        }
        if (changed)
            RaiseModelChanged();
    }

    /// <summary>Runs an effect on a worker. A released held command passes the key registration it already owns.</summary>
    void Execute(Cmd<TMessage>.Effect effect, CancellationTokenSource? keyed = null)
    {
        var epoch = generation;
        var source = keyed ?? (effect.Keyed ? Replace(effect.Name, cancellation.Token) : null);
        var token = source?.Token ?? cancellation.Token;
        Interlocked.Increment(ref pendingCommands);
        Track(Task.Run(async () =>
        {
            using var scope = KumikoExecution.Enter("command");
            try
            {
                TMessage result;
                try
                {
                    await using var execution = services.OpenScope();
                    result = await effect.Work(execution.Services, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception error) when (error is not KumikoViolationException)
                {
                    result = effect.OnError(error);
                }
                if (source is null)
                    Enqueue(result, null, epoch, null);
                else
                    Deliver(effect.Name, source, () => Enqueue(result, null, epoch, null));
            }
            catch (Exception error)
            {
                if (!token.IsCancellationRequested)
                {
                    errors.Enqueue(error);
                    Wake();
                }
            }
            finally
            {
                if (source is not null)
                    Release(effect.Name, source);
                Interlocked.Decrement(ref pendingCommands);
                Wake();
            }
        }));
    }

    void Reconcile()
    {
        using var execution = KumikoExecution.Enter("subscription");
        if (frozen)
            return;
        var desired = subscriptions(Model).ToArray();
        subscriptionsStarted = true;
        if (desired.Select(s => s.Key).Distinct(StringComparer.Ordinal).Count() != desired.Length)
            throw new InvalidOperationException("Subscription keys must be unique.");
        foreach (var key in running.Keys.Except(desired.Select(s => s.Key)).ToArray())
        {
            var old = running[key];
            old.Active = false;
            running.Remove(key);
            StopSubscription(old);
        }
        foreach (var sub in desired)
        {
            if (running.ContainsKey(sub.Key))
                continue;
            var current = new Subscription();
            var epoch = generation;
            running.Add(sub.Key, current);
            try
            {
                using var scope = KumikoExecution.Enter("subscription");
                void Send(TMessage message) => Enqueue(message, null, epoch, current);
                void Report(Exception error)
                {
                    if (epoch != Volatile.Read(ref generation) || !current.Active || disposed)
                        return;
                    errors.Enqueue(error);
                    Wake();
                }
                if (sub.ServiceStart is not null)
                    current.Scope = services.OpenScope();
                current.Handle = sub.ServiceStart is { } serviceStart ? serviceStart(current.Scope!.Services, Send) :
                    sub.CheckedStart is { } start ? start(Send, Report) : sub.Start(Send);
            }
            catch
            {
                current.Active = false;
                running.Remove(sub.Key);
                StopSubscription(current);
                throw;
            }
        }
    }

    public void Step(int step)
    {
        CheckThread();
        if (step < 0 || step >= history.Count)
            throw new ArgumentOutOfRangeException(nameof(step));
        var windows = ReadWindows(history[step].Model);
        Suspend();
        RememberRoute(step);
        Position = step;
        SelectWindows(windows);
        renderTime = history[step].Time;
        WriteCursor(recorder);
        ResetContexts();
        InvalidateWindows();
        RaiseModelChanged();
    }

    void Suspend()
    {
        if (building is not null)
            throw new InvalidOperationException("History navigation cannot run inside a view.");
        frozen = true;
        subscriptionsStarted = false;
        Interlocked.Increment(ref generation);
        var previous = cancellation;
        previous.Cancel();
        cancellation = new();
        foreach (var sub in running.Values)
        {
            sub.Active = false;
            StopSubscription(sub);
        }
        running.Clear();
        DiscardHeld();
        RetireCancellation(previous);
        messages.Clear();
        errors.Clear();
    }

    public event Action? Disposing;

    public void Dispose()
    {
        if (disposed)
            return;
        CheckThread();
        Disposing?.Invoke();
        Suspend();
        DisposeWindows();
        Volatile.Write(ref disposed, true);
        Interlocked.Exchange(ref hostWake, null);
        cancellation.Dispose();
        WriteCursor(recorder);
        recorder?.Dispose();
        recorder = null;
        workAvailable.Dispose();
        if (window is not null)
            window.Closed -= Dispose;
    }
}
