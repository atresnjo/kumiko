using System.Diagnostics;
using System.Text.Json;
using Kumiko;
using Kumiko.Desktop;
using S = Kumiko.Desktop.SecretVariant;

static class LinuxServiceReview
{
    internal static void Run(string output)
    {
        Directory.CreateDirectory(output);
        PrivateServiceBus.ResolveLibraries();
        using var stream = File.Create(Path.Combine(output, "linux-services-review.json"));
        using var json = new Utf8JsonWriter(stream, new()
        {
            Indented = true
        });
        json.WriteStartObject();
        json.WriteString("host", System.Runtime.InteropServices.RuntimeInformation.OSDescription);
        json.WriteString("evidence", "Private dbus-daemon, native host GIO, real D-Bus requests/signals/FD passing/cancellation; libsecret session/crypto replaced; no real desktop services");
        json.WriteString("delayPolicy", "Daemon replies are held independently of the App loop; no real power state changes.");
        json.WriteStartArray("checks");
        void Check(bool passed, string name)
        {
            json.WriteStartObject();
            json.WriteString("name", name);
            json.WriteBoolean("passed", passed);
            json.WriteEndObject();
            json.Flush();
            if (!passed)
                throw new InvalidOperationException(name);
        }
        using var fixture = new PrivateServiceBus(output);
        using var app = new App(new("Linux review", Hidden: true, QuitOnLastWindow: false));
        var errors = new List<Exception>();
        app.UnhandledException += errors.Add;
        Power(app, fixture, Check);
        Credentials(app, fixture, Check);
        Check(errors.Count == 0 && fixture.Errors.IsEmpty, "no native callback exceptions or fixture protocol errors");
        json.WriteEndArray();
        json.WriteString("notExecuted", "Linux kernel/desktop/logind/UPower policy; actual suspend; native libsecret encrypted sessions and crypto");
        json.WriteString("status", "passed");
        json.WriteEndObject();
        Console.WriteLine("Linux review isolated D-Bus behavior passed: " + output);
    }
    static void Pump(App app, Func<bool> complete)
    {
        var deadline = Stopwatch.StartNew();
        // Bounded test event pumping, never an application render or service polling loop.
        while (!complete() && deadline.Elapsed < TimeSpan.FromSeconds(5))
            app.RunFor(TimeSpan.FromMilliseconds(10));
        if (!complete())
            throw new TimeoutException("Private service evidence did not complete.");
    }
    static void Heartbeat(App app, Action<bool, string> check, string context)
    {
        bool delivered = false;
        app.Dispatch(() => delivered = true);
        app.RunFor(TimeSpan.FromMilliseconds(30));
        check(delivered, "App.Dispatch remains responsive during " + context);
    }
    static void Power(App app, PrivateServiceBus fixture, Action<bool, string> check)
    {
        fixture.DelayReads = true;
        var backend = new LinuxPowerBackend(fixture.Address);
        var timing = Stopwatch.StartNew();
        using var power = new PowerSession(app, backend);
        check(timing.Elapsed < TimeSpan.FromMilliseconds(250), "constructor returns before delayed UPower replies");
        Pump(app, () => fixture.ReadEntered.IsSet);
        check(power.Current.BatteryPresent is null, "slow initialization exposes unknown power state");
        Heartbeat(app, check, "initial D-Bus discovery/read");
        int owner = Environment.CurrentManagedThreadId;
        bool ownerOnly = true;
        int changes = 0;
        power.Changed += _ =>
        {
            changes++;
            ownerOnly &= Environment.CurrentManagedThreadId == owner;
        };
        int capabilities = 0;
        power.CapabilitiesChanged += _ => capabilities++;
        fixture.ReleaseReads();
        Pump(app, () => (power.Capabilities & PowerCapabilities.SystemKeepAwake) != 0);
        check(power.Current.BatteryPercent == 75 && capabilities > 0 && ownerOnly, "async initialization delivers battery/capability state on the App thread");
        fixture.ReadEntered.Reset();
        fixture.DelayReads = true;
        fixture.OnBattery = true;
        fixture.EmitPowerChange();
        Pump(app, () => fixture.ReadEntered.IsSet);
        Heartbeat(app, check, "a signal-triggered delayed UPower refresh");
        int requests = Volatile.Read(ref fixture.InhibitRequests);
        using (var cancelQueued = new CancellationTokenSource())
        {
            var queued = power.KeepAwakeAsync(KeepAwakeKind.System, "Cancelled queued work", cancelQueued.Token);
            cancelQueued.Cancel();
            check(queued.WaitForCancellation(), "a queued lease cancels before the preceding held refresh completes");
        }
        fixture.ReleaseReads();
        Pump(app, () => power.Current.OnBattery == true);
        check(ownerOnly && requests == Volatile.Read(ref fixture.InhibitRequests), "refresh results retain App-thread delivery and cancelled queued work never reaches login1");
        bool syncRejected = false;
        try
        {
            using var lease = power.KeepAwake(KeepAwakeKind.System, "Use async acquisition");
        }
        catch (NotSupportedException)
        {
            syncRejected = true;
        }
        check(syncRejected, "synchronous Linux acquisition explicitly directs callers to async API");
        fixture.DelayInhibits = true;
        var pending = power.KeepAwakeAsync(KeepAwakeKind.System, "Private idle work");
        Pump(app, () => fixture.InhibitEntered.IsSet);
        check(!pending.IsCompleted, "async lease is not reported acquired before the daemon grants it");
        Heartbeat(app, check, "a delayed login1 inhibitor grant");
        check(fixture.LastInhibit.Length == 4 && fixture.LastInhibit[0] == "idle" && fixture.LastInhibit[3] == "block",
            "wire request inhibits automatic idle logic, never explicit suspend/hibernate or hardware keys");
        fixture.ReleaseInhibits();
        Pump(app, () => pending.IsCompleted);
        using var granted = pending.GetAwaiter().GetResult();
        var closed = fixture.NextClosure();
        check(!closed.IsCompleted, "granted lease owns the received native inhibitor descriptor");
        granted.Dispose();
        closed.WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
        check(closed.IsCompletedSuccessfully, "disposing a granted lease closes every transferred descriptor copy");
        fixture.DenyInhibits = true;
        var denied = power.KeepAwakeAsync(KeepAwakeKind.System, "Denied fixture work");
        Pump(app, () => denied.IsCompleted);
        check(denied.IsFaulted, "native access denial faults acquisition instead of returning a fake lease");
        _ = denied.Exception;
        fixture.DenyInhibits = false;
        Cancellation(app, fixture, power, check);
        int beforeDispose = changes;
        power.Dispose();
        backend.Completion.WaitAsync(TimeSpan.FromSeconds(1)).GetAwaiter().GetResult();
        fixture.EmitPowerChange();
        app.RunFor(TimeSpan.FromMilliseconds(40));
        check(changes == beforeDispose, "disposed power service suppresses late native notifications");
        Disposal(app, fixture, check);
    }
    static void Cancellation(App app, PrivateServiceBus fixture, PowerSession power, Action<bool, string> check)
    {
        fixture.InhibitEntered.Reset();
        fixture.DelayInhibits = true;
        using var cancel = new CancellationTokenSource();
        var pending = power.KeepAwakeAsync(KeepAwakeKind.System, "Cancelled fixture work", cancel.Token);
        Pump(app, () => fixture.InhibitEntered.IsSet);
        cancel.Cancel();
        Pump(app, () => pending.IsCompleted);
        check(pending.IsCanceled, "cancellation completes a slow inhibitor request without waiting for its reply");
        fixture.ReleaseInhibits();
        var closed = fixture.NextClosure();
        closed.WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
        check(closed.IsCompletedSuccessfully, "late descriptor from a cancelled request is closed without becoming a lease");
    }
    static void Disposal(App app, PrivateServiceBus fixture, Action<bool, string> check)
    {
        fixture.DelayReads = true;
        fixture.ReadEntered.Reset();
        var backend = new LinuxPowerBackend(fixture.Address);
        using var power = new PowerSession(app, backend);
        int changed = 0;
        power.Changed += _ => changed++;
        Pump(app, () => fixture.ReadEntered.IsSet);
        var timing = Stopwatch.StartNew();
        power.Dispose();
        check(timing.Elapsed < TimeSpan.FromMilliseconds(250), "disposal does not wait for a delayed initialization reply");
        backend.Completion.WaitAsync(TimeSpan.FromSeconds(1)).GetAwaiter().GetResult();
        fixture.ReleaseReads();
        app.RunFor(TimeSpan.FromMilliseconds(40));
        check(changed == 0, "cancelled initialization releases its bus and cannot deliver after disposal");
        var second = new LinuxPowerBackend(fixture.Address);
        using var running = new PowerSession(app, second);
        Pump(app, () => (running.Capabilities & PowerCapabilities.SystemKeepAwake) != 0);
        fixture.DelayReads = true;
        fixture.ReadEntered.Reset();
        fixture.EmitPowerChange();
        Pump(app, () => fixture.ReadEntered.IsSet);
        Heartbeat(app, check, "a refresh pending at disposal");
        running.Dispose();
        second.Completion.WaitAsync(TimeSpan.FromSeconds(1)).GetAwaiter().GetResult();
        fixture.ReleaseReads();
        check(second.Completion.IsCompletedSuccessfully, "disposing during a signal refresh cancels native work before the held reply");
        var third = new LinuxPowerBackend(fixture.Address);
        using var acquiring = new PowerSession(app, third);
        Pump(app, () => (acquiring.Capabilities & PowerCapabilities.SystemKeepAwake) != 0);
        fixture.DelayInhibits = true;
        fixture.InhibitEntered.Reset();
        var pending = acquiring.KeepAwakeAsync(KeepAwakeKind.System, "Disposed fixture work");
        Pump(app, () => fixture.InhibitEntered.IsSet);
        acquiring.Dispose();
        Pump(app, () => pending.IsCompleted);
        third.Completion.WaitAsync(TimeSpan.FromSeconds(1)).GetAwaiter().GetResult();
        check(pending.IsCanceled, "service disposal cancels pending acquisition and finishes worker cleanup");
        fixture.ReleaseInhibits();
        var closed = fixture.NextClosure();
        closed.WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
        check(closed.IsCompletedSuccessfully, "late inhibitor reply after disposal releases its descriptors");
        var fourth = new LinuxPowerBackend(fixture.Address);
        using var shuttingDown = new PowerSession(app, fourth);
        Pump(app, () => (shuttingDown.Capabilities & PowerCapabilities.SystemKeepAwake) != 0);
        fixture.StopDefaultCallbacks();
        shuttingDown.Dispose();
        // No App/default-context pumping here: native teardown must own its independent drain loop.
        fourth.Completion.WaitAsync(TimeSpan.FromSeconds(1)).GetAwaiter().GetResult();
        check(LinuxPowerBus.ActiveCallbackContexts == 0, "all native signal contexts release after the App/default loop stops pumping");
    }
    static bool WaitForCancellation(this Task task)
    {
        try
        {
            task.WaitAsync(TimeSpan.FromSeconds(1)).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            return task.IsCanceled;
        }
        return false;
    }
    static void Credentials(App app, PrivateServiceBus fixture, Action<bool, string> check)
    {
        var scope = new CredentialScope("dev.kumiko.review", "isolated");
        using var store = new CredentialStore(app, scope, new LinuxCredentialBackend(scope, cancellation => new FixtureSecretSession(fixture.Address, cancellation)));
        fixture.SecretRace = 1;
        using (var locked = store.ReadAsync("refresh").GetAwaiter().GetResult())
            check(locked.Status == CredentialStatus.Locked && locked.Secret is null, "item locking after SearchItems preserves Locked without exposing a secret");
        fixture.SecretRace = 2;
        using (var missing = store.ReadAsync("refresh").GetAwaiter().GetResult())
            check(missing.Status == CredentialStatus.Missing && missing.Secret is null, "item disappearing after SearchItems preserves Missing without exposing another item");
        fixture.SecretRace = 0;
        using var retry = store.ReadAsync("refresh").GetAwaiter().GetResult();
        check(retry.Status == CredentialStatus.Success && retry.Secret!.Bytes.Length == 32, "retry after recovery receives the private item's binary data");
    }
    sealed class FixtureSecretSession : ILinuxSecretSession
    {
        public nint Connection
        {
            get;
        }
        public string Algorithms => "dh-ietf1024-sha256-aes128-cbc-pkcs7";
        public string Path => "/fixture/session";
        internal FixtureSecretSession(string address, nint cancellation) => Connection = PrivateServiceBus.Connect(address, cancellation);
        public S Encode(ReadOnlySpan<byte> secret) => throw new NotSupportedException("Fixture supports credential reads only.");
        public SecretBuffer Decode(S encoded)
        {
            using var bytes = encoded.Child(2);
            return PrivateServiceBus.CopyBytes(bytes);
        }
        public void Dispose() => PrivateServiceBus.g_object_unref(Connection);
    }
}
