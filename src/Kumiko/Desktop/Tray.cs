using Kumiko.UI;

namespace Kumiko.Desktop;

public sealed record TrayOptions(string Id, byte[]? Icon = null, byte[]? Icon2x = null, bool Template = false, string ToolTip = "", string Title = "", Menu? Menu = null);
internal interface ITrayBackend : IDisposable
{
    Rect Bounds
    {
        get;
    }

    void Update(TrayOptions options);
    void PopUpMenu(Menu menu);
}

/// <summary>One explicitly owned status item. Dispose removes its callbacks and native resources.</summary>
public sealed class Tray : IDisposable
{
    static readonly Dictionary<App, HashSet<string>> identities = new();
    readonly App app;
    readonly ITrayBackend backend;
    TrayOptions options;
    bool disposed;
    public string Id => options.Id;

    public bool IsDark
    {
        get
        {
            Check();
            return backend is ITrayAppearance appearance ? appearance.IsDark : Appearance.IsDark;
        }
    }

    public Rect Bounds
    {
        get
        {
            Check();
            return backend.Bounds;
        }
    }

    public event Action? Click;
    public event Action? RightClick;
    public event Action<bool>? AppearanceChanged;
    public Tray(string title, Menu? menu = null, string tooltip = "") : this(App.Current, new("legacy-" + Guid.NewGuid().ToString("N"), ToolTip: tooltip, Title: title, Menu: menu))
    {
    }

    public Tray(App app, TrayOptions options) : this(app, options, null)
    {
    }

    internal Tray(App app, TrayOptions options, Func<Action<bool>, ITrayBackend>? factory)
    {
        app.CheckThread();
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Id);
        if (factory is null && app.Options.Hidden)
            throw new InvalidOperationException("Hidden apps require an injected tray backend.");
        this.app = app;
        options.Menu?.CheckOwner(app);
        this.options = Freeze(options);
        if (!identities.TryGetValue(app, out var ids))
            identities.Add(app, ids = new());
        if (!ids.Add(options.Id))
            throw new ArgumentException("Tray IDs must be unique within an App.", nameof(options));
        ITrayBackend? created = null;
        try
        {
            backend = created = factory is not null ? factory(NativeClick) : OperatingSystem.IsMacOS() ? new MacTrayBackend(app, NativeClick) : OperatingSystem.IsWindows() ? new WindowsTrayBackend(app, NativeClick, options.Id) : new LinuxTrayBackend(app, options);
            backend.Update(this.options);
        }
        catch
        {
            created?.Dispose();
            ids.Remove(options.Id);
            if (ids.Count == 0)
                identities.Remove(app);
            throw;
        }

        if (backend is ITrayAppearance appearance)
            appearance.AppearanceChanged += OnAppearance;
        else
            Appearance.Changed += OnAppearance;
        app.Disposing += Dispose;
    }

    static TrayOptions Freeze(TrayOptions options)
    {
        static byte[]? Png(byte[]? bytes)
        {
            if (bytes is null)
                return null;
            if (bytes.Length < 24 || !bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
                throw new ArgumentException("Tray icons must contain PNG data.");
            return (byte[])bytes.Clone();
        }

        return options with
        {
            Icon = Png(options.Icon),
            Icon2x = Png(options.Icon2x)
        };
    }

    void Check()
    {
        app.CheckThread();
        ObjectDisposedException.ThrowIf(disposed, this);
    }

    void NativeClick(bool right)
    {
        app.Dispatch(() =>
        {
            if (disposed)
                return;
            if (right)
                RightClick?.Invoke();
            else
                Click?.Invoke();
            if (options.Menu is { } menu && (right || Click is null))
            {
                menu.CheckOwner(app);
                backend.PopUpMenu(menu);
            }
        });
    }

    void OnAppearance(bool dark)
    {
        if (!disposed)
            AppearanceChanged?.Invoke(dark);
    }

    void Update(TrayOptions next)
    {
        Check();
        next.Menu?.CheckOwner(app);
        next = Freeze(next);
        backend.Update(next);
        options = next;
    }

    public void SetIcon(byte[] png, byte[]? png2x = null, bool template = false) => Update(options with { Icon = png, Icon2x = png2x, Template = template });
    public void SetMenu(Menu? menu) => Update(options with { Menu = menu });
    public void SetToolTip(string text) => Update(options with { ToolTip = text });
    public void SetTitle(string title) => Update(options with { Title = title });
    public void PopUpMenu()
    {
        Check();
        if (options.Menu is not { } menu)
            throw new InvalidOperationException("The tray has no menu.");
        menu.CheckOwner(app);
        backend.PopUpMenu(menu);
    }

    public void Dispose()
    {
        app.CheckThread();
        if (disposed)
            return;
        disposed = true;
        if (backend is ITrayAppearance appearance)
            appearance.AppearanceChanged -= OnAppearance;
        else
            Appearance.Changed -= OnAppearance;
        app.Disposing -= Dispose;
        backend.Dispose();
        if (identities.TryGetValue(app, out var ids) && ids.Remove(Id) && ids.Count == 0)
            identities.Remove(app);
    }
}
