using System.Runtime.InteropServices;
using System.Text;
using Kumiko.Platform;

namespace Kumiko.Desktop;

internal sealed unsafe class WindowsClipboardBackend : IClipboardBackend
{
    readonly App app;
    readonly WindowsMessageWindow host;
    readonly Dictionary<uint, string> offered = new();
    TransferSnapshot? snapshot;
    bool clearing;
    internal WindowsClipboardBackend(App app)
    {
        this.app = app;
        host = new(app, Message);
    }

    static uint Native(string format) => format switch
    {
        TransferFormats.Text => 13,
        TransferFormats.FileList => 15,
        TransferFormats.HTML => RegisterClipboardFormatW("HTML Format"),
        TransferFormats.PNG => RegisterClipboardFormatW("PNG"),
        _ => RegisterClipboardFormatW(format)
    };
    static string? Portable(uint format)
    {
        if (format == 13)
            return TransferFormats.Text;
        if (format == 15)
            return TransferFormats.FileList;
        var buffer = new char[512];
        int length;
        fixed (char* text = buffer)
            length = GetClipboardFormatNameW(format, text, buffer.Length);
        var name = new string(buffer, 0, length);
        return name switch
        {
            "HTML Format" => TransferFormats.HTML,
            "PNG" => TransferFormats.PNG,
            _ => name.Contains('/') ? name : null
        };
    }

    public IReadOnlyList<string> Formats
    {
        get
        {
            app.CheckThread();
            Open();
            try
            {
                var result = new List<string>();
                uint format = 0;
                while ((format = EnumClipboardFormats(format)) != 0)
                    if (Portable(format) is { } name)
                        result.Add(name);
                return result.AsReadOnly();
            }
            finally
            {
                CloseClipboard();
            }
        }
    }

    void Open()
    {
        if (OpenClipboard(host.Handle) == 0)
            throw new IOException("The Windows clipboard is busy.");
    }

    public TransferData Read(string[] formats)
    {
        app.CheckThread();
        if (GetClipboardOwner() == host.Handle && snapshot is { } own)
            return own.Materialize(formats);
        var selected = formats.Length == 0 ? Formats : formats;
        Open();
        try
        {
            var result = new List<TransferRepresentation>();
            foreach (var format in selected)
            {
                uint native = Native(format);
                if (IsClipboardFormatAvailable(native) == 0)
                    continue;
                var handle = GetClipboardData(native);
                if (handle == 0)
                    throw new IOException($"The clipboard owner did not render {format}.");
                nuint length = GlobalSize(handle);
                if (length > TransferSnapshot.MaximumForeignBytes)
                    throw new IOException("Foreign clipboard representation exceeds 64 MiB.");
                var pointer = GlobalLock(handle);
                if (pointer == 0 && length > 0)
                    throw new IOException("Could not lock clipboard data.");
                try
                {
                    var bytes = new byte[(int)length];
                    if (bytes.Length > 0)
                        Marshal.Copy(pointer, bytes, 0, bytes.Length);
                    if (native == 13)
                        bytes = Encoding.UTF8.GetBytes(Encoding.Unicode.GetString(bytes).TrimEnd('\0'));
                    else if (native == 15)
                    {
                        if (bytes.Length < 20 || BitConverter.ToInt32(bytes, 16) == 0)
                            throw new IOException("Unsupported CF_HDROP encoding.");
                        int offset = BitConverter.ToInt32(bytes);
                        if (offset < 20 || offset > bytes.Length)
                            throw new IOException("Invalid CF_HDROP offset.");
                        var paths = Encoding.Unicode.GetString(bytes, offset, bytes.Length - offset).Split('\0', StringSplitOptions.RemoveEmptyEntries);
                        bytes = Encoding.UTF8.GetBytes(string.Join("\r\n", paths.Select(p => new Uri(p).AbsoluteUri)));
                    }
                    else if (format == TransferFormats.HTML)
                        bytes = DecodeHtml(bytes);
                    result.Add(new(format, bytes));
                }
                finally
                {
                    if (pointer != 0)
                        GlobalUnlock(handle);
                }
            }

            if (formats.Length > 0 && result.Count == 0)
                throw new KeyNotFoundException("None of the requested clipboard formats are available.");
            return result.Count == 0 ? new() : new(new TransferItem(result.ToArray()));
        }
        finally
        {
            CloseClipboard();
        }
    }

    public void Write(TransferSnapshot next)
    {
        Open();
        try
        {
            clearing = true;
            if (EmptyClipboard() == 0)
                throw new IOException("Could not empty the clipboard.");
            offered.Clear();
            snapshot = next;
            foreach (var format in next.Data.Formats)
            {
                uint native = Native(format);
                if (native == 0)
                    throw new IOException($"Could not register clipboard format {format}.");
                offered[native] = format;
                SetClipboardData(native, 0); // Delayed rendering deliberately returns NULL.
            }
        }
        finally
        {
            clearing = false;
            CloseClipboard();
        }
    }

    nint? Message(uint message, nuint w, nint l)
    {
        if (message == 0x305)
        {
            Render((uint)w);
            return 0;
        }

        if (message == 0x306)
        {
            Flush();
            return 0;
        }

        if (message == 0x307 && !clearing)
        {
            snapshot?.Dispose();
            snapshot = null;
            offered.Clear();
            return 0;
        }

        return null;
    }

