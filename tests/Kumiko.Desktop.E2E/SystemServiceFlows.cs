using System.Runtime.InteropServices;
using System.Text.Json;
using Kumiko;
using Kumiko.Desktop;
using Kumiko.Platform;

static class SystemServiceFlows
{
    internal static void Run(string output, Action<bool, string> check)
    {
        Directory.CreateDirectory(output);
        using var stream = File.Create(Path.Combine(output, "system-services.json"));
        using var json = new Utf8JsonWriter(stream, new()
        {
            Indented = true
        });
        json.WriteStartObject();
        json.WriteString("platform", RuntimeInformation.OSDescription);
        json.WriteStartArray("checks");
        void Verify(bool condition, string name)
        {
            check(condition, name);
            json.WriteStringValue(name);
            json.Flush();
        }
        using var app = new App(new("System services", QuitOnLastWindow: false, Hidden: true));
        var errors = new List<Exception>();
        app.UnhandledException += errors.Add;
        Dispatch(app, Verify);
        if (OperatingSystem.IsMacOS())
            NativeMac(app, json, Verify);
        Verify(errors.Count == 0, "native service callbacks never escape or report managed errors");
        json.WriteEndArray();
        json.WriteString("notExecuted", "Real lock/suspend/resume, network changes, real power assertions; all Windows/Linux runtime behavior");
        json.WriteString("status", "passed");
        json.WriteEndObject();
    }

    static void Dispatch(App app, Action<bool, string> verify)
    {
        int owner = Environment.CurrentManagedThreadId;
        var native = new NetworkFixture();
        using var monitor = new NetworkMonitor(app, native);
        int changes = 0;
        bool ownerOnly = true;
        monitor.Changed += _ =>
        {
            changes++;
            ownerOnly &= Environment.CurrentManagedThreadId == owner;
        };
        Task.Run(() => native.Emit(new(Connectivity.Local, true))).GetAwaiter().GetResult();
        app.RunFor(TimeSpan.FromMilliseconds(30));
        verify(changes == 1 && monitor.Current.Connectivity == Connectivity.Local && monitor.Current.Expensive == true && ownerOnly,
            "worker network changes arrive once on the App owner thread");
        native.Emit(new(Connectivity.Local, true));
        app.RunFor(TimeSpan.FromMilliseconds(20));
        verify(changes == 1, "identical native network snapshots do not cause duplicate UI updates");
        native.Emit(new(Connectivity.Offline));
        monitor.Dispose();
        app.RunFor(TimeSpan.FromMilliseconds(20));
        verify(changes == 1 && native.Disposed, "disposing a network monitor drops queued delivery and tears down its subscription");
        var powerBackend = new PowerFixture();
        using var power = new PowerSession(app, powerBackend);
        var first = power.KeepAwake(KeepAwakeKind.System, "First export");
        var second = power.KeepAwake(KeepAwakeKind.System, "Second export");
        first.Dispose();
        verify(powerBackend.Active == 1, "releasing one export lease keeps a second export awake");
        first.Dispose();
        verify(powerBackend.Active == 1, "releasing the same lease twice cannot drop another lease");
        bool unsupported = false;
        try
        {
            using var display = power.KeepAwake(KeepAwakeKind.Display, "Display task");
        }
        catch (PlatformNotSupportedException)
        {
            unsupported = true;
        }
        verify(unsupported, "unsupported display inhibition is rejected before touching native state");
        int events = 0;
        power.SessionChanged += _ => events++;
        Task.Run(() => powerBackend.Session!(SessionChange.Resumed)).GetAwaiter().GetResult();
        app.RunFor(TimeSpan.FromMilliseconds(20));
        verify(events == 1, "worker session notifications marshal to the App loop");
        powerBackend.Session!(SessionChange.Locked);
        power.Dispose();
        app.RunFor(TimeSpan.FromMilliseconds(20));
        second.Dispose();
        verify(events == 1 && powerBackend.Active == 0 && powerBackend.Disposed, "service disposal releases all remaining leases and suppresses pending session events");
    }

