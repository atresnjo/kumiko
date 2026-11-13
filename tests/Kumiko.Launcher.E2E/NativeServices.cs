using System.Runtime.InteropServices;
using Kumiko;
using Kumiko.Desktop;
using O = Kumiko.Platform.ObjC;

static unsafe partial class NativeFlows
{
    static void Startup(App app, string output, Action<bool, string> check)
    {
        var backend = new PrivateStartup();
        using (var startup = new StartupItem(app, "dev.fixture.launcher", "Fixture launcher", backend))
        {
            check(startup.Read().State == StartupState.Disabled && backend.Writes == 0, "public startup owner construction and reads are side-effect free");
            check(startup.SetEnabled(true).State == StartupState.Enabled && backend.Writes == 1, "public startup owner forwards explicit preference mutation");
        }
        var root = Path.Combine(output, "startup-fixtures");
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "dev.fixture.launcher.desktop");
        File.Delete(path);
        const string content = "[Desktop Entry]\nType=Application\nName=Fixture\nExec=/fixture/launcher --kumiko-login=dev.fixture.launcher\nX-Kumiko-Owner=dev.fixture.launcher\n";
        check(NativeStartupItem.ReadLinux(path, content).State == StartupState.Disabled && !File.Exists(path), "XDG startup read leaves temporary fixture storage untouched");
        NativeStartupItem.WriteLinux(path, content, true);
        check(NativeStartupItem.ReadLinux(path, content).State == StartupState.Enabled, "XDG startup writes and reads its exact owned desktop entry in temporary storage");
        NativeStartupItem.WriteLinux(path, content, false);
        check(!File.Exists(path), "XDG disable removes only its own temporary desktop entry");
        File.WriteAllText(path, "foreign content");
        var rejected = false;
        try
        {
            NativeStartupItem.WriteLinux(path, content, false);
        }
        catch (InvalidOperationException)
        {
            rejected = true;
        }
        check(rejected && File.ReadAllText(path) == "foreign content", "XDG startup refuses to remove another owner's entry");
        if (!OperatingSystem.IsWindows())
        {
            File.Delete(path);
            var target = Path.Combine(root, "missing-owner.desktop");
            File.CreateSymbolicLink(path, target);
            rejected = false;
            try
            {
                NativeStartupItem.WriteLinux(path, content, true);
            }
            catch (InvalidOperationException)
            {
                rejected = true;
            }
            check(rejected && new FileInfo(path).LinkTarget == target, "XDG startup preserves a foreign dangling symlink instead of replacing it");
            File.Delete(path);
        }
        if (!OperatingSystem.IsMacOS())
            return;
        using var pool = new Kumiko.Platform.AutoreleasePool();
        var cls = O.Define("KumikoLauncherStartupFixture", "NSObject",
            ("status", (nint)(delegate* unmanaged<nint, nint, nuint>)&Status, "Q@:"),
            ("registerAndReturnError:", (nint)(delegate* unmanaged<nint, nint, nint, byte>)&Register, "B@:^@"),
            ("unregisterAndReturnError:", (nint)(delegate* unmanaged<nint, nint, nint, byte>)&Unregister, "B@:^@"));
        var service = O.O(cls, "new");
        try
        {
            serviceState = 0;
            serviceWrites = 0;
            rejectWrite = false;
            check(NativeStartupItem.Mac(false, false, service).State == StartupState.Disabled && serviceWrites == 0, "production macOS startup adapter reads a private native service without registering");
            check(NativeStartupItem.Mac(true, true, service).State == StartupState.Enabled && serviceWrites == 1, "production macOS register ABI operates on a private native service fixture");
            serviceState = 2;
            check(NativeStartupItem.Mac(false, false, service).State == StartupState.RequiresApproval, "macOS pending user approval remains distinct from enabled");
            rejectWrite = true;
            rejected = false;
            try
            {
                NativeStartupItem.Mac(true, false, service);
            }
            catch (IOException)
            {
                rejected = true;
            }
            check(rejected && serviceState == 2, "macOS native registration error is surfaced without inventing a successful setting");
            rejectWrite = false;
            check(NativeStartupItem.Mac(true, false, service).State == StartupState.Disabled, "production macOS unregister ABI retires only the fixture service");
            var descriptor = O.Class("NSAppleEventDescriptor");
            var appleEvent = O.O(descriptor, "appleEventWithEventClass:eventID:targetDescriptor:returnID:transactionID:", (nint)0x61657674, (nint)0x6f617070, (nint)0, (nint)(-1), (nint)0);
            check(!NativeStartupItem.ReadMacLoginEvent(appleEvent), "ordinary native open-application event is not classified as login startup");
            O.V(appleEvent, "setParamDescriptor:forKeyword:", O.O(descriptor, "descriptorWithEnumCode:", (nuint)0x73766974), (nuint)0x70726474);
            check(!NativeStartupItem.ReadMacLoginEvent(appleEvent), "a native service-item launch is distinct from login startup");
            O.V(appleEvent, "setParamDescriptor:forKeyword:", O.O(descriptor, "descriptorWithEnumCode:", (nuint)0x6c676974), (nuint)0x70726474);
            check(NativeStartupItem.ReadMacLoginEvent(appleEvent), "native Apple Event login provenance is recognized without changing login items");
        }
        finally
        {
            O.Release(service);
        }
    }

    static nuint serviceState;
    static int serviceWrites;
    static bool rejectWrite;
    [UnmanagedCallersOnly]
    static nuint Status(nint self, nint cmd) => serviceState;
    [UnmanagedCallersOnly]
    static byte Register(nint self, nint cmd, nint error)
    {
        if (rejectWrite)
            return 0;
        serviceWrites++;
        serviceState = 1;
        return 1;
    }
    [UnmanagedCallersOnly]
    static byte Unregister(nint self, nint cmd, nint error)
    {
        if (rejectWrite)
            return 0;
        serviceWrites++;
        serviceState = 0;
        return 1;
    }

    static void Shortcuts(App app, Action<bool, string> check)
    {
        var backend = new PrivateShortcut();
        var owner = new GlobalShortcut(app, backend);
        var invoked = 0;
        check(owner.Register("Alt+Shift+Space", () => invoked++), "public shortcut owner registers only against an injected backend");
        var callback = backend.Callback!;
        callback();
        app.RunFor(TimeSpan.FromMilliseconds(15));
        check(invoked == 1, "global shortcut callback crosses App.Dispatch before invoking launcher work");
        callback();
        owner.Dispose();
        app.RunFor(TimeSpan.FromMilliseconds(15));
        check(invoked == 1 && backend.Disposed, "shortcut disposal rejects already queued callbacks and releases the backend");
    }

    sealed class PrivateStartup : IStartupItemBackend
    {
        public int Writes;
        StartupState state;
        public bool WasLaunchedAtLogin => false;
        public StartupStatus Read() => new(state);
        public StartupStatus SetEnabled(bool enabled)
        {
            Writes++;
            state = enabled ? StartupState.Enabled : StartupState.Disabled;
            return Read();
        }
    }
    sealed class PrivateShortcut : IGlobalShortcutBackend
    {
        public Action? Callback;
        public bool Disposed;
        public bool Register(ShortcutChord chord, Action callback)
        {
            Callback = callback;
            return true;
        }
        public void Unregister(ShortcutChord chord) => Callback = null;
        public void Dispose() => Disposed = true;
    }
}
