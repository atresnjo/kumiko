using System.Runtime.InteropServices;
using Kumiko.Platform;

namespace Kumiko.Desktop;

internal sealed unsafe class WindowsPowerBackend(App app) : IPowerSessionBackend
{
    WindowsMessageWindow? window;
    nint batteryNotification;
    nint sourceNotification;
    bool sessionRegistered;
    public PowerCapabilities Capabilities => PowerCapabilities.SystemKeepAwake | PowerCapabilities.DisplayKeepAwake |
        PowerCapabilities.SuspendResume | PowerCapabilities.LockUnlock | PowerCapabilities.Battery | PowerCapabilities.IdleTime;
    public PowerStatus Read()
    {
        if (GetSystemPowerStatus(out var value) == 0)
            return new();
        bool? present = value.BatteryFlags == 255 ? null : (value.BatteryFlags & 128) == 0;
        return new(present, value.AC == 255 ? null : value.AC == 0,
            present == true && value.Percent <= 100 ? value.Percent : null,
            present == true && value.BatteryFlags != 255 ? (value.BatteryFlags & 8) != 0 : null);
    }
    public TimeSpan? IdleTime
    {
        get
        {
            var info = new LastInput { Size = 8 };
            return GetLastInputInfo(ref info) == 0 ? null : TimeSpan.FromMilliseconds(unchecked((uint)Environment.TickCount - info.Tick));
        }
    }
    public void Start(Action<PowerStatus> changed, Action<SessionChange> session)
    {
        window = new(app, (message, w, l) =>
        {
            if (message == 0x218)
            {
                if (w == 4)
                    session(SessionChange.Suspending);
                else if (w == 18)
                    session(SessionChange.Resumed);
                else if (w == 0x8013 || w == 10)
                    changed(Read());
                return 1;
            }
            if (message == 0x2B1)
            {
                if (w == 7)
                    session(SessionChange.Locked);
                else if (w == 8)
                    session(SessionChange.Unlocked);
                return 0;
            }
            return null;
        }, topLevel: true);
        sessionRegistered = WTSRegisterSessionNotification(window.Handle, 0) != 0;
        Guid battery = new("a7ad8041-b45a-4cae-87a3-eecbb468a9e1");
        Guid source = new("5d3e9a59-e9d5-4b00-a6bd-ff34ff516548");
        batteryNotification = RegisterPowerSettingNotification(window.Handle, ref battery, 0);
        sourceNotification = RegisterPowerSettingNotification(window.Handle, ref source, 0);
        if (!sessionRegistered || batteryNotification == 0 || sourceNotification == 0)
            throw new PlatformNotSupportedException("Windows power/session notification registration failed.");
    }
    public IDisposable KeepAwake(KeepAwakeKind kind, string reason)
    {
        fixed (char* text = reason)
        {
            var context = new Reason { Version = 0, Flags = 1, Simple = (nint)text };
            nint request = PowerCreateRequest(ref context);
            if (request == -1 || request == 0)
                throw new InvalidOperationException("Creating the native power request failed.");
            int type = kind == KeepAwakeKind.Display ? 0 : 1;
            if (PowerSetRequest(request, type) == 0)
            {
                CloseHandle(request);
                throw new InvalidOperationException("The native power request was denied.");
            }
            return new Request(request, type);
        }
    }
    sealed class Request(nint handle, int type) : IDisposable
    {
        bool disposed;
        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            PowerClearRequest(handle, type);
            CloseHandle(handle);
        }
    }
    public void Dispose()
    {
        if (batteryNotification != 0)
            UnregisterPowerSettingNotification(batteryNotification);
        if (sourceNotification != 0)
            UnregisterPowerSettingNotification(sourceNotification);
        batteryNotification = 0;
        sourceNotification = 0;
        if (sessionRegistered && window is not null)
            WTSUnRegisterSessionNotification(window.Handle);
        sessionRegistered = false;
        window?.Dispose();
        window = null;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct SystemPower
    {
        public byte AC;
        public byte BatteryFlags;
        public byte Percent;
        public byte Saver;
        public uint Lifetime;
        public uint FullLifetime;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct LastInput
    {
        public uint Size;
        public uint Tick;
    }
    // REASON_CONTEXT detailed union determines the full x86/x64 layout; Simple aliases its first pointer.
    [StructLayout(LayoutKind.Sequential)]
    struct Reason
    {
        public uint Version;
        public uint Flags;
        public nint Simple;
        public uint ReservedId;
        public uint ReservedCount;
        public nint ReservedStrings;
    }
    [DllImport("kernel32.dll")]
    static extern int GetSystemPowerStatus(out SystemPower value);
    [DllImport("user32.dll")]
    static extern int GetLastInputInfo(ref LastInput value);
    [DllImport("wtsapi32.dll")]
    static extern int WTSRegisterSessionNotification(nint window, uint flags);
    [DllImport("wtsapi32.dll")]
    static extern int WTSUnRegisterSessionNotification(nint window);
    [DllImport("user32.dll")]
    static extern nint RegisterPowerSettingNotification(nint window, ref Guid setting, uint flags);
    [DllImport("user32.dll")]
    static extern int UnregisterPowerSettingNotification(nint handle);
    [DllImport("kernel32.dll")]
    static extern nint PowerCreateRequest(ref Reason reason);
    [DllImport("kernel32.dll")]
    static extern int PowerSetRequest(nint handle, int type);
    [DllImport("kernel32.dll")]
    static extern int PowerClearRequest(nint handle, int type);
    [DllImport("kernel32.dll")]
    static extern int CloseHandle(nint handle);
}
