using System.Runtime.InteropServices;

namespace Kumiko.Platform;
/// <summary>A message-only window. It never maps, activates or accesses screen contents.</summary>
internal sealed unsafe class WindowsMessageWindow : IDisposable
{
    static readonly Dictionary<nint, WindowsMessageWindow> instances = new();
    static ushort registered;
    readonly App app;
    readonly Func<uint, nuint, nint, nint?> callback;
    internal nint Handle
    {
        get;
    }

    internal WindowsMessageWindow(App app, Func<uint, nuint, nint, nint?> callback, bool topLevel = false)
    {
        app.CheckThread();
        this.app = app;
        this.callback = callback;
        if (registered == 0)
        {
            var cls = new Win32.WindowClass
            {
                ClassName = "Kumiko.ShellMessages",
                Procedure = &Procedure,
                Instance = Win32.GetModuleHandleW(null)
            };
            registered = Win32.RegisterClassW(ref cls);
            if (registered == 0)
                throw new InvalidOperationException("Shell message class registration failed.");
        }

        Handle = Win32.CreateWindowExW(0, "Kumiko.ShellMessages", "", 0, 0, 0, 0, 0, topLevel ? 0 : -3, 0, Win32.GetModuleHandleW(null), 0);
        if (Handle == 0)
            throw new InvalidOperationException("Shell message window creation failed.");
        instances.Add(Handle, this);
    }

    [UnmanagedCallersOnly]
    static nint Procedure(nint handle, uint message, nuint w, nint l)
    {
        if (instances.TryGetValue(handle, out var instance))
            try
            {
                if (instance.callback(message, w, l) is { } result)
                    return result;
            }
            catch (Exception ex)
            {
                instance.app.Report(ex);
                return 0;
            }

        return Win32.DefWindowProcW(handle, message, w, l);
    }

    public void Dispose()
    {
        app.CheckThread();
        if (instances.Remove(Handle))
            Win32.DestroyWindow(Handle);
    }
}
