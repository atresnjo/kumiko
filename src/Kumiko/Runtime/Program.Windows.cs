using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Kumiko.UI;

namespace Kumiko.Runtime;

public sealed partial class Program<TModel, TMessage>
{
    sealed record WindowEntry(object Id, ProgramWindow Info, WindowOptions Options, TMessage Close,
        Action<Context, TModel, Dispatch<TMessage>> Render);
    Func<TModel, WindowEntry[]>? windowFactory;
    WindowEntry[] desiredWindows = [];
    readonly Dictionary<Context, string> contextWindows = [];
    readonly Dictionary<string, Window> nativeWindows = new(StringComparer.Ordinal);
    App? nativeApp;
    WindowOptions windowDefaults = new();
    string? renderingWindow;
    bool reconciliationQueued, reconciledWindowSet, lastWindowSetEmpty;

    public IReadOnlyList<ProgramWindow> Windows
    {
        get
        {
            CheckThread();
            return desiredWindows.Select(w => w.Info).ToArray();
        }
    }

    /// <summary>Generated multi-window wiring. Configure once before attaching or rendering.</summary>
    public Program<TModel, TMessage> WithWindows<TWindow>(Func<TModel, IEnumerable<WindowSpec<TWindow, TMessage>>> windows,
        Func<TModel, TWindow, NavigationRoot<TMessage>> root, JsonTypeInfo<TWindow> windowType) where TWindow : notnull
    {
        CheckThread();
        ArgumentNullException.ThrowIfNull(windows);
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(windowType);
        if (windowFactory is not null || lastContext is not null || window is not null || hostWake is not null)
            throw new InvalidOperationException("Configure windows once, before hosting or rendering.");
        windowFactory = model => windows(model).Select(spec =>
        {
            ArgumentNullException.ThrowIfNull(spec.Id);
            ArgumentNullException.ThrowIfNull(spec.OnClose);
            ArgumentNullException.ThrowIfNull(spec.Title);
            var key = JsonSerializer.Serialize(spec.Id, windowType);
            if (!float.IsFinite(spec.Width) || !float.IsFinite(spec.Height) || spec.Width <= 0 || spec.Height <= 0 ||
                !float.IsFinite(spec.MinWidth) || !float.IsFinite(spec.MinHeight) || spec.MinWidth < 0 || spec.MinHeight < 0)
                throw new InvalidOperationException($"Invalid dimensions for window {key}.");
            var options = new WindowOptions(spec.Title, spec.Width, spec.Height, spec.MinWidth, spec.MinHeight,
                spec.Resizable, HiddenTitleBar: spec.HiddenTitleBar, AlwaysOnTop: spec.AlwaysOnTop, Theme: spec.Theme)
            {
                RememberAs = spec.RememberAs
            };
            return new WindowEntry(spec.Id, new(key, spec.Title, spec.Width, spec.Height), options, spec.OnClose,
                (context, current, send) => RenderRoot(context, root(current, spec.Id), send));
        }).ToArray();
        try
        {
            desiredWindows = ReadWindows(Model);
            return this;
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    WindowEntry[] ReadWindows(TModel model)
    {
        if (windowFactory is null)
            return [];
        WindowEntry[] Read()
        {
            using var scope = KumikoExecution.Enter("update");
            var entries = windowFactory(model);
            if (entries.GroupBy(w => w.Info.Key, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1) is { } duplicate)
                throw new InvalidOperationException($"Duplicate window identity: {duplicate.Key}");
            if (hostWake is not null && entries.Length > 1)
                throw new InvalidOperationException("This host has one surface. Multi-window models require a desktop window host.");
            return entries;
        }
        var before = devMode ? JsonSerializer.Serialize(model, modelType) : null;
        var first = Read();
        if (devMode)
        {
            var second = Read();
            if (before != JsonSerializer.Serialize(model, modelType) || first.Length != second.Length ||
                !first.Zip(second).All(pair => pair.First.Info == pair.Second.Info && pair.First.Options == pair.Second.Options &&
                    JsonSerializer.Serialize(pair.First.Close, messageType) == JsonSerializer.Serialize(pair.Second.Close, messageType)))
                throw new InvalidOperationException("Kumiko Windows(model) must be pure and deterministic, including identities, ordering, options and close messages.");
        }
        return first;
    }

    void SelectWindows(WindowEntry[] entries)
    {
        desiredWindows = entries;
        InvalidateWindows();
        QueueWindows();
    }

    void ResetContexts()
    {
        foreach (var context in contextWindows.Keys.Append(lastContext).OfType<Context>().Distinct())
            context.ResetProgramView();
    }

    void InvalidateWindows()
    {
        window?.Invalidate();
        foreach (var native in nativeWindows.Values)
            native.Invalidate();
    }

    bool IProgram.UsesWindows => windowFactory is not null;
    IReadOnlyList<ProgramWindow> IProgram.WindowList() => Windows;
    string IProgram.WindowKey(object identity)
    {
        CheckThread();
        return desiredWindows.FirstOrDefault(w => Equals(w.Id, identity))?.Info.Key ??
            throw new KeyNotFoundException($"Window is not open: {identity}");
    }
    void IProgram.ForgetContext(Context context)
    {
        contextWindows.Remove(context);
        if (lastContext == context)
            lastContext = null;
    }
    void IProgram.ViewWindow(Context context, string key) => ViewForWindow(context, key);

    void ViewForWindow(Context context, string? key)
    {
        CheckThread();
        if (building is not null)
            throw new InvalidOperationException("A Kumiko view cannot render recursively.");
        renderingWindow = key ?? desiredWindows.FirstOrDefault()?.Info.Key;
        if (windowFactory is not null && renderingWindow is { } selected)
        {
            if (contextWindows.TryGetValue(context, out var previous) && previous != selected)
                context.ResetProgramView();
            contextWindows[context] = selected;
        }
        ViewCore(context);
    }

    void IProgram.AttachWindows(App app, WindowOptions defaults)
    {
        CheckThread();
        app.CheckThread();
        if (windowFactory is null || window is not null || hostWake is not null || nativeApp is not null)
            throw new InvalidOperationException("Attach a configured multi-window program to one host only.");
        nativeApp = app;
        windowDefaults = defaults;
        app.Disposing += Dispose;
        try
        {
            ReconcileWindows();
        }
        catch
        {
            DisposeWindows();
            throw;
        }
    }

    void QueueWindows()
    {
        if (nativeApp is null || reconciliationQueued || disposed)
            return;
        reconciliationQueued = true;
        nativeApp.Dispatch(() =>
        {
            reconciliationQueued = false;
            if (!disposed)
                ReconcileWindows();
        });
    }

    void ReconcileWindows()
    {
        if (nativeApp is not { } app)
            return;
        bool empty = desiredWindows.Length == 0;
        bool becameEmpty = empty && (!reconciledWindowSet || !lastWindowSetEmpty);
        reconciledWindowSet = true;
        lastWindowSetEmpty = empty;
        // Add replacements first so QuitOnLastWindow never observes a transient empty set.
        foreach (var entry in desiredWindows)
        {
            var key = entry.Info.Key;
            if (nativeWindows.TryGetValue(key, out var existing))
            {
                if (existing.Options.Title != entry.Info.Title)
                    existing.SetTitle(entry.Info.Title);
                existing.Invalidate();
                continue;
            }
            var options = entry.Options with
            {
                Visible = windowDefaults.Visible,
                Theme = entry.Options.Theme ?? windowDefaults.Theme,
                Icons = windowDefaults.Icons,
                DevMode = devMode || windowDefaults.DevMode
            };
            var opened = app.NewWindow(options, context => ViewForWindow(context, key));
            nativeWindows.Add(key, opened);
            opened.CloseRequested = () =>
            {
                if (desiredWindows.FirstOrDefault(w => w.Info.Key == key) is { } current)
                    Enqueue(current.Close, new(Role.Button, "Close " + current.Info.Title, key, "/windows/" + key), generation, null, key, opened.Context?.FrameId, opened.Context?.Time);
            };
            opened.QuitCloseRequested = complete =>
            {
                opened.CloseRequested?.Invoke();
                app.Dispatch(() =>
                {
                    try
                    {
                        if (!disposed)
                        {
                            ProcessMessages();
                            ReconcileWindows();
                        }
                        complete(disposed || !desiredWindows.Any(w => w.Info.Key == key));
                    }
                    catch
                    {
                        complete(false);
                        throw;
                    }
                });
            };
            opened.Closed += () =>
            {
                nativeWindows.Remove(key);
                if (opened.Context is { } context)
                    ((IProgram)this).ForgetContext(context);
            };
        }
        foreach (var key in nativeWindows.Keys.Except(desiredWindows.Select(w => w.Info.Key)).ToArray())
            nativeWindows[key].Dispose();
        if (becameEmpty && app.Options.QuitOnLastWindow)
            app.Quit();
    }

    void DisposeWindows()
    {
        if (nativeApp is { } app)
            app.Disposing -= Dispose;
        foreach (var opened in nativeWindows.Values.ToArray())
            opened.Dispose();
        nativeWindows.Clear();
        nativeApp = null;
        contextWindows.Clear();
    }
}