    void Render(uint native)
    {
        if (snapshot is null || !offered.TryGetValue(native, out var format))
            return;
        var bytes = snapshot.Read(format);
        if (native == 13)
            bytes = Encoding.Unicode.GetBytes(Encoding.UTF8.GetString(bytes) + "\0");
        else if (native == 15)
        {
            var paths = Encoding.UTF8.GetString(bytes).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Select(s => new Uri(s)).Where(u => u.IsFile && (u.Host.Length == 0 || u.IsLoopback)).Select(u => u.LocalPath);
            var list = Encoding.Unicode.GetBytes(string.Join('\0', paths) + "\0\0");
            bytes = new byte[20 + list.Length];
            BitConverter.GetBytes(20).CopyTo(bytes, 0);
            BitConverter.GetBytes(1).CopyTo(bytes, 16);
            list.CopyTo(bytes, 20);
        }
        else if (format == TransferFormats.HTML)
            bytes = EncodeHtml(bytes);
        var handle = GlobalAlloc(0x42, (nuint)Math.Max(1, bytes.Length));
        if (handle == 0)
            throw new OutOfMemoryException();
        var pointer = GlobalLock(handle);
        if (pointer == 0)
        {
            GlobalFree(handle);
            throw new IOException("GlobalLock failed.");
        }

        Marshal.Copy(bytes, 0, pointer, bytes.Length);
        GlobalUnlock(handle);
        if (SetClipboardData(native, handle) == 0)
        {
            GlobalFree(handle);
            throw new IOException("SetClipboardData failed.");
        }
    }

    internal static byte[] EncodeHtml(byte[] html)
    {
        const string header = "Version:0.9\r\nStartHTML:{0:0000000000}\r\nEndHTML:{1:0000000000}\r\nStartFragment:{2:0000000000}\r\nEndFragment:{3:0000000000}\r\n";
        const string prefix = "<html><body><!--StartFragment-->";
        const string suffix = "<!--EndFragment--></body></html>";
        int start = string.Format(System.Globalization.CultureInfo.InvariantCulture, header, 0, 0, 0, 0).Length;
        var text = string.Format(System.Globalization.CultureInfo.InvariantCulture, header, start, start + prefix.Length + html.Length + suffix.Length, start + prefix.Length, start + prefix.Length + html.Length);
        return Encoding.UTF8.GetBytes(text + prefix).Concat(html).Concat(Encoding.UTF8.GetBytes(suffix + "\0")).ToArray();
    }

    static byte[] DecodeHtml(byte[] bytes)
    {
        var header = Encoding.ASCII.GetString(bytes.AsSpan(0, Math.Min(1024, bytes.Length)));
        int Offset(string name)
        {
            int at = header.IndexOf(name, StringComparison.Ordinal);
            if (at < 0 || !int.TryParse(header.AsSpan(at + name.Length, 10), out var value))
                throw new IOException("Invalid CF_HTML header.");
            return value;
        }

        int start = Offset("StartFragment:"), end = Offset("EndFragment:");
        if (start < 0 || end < start || end > bytes.Length)
            throw new IOException("Invalid CF_HTML fragment offsets.");
        return bytes.AsSpan(start, end - start).ToArray();
    }

    public void Clear()
    {
        Open();
        try
        {
            if (EmptyClipboard() == 0)
                throw new IOException("Could not clear clipboard.");
        }
        finally
        {
            CloseClipboard();
        }

        snapshot = null;
        offered.Clear();
    }

    public void Flush()
    {
        app.CheckThread();
        Open();
        try
        {
            if (GetClipboardOwner() != host.Handle)
                return;
            foreach (var format in offered.Keys.ToArray())
                Render(format);
        }
        finally
        {
            CloseClipboard();
        }
    }

    public void Dispose()
    {
        Flush();
        host.Dispose();
    }

    [DllImport("user32.dll")]
    static extern int OpenClipboard(nint owner);
    [DllImport("user32.dll")]
    static extern int CloseClipboard();
    [DllImport("user32.dll")]
    static extern int EmptyClipboard();
    [DllImport("user32.dll")]
    static extern nint GetClipboardOwner();
    [DllImport("user32.dll")]
    static extern uint EnumClipboardFormats(uint format);
    [DllImport("user32.dll")]
    static extern int IsClipboardFormatAvailable(uint format);
    [DllImport("user32.dll")]
    static extern nint GetClipboardData(uint format);
    [DllImport("user32.dll")]
    static extern nint SetClipboardData(uint format, nint data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern uint RegisterClipboardFormatW(string format);
    [DllImport("user32.dll")]
    static extern int GetClipboardFormatNameW(uint format, char* name, int length);
    [DllImport("kernel32.dll")]
    static extern nint GlobalAlloc(uint flags, nuint size);
    [DllImport("kernel32.dll")]
    static extern nint GlobalLock(nint handle);
    [DllImport("kernel32.dll")]
    static extern int GlobalUnlock(nint handle);
    [DllImport("kernel32.dll")]
    static extern nint GlobalFree(nint handle);
    [DllImport("kernel32.dll")]
    static extern nuint GlobalSize(nint handle);
}
