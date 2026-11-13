using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Kumiko.Platform;
using Kumiko.Rendering;
using Kumiko.UI;

namespace Kumiko.Desktop;

internal sealed unsafe class WindowsTrayBackend : ITrayBackend
{
    readonly WindowsMessageWindow host;
    readonly Action<bool> click;
    readonly Guid id;
    NotifyData data;
    nint icon;
    bool installed;
    internal WindowsTrayBackend(App app, Action<bool> click, string id)
    {
        this.click = click;
        this.id = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(app.Options.Name + "\0" + Environment.ProcessPath + "\0" + id)).AsSpan(0, 16));
        host = new(app, Message, topLevel: true);
    }

    nint? Message(uint message, nuint w, nint l)
    {
        if (message != 0x8002)
            return null;
        uint notification = (uint)((long)l & 65535);
        if (notification is 0x202 or 0x205)
            click(notification == 0x205);
        return 0;
    }

    public Rect Bounds
    {
        get
        {
            var identifier = new IconIdentifier
            {
                Size = (uint)sizeof(IconIdentifier),
                Window = host.Handle,
                Id = 1,
                Guid = id
            };
            Marshal.ThrowExceptionForHR(Shell_NotifyIconGetRect(ref identifier, out var rect));
            return new(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
        }
    }

    public void Update(TrayOptions options)
    {
        if (options.Title.Length > 0)
            throw new PlatformNotSupportedException("Windows tray icons do not display titles; use ToolTip.");
        if (options.Icon is null)
            throw new ArgumentException("Windows tray icons require a PNG.");
        var next = CreateIcon(options.Icon2x ?? options.Icon);
        var previous = icon;
        icon = next;
        data.Size = (uint)sizeof(NotifyData);
        data.Window = host.Handle;
        data.Id = 1;
        data.Flags = 1 | 2 | 4 | 0x20;
        data.Callback = 0x8002;
        data.Icon = icon;
        data.Guid = id;
        fixed (char* tip = data.ToolTip)
        {
            new Span<char>(tip, 128).Clear();
            options.ToolTip.AsSpan(0, Math.Min(options.ToolTip.Length, 127)).CopyTo(new Span<char>(tip, 128));
        }

        if (Shell_NotifyIconW(installed ? 1u : 0u, ref data) == 0)
        {
            DestroyIcon(next);
            icon = previous;
            throw new IOException("Shell_NotifyIcon failed; the Windows notification area may be unavailable.");
        }

        installed = true;
        if (previous != 0)
            DestroyIcon(previous);
    }

    static nint CreateIcon(byte[] png)
    {
        var pixels = Png.Read(png);
        int width = pixels.Width, height = pixels.Height;
        if (width > 512 || height > 512)
            throw new ArgumentException("Tray PNG dimensions must not exceed 512 pixels.");
        var header = new BitmapHeader
        {
            Size = 40,
            Width = width,
            Height = -height,
            Planes = 1,
            Bits = 32
        };
        var bitmap = CreateDIBSection(0, ref header, 0, out var target, 0, 0);
        if (bitmap == 0)
            throw new IOException("CreateDIBSection failed for the tray icon.");
        nint mask = 0;
        try
        {
            // Kumiko pixels already contain premultiplied BGRA in top-down order.
            Marshal.Copy(pixels.Bytes, 0, target, pixels.Bytes.Length);
            var maskBytes = new byte[((width + 15) / 16) * 2 * height];
            fixed (byte* p = maskBytes)
                mask = CreateBitmap(width, height, 1, 1, (nint)p);
            var info = new IconInfo
            {
                IsIcon = 1,
                Mask = mask,
                Color = bitmap
            };
            var icon = CreateIconIndirect(ref info);
            if (icon == 0)
                throw new IOException("CreateIconIndirect failed.");
            return icon;
        }
        finally
        {
            if (mask != 0)
                DeleteObject(mask);
            DeleteObject(bitmap);
        }
    }

    public void PopUpMenu(Menu menu)
    {
        GetCursorPos(out var point);
        SetForegroundWindow(host.Handle);
        menu.PopupWindows(host.Handle, point.X, point.Y);
    }

    public void Dispose()
    {
        if (installed)
            Shell_NotifyIconW(2, ref data);
        installed = false;
        if (icon != 0)
            DestroyIcon(icon);
        icon = 0;
        host.Dispose();
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct NotifyData
    {
        public uint Size;
        public nint Window;
        public uint Id, Flags, Callback;
        public nint Icon;
        public fixed char ToolTip[128];
        public uint State, StateMask;
        public fixed char Info[256];
        public uint Timeout;
        public fixed char InfoTitle[64];
        public uint InfoFlags;
        public Guid Guid;
        public nint BalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct IconIdentifier
    {
        public uint Size;
        public nint Window;
        public uint Id;
        public Guid Guid;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct PointNative
    {
        public int X, Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct IconInfo
    {
        public int IsIcon;
        public uint X, Y;
        public nint Mask, Color;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct BitmapHeader
    {
        public uint Size;
        public int Width, Height;
        public ushort Planes, Bits;
        public uint Compression, ImageSize;
        public int X, Y;
        public uint Used, Important;
    }

    [DllImport("shell32.dll")]
    internal static extern int Shell_NotifyIconW(uint message, ref NotifyData data);
    [DllImport("shell32.dll")]
    static extern int Shell_NotifyIconGetRect(ref IconIdentifier identifier, out Win32.NativeRectangle rect);
    [DllImport("user32.dll")]
    static extern int GetCursorPos(out PointNative point);
    [DllImport("user32.dll")]
    static extern int SetForegroundWindow(nint window);
    [DllImport("user32.dll")]
    static extern nint CreateIconIndirect(ref IconInfo info);
    [DllImport("user32.dll")]
    static extern int DestroyIcon(nint icon);
    [DllImport("gdi32.dll")]
    static extern nint CreateDIBSection(nint dc, ref BitmapHeader header, uint usage, out nint bits, nint section, uint offset);
    [DllImport("gdi32.dll")]
    static extern nint CreateBitmap(int width, int height, uint planes, uint bits, nint data);
    [DllImport("gdi32.dll")]
    static extern int DeleteObject(nint obj);
}
