namespace Kumiko.Desktop;

internal readonly record struct ShortcutChord(string Key, bool Control, bool Alt, bool Shift, bool Meta)
{
    internal static ShortcutChord Parse(string accelerator)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accelerator);
        bool control = false, alt = false, shift = false, meta = false;
        string? key = null;
        foreach (var token in accelerator.Split('+'))
        {
            switch (token.Trim().ToLowerInvariant())
            {
                case "primary":
                case "cmdorctrl":
                case "commandorcontrol":
                    if (OperatingSystem.IsMacOS())
                        meta = true;
                    else
                        control = true;
                    break;
                case "cmd":
                case "command":
                case "meta":
                case "super":
                    meta = true;
                    break;
                case "control":
                case "ctrl":
                    control = true;
                    break;
                case "alt":
                case "option":
                    alt = true;
                    break;
                case "shift":
                    shift = true;
                    break;
                default:
                    if (key is not null || token.Length == 0)
                        throw new ArgumentException("A shortcut must contain exactly one key.", nameof(accelerator));
                    key = token.Trim().ToUpperInvariant();
                    break;
            }
        }

        if (key is null || (!control && !alt && !meta))
            throw new ArgumentException("A global shortcut needs a key and Control, Alt or Meta.", nameof(accelerator));
        return new(key, control, alt, shift, meta);
    }
}

internal interface IGlobalShortcutBackend : IDisposable
{
    bool Register(ShortcutChord chord, Action callback);
    void Unregister(ShortcutChord chord);
}

/// <summary>Owns system-wide registrations. False means another registration already owns the chord.</summary>
public sealed class GlobalShortcut : IDisposable
{
    readonly App app;
    readonly IGlobalShortcutBackend backend;
    readonly Dictionary<ShortcutChord, Action> registrations = new();
    bool disposed;
    public GlobalShortcut(App app) : this(app, null)
    {
    }

    internal GlobalShortcut(App app, IGlobalShortcutBackend? backend)
    {
        app.CheckThread();
        this.app = app;
        if (backend is null && app.Options.Hidden)
            throw new InvalidOperationException("Hidden apps require an injected global shortcut backend.");
        this.backend = backend ?? (OperatingSystem.IsMacOS() ? new MacGlobalShortcutBackend(app) : OperatingSystem.IsWindows() ? new WindowsGlobalShortcutBackend(app) : new X11GlobalShortcutBackend(app));
        app.Disposing += Dispose;
    }

    void Check()
    {
        app.CheckThread();
        ObjectDisposedException.ThrowIf(disposed, this);
    }

    public bool Register(string accelerator, Action callback)
    {
        Check();
        ArgumentNullException.ThrowIfNull(callback);
        var chord = ShortcutChord.Parse(accelerator);
        if (registrations.ContainsKey(chord))
            return false;
        Action? guarded = null;
        guarded = () =>
        {
            if (!disposed && registrations.TryGetValue(chord, out var current) && ReferenceEquals(current, guarded))
                callback();
        };
        if (!backend.Register(chord, () =>
        {
            try
            {
                app.Dispatch(guarded);
            }
            catch (ObjectDisposedException)
            {
            }
        }))
            return false;
        if (disposed)
        {
            backend.Unregister(chord);
            return false;
        }
        registrations.Add(chord, guarded);
        return true;
    }

    public bool IsRegistered(string accelerator)
    {
        Check();
        return registrations.ContainsKey(ShortcutChord.Parse(accelerator));
    }

    public void Unregister(string accelerator)
    {
        Check();
        var chord = ShortcutChord.Parse(accelerator);
        if (registrations.Remove(chord))
            backend.Unregister(chord);
    }

    public void Dispose()
    {
        app.CheckThread();
        if (disposed)
            return;
        disposed = true;
        app.Disposing -= Dispose;
        registrations.Clear();
        backend.Dispose();
    }
}