    static void NativeMac(App app, Utf8JsonWriter json, Action<bool, string> verify)
    {
        using (var network = new NetworkMonitor(app))
        {
            // One bounded event-loop wait for the native initial path, not an application polling loop.
            app.RunFor(TimeSpan.FromMilliseconds(350));
            verify(network.Current.Connectivity != Connectivity.Unknown, "native Network.framework publishes its initial path through the App loop");
            json.WriteStringValue($"Native path: {network.Current.Connectivity}; cost known: {network.Current.Expensive.HasValue}");
        }
        using var pool = new AutoreleasePool();
        nint center = ObjC.New("NSNotificationCenter");
        try
        {
            var assertions = new AssertionFixture();
            var native = new MacPowerBackend(center, assertions);
            using var power = new PowerSession(app, native);
            var snapshot = power.Current;
            verify((snapshot.BatteryPercent is null || snapshot.BatteryPercent is >= 0 and <= 100) && power.IdleTime is { } idle && idle >= TimeSpan.Zero,
                "read-only IOKit battery and CoreGraphics idle APIs return a bounded snapshot");
            verify((power.Capabilities & PowerCapabilities.LockUnlock) == 0 && (power.Capabilities & PowerCapabilities.SessionActivity) != 0,
                "macOS session activity is not advertised as secure screen-lock detection");
            bool prevented = false;
            using (var production = new PowerSession(app))
            {
                try
                {
                    using var forbidden = production.KeepAwake(KeepAwakeKind.System, "Never installed");
                }
                catch (InvalidOperationException)
                {
                    prevented = true;
                }
            }
            verify(prevented, "hidden App cannot install real native power assertions");
            var events = new List<SessionChange>();
            power.SessionChanged += events.Add;
            foreach (string name in new[] { "NSWorkspaceWillSleepNotification", "NSWorkspaceDidWakeNotification", "NSWorkspaceSessionDidResignActiveNotification", "NSWorkspaceSessionDidBecomeActiveNotification" })
                ObjC.V(center, "postNotificationName:object:", ObjC.TempString(name), (nint)0);
            app.RunFor(TimeSpan.FromMilliseconds(30));
            verify(events.SequenceEqual(new[] { SessionChange.Suspending, SessionChange.Resumed, SessionChange.Deactivated, SessionChange.Activated }),
                "private native notification center exercises workspace suspend/resume/session block callbacks without changing the desktop");
            var system = power.KeepAwake(KeepAwakeKind.System, "Private system fixture");
            var display = power.KeepAwake(KeepAwakeKind.Display, "Private display fixture");
            verify(assertions.Active.Values.Order().SequenceEqual(new[] { "PreventUserIdleDisplaySleep", "PreventUserIdleSystemSleep" }),
                "native assertion boundary receives distinct display/system assertion types");
            system.Dispose();
            verify(assertions.Active.Count == 1, "macOS assertion IDs have independent release lifetimes");
            power.Dispose();
            display.Dispose();
            verify(assertions.Active.Count == 0, "disposing macOS power service releases every fixture assertion exactly once");
            ObjC.V(center, "postNotificationName:object:", ObjC.TempString("NSWorkspaceDidWakeNotification"), (nint)0);
            app.RunFor(TimeSpan.FromMilliseconds(20));
            verify(events.Count == 4, "removed native workspace subscriptions cannot deliver after disposal");
            // Verify adapter teardown directly, without the public service's late-delivery guard hiding a leak.
            int direct = 0;
            var adapter = new MacPowerBackend(center, assertions);
            adapter.Start(_ => { }, _ => direct++);
            adapter.Dispose();
            ObjC.V(center, "postNotificationName:object:", ObjC.TempString("NSWorkspaceDidWakeNotification"), (nint)0);
            verify(direct == 0, "native adapter removes observers before releasing block/context ownership");
        }
        finally
        {
            ObjC.Release(center);
        }
    }

    sealed class NetworkFixture : INetworkStatusBackend
    {
        Action<NetworkStatus>? changed;
        internal bool Disposed;
        public void Start(Action<NetworkStatus> callback) => changed = callback;
        internal void Emit(NetworkStatus status) => changed!(status);
        public void Dispose() => Disposed = true;
    }
    sealed class PowerFixture : IPowerSessionBackend
    {
        internal int Active;
        internal bool Disposed;
        internal Action<SessionChange>? Session;
        public PowerCapabilities Capabilities => PowerCapabilities.SystemKeepAwake | PowerCapabilities.LockUnlock;
        public TimeSpan? IdleTime => null;
        public PowerStatus Read() => new();
        public void Start(Action<PowerStatus> changed, Action<SessionChange> session) => Session = session;
        public IDisposable KeepAwake(KeepAwakeKind kind, string reason)
        {
            Active++;
            return new Release(() => Active--);
        }
        public void Dispose() => Disposed = true;
    }
    sealed class Release(Action action) : IDisposable
    {
        bool disposed;
        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            action();
        }
    }
    sealed class AssertionFixture : IMacPowerAssertions
    {
        uint next;
        internal Dictionary<uint, string> Active = new();
        public int Create(nint type, nint reason, out uint id)
        {
            id = ++next;
            Active.Add(id, ObjC.Text(type));
            return 0;
        }
        public void Release(uint id)
        {
            if (!Active.Remove(id))
                throw new InvalidOperationException("An assertion was released more than once.");
        }
    }
}
