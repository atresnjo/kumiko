using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Kumiko.Platform;
using O = Kumiko.Platform.ObjC;

namespace Kumiko.Desktop;

internal interface ITrayAppearance
{
    bool IsDark
    {
        get;
    }
    event Action<bool>? AppearanceChanged;
}

/// <summary>Observes one owned AppKit appearance source, independently of windows.</summary>
internal sealed unsafe class NativeAppearance : IDisposable
{
    static readonly ConcurrentDictionary<nint, NativeAppearance> observers = new();
    readonly App app;
    readonly nint source, observer;
    readonly int thread;
    bool disposed;
    internal bool IsDark
    {
        get; private set;
    }
    internal event Action<bool>? Changed;

    internal NativeAppearance(App app, nint source)
    {
        app.CheckThread();
        this.app = app;
        thread = Environment.CurrentManagedThreadId;
        using var pool = new AutoreleasePool();
        this.source = O.O(source, "retain");
        var cls = O.Define("KumikoAppearanceObserver", "NSObject",
            ("observeValueForKeyPath:ofObject:change:context:", (nint)(delegate* unmanaged<nint, nint, nint, nint, nint, nint, void>)&Observe, "v@:@@@^v"));
        observer = O.O(cls, "new");
        observers[observer] = this;
        try
        {
            IsDark = Read();
            O.V(source, "addObserver:forKeyPath:options:context:", observer, O.TempString("effectiveAppearance"), (nint)0, observer);
        }
        catch
        {
            observers.TryRemove(observer, out _);
            O.Release(observer);
            O.Release(this.source);
            throw;
        }
    }

    bool Read()
    {
        nint* values = stackalloc nint[2];
        values[0] = O.TempString("NSAppearanceNameAqua");
        values[1] = O.TempString("NSAppearanceNameDarkAqua");
        var names = O.O(O.Class("NSArray"), "arrayWithObjects:count:", (nint)values, (nuint)2);
        return O.Text(O.O(O.O(source, "effectiveAppearance"), "bestMatchFromAppearancesWithNames:", names)) == "NSAppearanceNameDarkAqua";
    }

    [UnmanagedCallersOnly]
    static void Observe(nint self, nint cmd, nint key, nint source, nint change, nint context)
    {
        if (context != self || !observers.TryGetValue(self, out var owner))
            return;
        try
        {
            if (Environment.CurrentManagedThreadId == owner.thread)
                owner.Sample();
            else
                owner.app.Dispatch(owner.Sample);
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
        }
    }

    void Sample()
    {
        app.CheckThread();
        if (disposed)
            return;
        using var pool = new AutoreleasePool();
        bool dark = Read();
        if (dark == IsDark)
            return;
        IsDark = dark;
        app.Dispatch(() =>
        {
            if (!disposed)
                Changed?.Invoke(dark);
        });
    }

    public void Dispose()
    {
        app.CheckThread();
        if (disposed)
            return;
        disposed = true;
        using var pool = new AutoreleasePool();
        O.V(source, "removeObserver:forKeyPath:context:", observer, O.TempString("effectiveAppearance"), observer);
        observers.TryRemove(observer, out _);
        Changed = null;
        O.Release(observer);
        O.Release(source);
    }
}
