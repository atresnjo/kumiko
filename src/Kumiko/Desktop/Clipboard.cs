using System.Text;

namespace Kumiko.Desktop;

internal interface IClipboardBackend : IDisposable
{
    IReadOnlyList<string> Formats
    {
        get;
    }

    TransferData Read(string[] formats);
    void Write(TransferSnapshot snapshot);
    void Clear();
    void Flush();
}

public static class Clipboard
{
    [ThreadStatic]
    static ClipboardScope? current;
    static ClipboardScope Current => current ?? throw new InvalidOperationException("Clipboard requires an App on the UI thread.");

    internal static IDisposable Attach(App app, IClipboardBackend? backend = null)
    {
        var scope = new ClipboardScope(app, backend, current);
        current = scope;
        // An injected backend never outlives its App, even when the caller forgets the scope.
        if (backend is not null)
            app.Disposing += scope.Dispose;
        return scope;
    }

    public static IReadOnlyList<string> Formats => Current.Backend.Formats;

    public static TransferData Read(params string[] formats) => Current.Read(formats);
    public static byte[] ReadFormat(string format)
    {
        using var snapshot = new TransferSnapshot(Current.App, Read(format));
        return snapshot.Read(format);
    }

    public static string ReadText() => Formats.Contains(TransferFormats.Text) ? Encoding.UTF8.GetString(ReadFormat(TransferFormats.Text)) : "";
    public static void Write(TransferData data, Action? onRelease = null) => Current.Write(data, onRelease);
    public static void WriteText(string text) => Write(TransferData.Text(text));
    public static void Clear() => Current.Clear();
    public static void Flush() => Current.Backend.Flush();
    public static void WriteFiles(params string[] paths) => Write(TransferData.Files(paths));
    public static IReadOnlyList<string> ReadFiles()
    {
        var format = Formats.Contains(TransferFormats.FileList) ? TransferFormats.FileList : TransferFormats.URIList;
        if (!Formats.Contains(format))
            return Array.Empty<string>();
        return Array.AsReadOnly(Encoding.UTF8.GetString(ReadFormat(format)).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Where(line => !line.StartsWith('#')).Select(line => Uri.TryCreate(line, UriKind.Absolute, out var uri) && uri.IsFile && (uri.Host.Length == 0 || uri.IsLoopback) ? uri.LocalPath : null).OfType<string>().ToArray());
    }

    sealed class ClipboardScope(App app, IClipboardBackend? backend, ClipboardScope? previous) : IDisposable
    {
        IClipboardBackend? native = backend;
        readonly ClipboardScope? before = previous;
        TransferSnapshot? snapshot;
        bool disposed;
        internal App App => app;

        internal IClipboardBackend Backend
        {
            get
            {
                Check();
                return native ??= app.Options.Hidden ? new MemoryClipboardBackend(app) : OperatingSystem.IsMacOS() ? new MacClipboardBackend(app) : OperatingSystem.IsWindows() ? new WindowsClipboardBackend(app) : new LinuxClipboardBackend(app);
            }
        }

        void Check()
        {
            app.CheckThread();
            ObjectDisposedException.ThrowIf(disposed, this);
            if (snapshot?.Fetching == true)
                throw new InvalidOperationException("Clipboard providers cannot reenter clipboard operations.");
        }

        internal TransferData Read(string[] formats)
        {
            Check();
            return Backend.Read(formats);
        }

        internal void Write(TransferData data, Action? release)
        {
            Check();
            ArgumentNullException.ThrowIfNull(data);
            var next = new TransferSnapshot(app, data, release);
            try
            {
                Backend.Write(next);
            }
            catch
            {
                next.Dispose();
                throw;
            }

            snapshot?.Dispose();
            snapshot = next;
        }

        internal void Clear()
        {
            Check();
            Backend.Clear();
            snapshot?.Dispose();
            snapshot = null;
        }

        /// <summary>Releases the native backend, then the snapshot, then restores the nearest live previous scope.
        /// Provider and release failures are reported instead of interrupting App teardown.</summary>
        public void Dispose()
        {
            app.CheckThread();
            if (disposed)
                return;
            if (snapshot?.Fetching == true)
                throw new InvalidOperationException("Clipboard providers cannot dispose their clipboard scope.");
            disposed = true;
            app.Disposing -= Dispose;
            try
            {
                native?.Dispose();
            }
            catch (Exception ex)
            {
                app.Report(ex);
            }

            try
            {
                snapshot?.Dispose();
            }
            catch (Exception ex)
            {
                app.Report(ex);
            }
            finally
            {
                snapshot = null;
                if (current == this)
                    current = Live(before);
            }
        }

        static ClipboardScope? Live(ClipboardScope? scope)
        {
            while (scope is { disposed: true })
                scope = scope.before;
            return scope;
        }
    }
}

internal sealed class MemoryClipboardBackend(App app) : IClipboardBackend
{
    TransferSnapshot? snapshot;
    public IReadOnlyList<string> Formats => snapshot?.Data.Formats ?? Array.Empty<string>();

    public TransferData Read(string[] formats) => snapshot?.Materialize(formats) ?? (formats.Length == 0 ? new() : throw new KeyNotFoundException("None of the requested clipboard formats are available."));
    public void Write(TransferSnapshot snapshot) => this.snapshot = snapshot;
    public void Clear() => snapshot = null;
    public void Flush()
    {
        app.CheckThread();
    }

    public void Dispose() => snapshot = null;
}
