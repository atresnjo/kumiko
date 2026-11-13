using System.Runtime.InteropServices;
using System.Text.Json;
using Kumiko;
using Kumiko.Desktop;
using Kumiko.Platform;
using Launcher;
using O = Kumiko.Platform.ObjC;

static unsafe class ReviewLaunchFlows
{
    [DllImport("/System/Library/Frameworks/Carbon.framework/Carbon")]
    static extern short AEGetEventHandler(uint eventClass, uint eventId, out nint handler, out nint reference, byte systemHandler);
    public static void Run(string output, Action<bool, string> check)
    {
        if (!OperatingSystem.IsMacOS())
            return;
        using var stream = File.Create(Path.Combine(output, "review-launch-order.json"));
        using var json = new Utf8JsonWriter(stream, new()
        {
            Indented = true
        });
        json.WriteStartArray();
        foreach (var login in new[] { true, false })
            Delayed(json, login, check);
        EarlyAndDisposed(check);
        json.WriteEndArray();
    }

    static void Delayed(Utf8JsonWriter json, bool login, Action<bool, string> check)
    {
        using var app = new App(new("Private delayed launch event", false, true), owner => new MacApp(owner, awaitLaunchEvent: true));
        using var startup = new StartupItem(app, "dev.fixture.provenance", "Provenance", new NativeStartupItem(app, "dev.fixture.provenance", "Provenance"));
        using var ui = new Session(new()
        {
            LaunchProvenance = startup.GetLaunchAtLoginAsync
        });
        // The real MacApp constructor has already called finishLaunching. Drain
        // dispatch before an event exists: this must not guess an ordinary launch.
        app.RunFor(TimeSpan.FromMilliseconds(30));
        ui.Pump();
        check(!ui.Desktop.Visible && ui.Desktop.Shows == 0, "finishLaunching and an empty dispatch turn cannot summon before launch provenance");
        using var delivered = new ManualResetEventSlim();
        using var scheduled = app.Schedule(TimeSpan.FromMilliseconds(20), () =>
        {
            Event(login);
            delivered.Set();
        });
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!delivered.IsSet && DateTime.UtcNow < deadline)
            app.RunFor(TimeSpan.FromMilliseconds(10));
        check(delivered.IsSet, "a delayed private native open-application callback runs after initialization and controller Start");
        ui.Wait(() => login ? startup.WasLaunchedAtLogin : ui.Desktop.Shows == 1);
        // Wait for the controller's continuation even when the outcome is hidden.
        var outcome = startup.GetLaunchAtLoginAsync().GetAwaiter().GetResult();
        ui.Pump();
        check(outcome == login && startup.WasLaunchedAtLogin == login && ui.Desktop.Visible == !login,
            "actual delayed Apple Event provenance keeps login hidden and summons an ordinary launch");
        Event(!login);
        ui.Pump();
        check(startup.WasLaunchedAtLogin == login && ui.Desktop.Shows == (login ? 0 : 1),
            "duplicate open-application events cannot rewrite initial provenance or summon twice");
        json.WriteStartObject();
        json.WriteString("order", "MacApp.finishLaunching -> controller.Start -> empty dispatch -> scheduled private oapp -> controller decision");
        json.WriteBoolean("login", login);
        json.WriteBoolean("visible", ui.Desktop.Visible);
        json.WriteNumber("shows", ui.Desktop.Shows);
        json.WriteEndObject();
    }

    static void EarlyAndDisposed(Action<bool, string> check)
    {
        using (var app = new App(new("Early private launch event", false, true), owner => new MacApp(owner, awaitLaunchEvent: true)))
        using (var startup = new StartupItem(app, "dev.fixture.early", "Early", new NativeStartupItem(app, "dev.fixture.early", "Early")))
        {
            Event(true);
            using var ui = new Session(new()
            {
                LaunchProvenance = startup.GetLaunchAtLoginAsync
            });
            check(!ui.Desktop.Visible && startup.WasLaunchedAtLogin, "login event before subscription is remembered for the owning App");
        }
        using (var app = new App(new("Disposed private launch waiter", false, true), owner => new MacApp(owner, awaitLaunchEvent: true)))
        using (var startup = new StartupItem(app, "dev.fixture.late", "Late", new NativeStartupItem(app, "dev.fixture.late", "Late")))
        using (var ui = new Session(new()
        {
            LaunchProvenance = startup.GetLaunchAtLoginAsync
        }))
        {
            using var cancel = new CancellationTokenSource();
            var canceled = startup.GetLaunchAtLoginAsync(cancel.Token);
            cancel.Cancel();
            check(canceled.IsCanceled, "canceling one launch-provenance waiter does not complete the shared native launch");
            ui.Host.Dispose();
            Event(false);
            ui.Pump();
            check(!ui.Desktop.Visible && !startup.WasLaunchedAtLogin, "late ordinary launch after host disposal cannot summon a retired controller");
        }
        using var normal = new App(new("Fresh hidden App provenance", false, true));
        using var fresh = new StartupItem(normal, "dev.fixture.fresh", "Fresh", new NativeStartupItem(normal, "dev.fixture.fresh", "Fresh"));
        check(fresh.GetLaunchAtLoginAsync().IsCompletedSuccessfully && !fresh.WasLaunchedAtLogin,
            "a new hidden App has its own settled provenance without leaking a prior App login event");
    }

    static void Event(bool login)
    {
        using var pool = new AutoreleasePool();
        var descriptor = O.Class("NSAppleEventDescriptor");
        var appleEvent = O.O(descriptor, "appleEventWithEventClass:eventID:targetDescriptor:returnID:transactionID:", (nint)0x61657674, (nint)0x6f617070, (nint)0, (nint)(-1), (nint)0);
        if (login)
            O.V(appleEvent, "setParamDescriptor:forKeyword:", O.O(descriptor, "descriptorWithEnumCode:", (nuint)0x6c676974), (nuint)0x70726474);
        var reply = O.O(descriptor, "nullDescriptor");
        // Look up and directly invoke only this process's registered handler.
        // Nothing is posted/sent to the system or addressed to another process.
        var found = AEGetEventHandler(0x61657674, 0x6f617070, out var handler, out var reference, 0);
        if (found != 0 || handler == 0)
            throw new InvalidOperationException("Own open-application handler missing: " + found);
        var error = ((delegate* unmanaged<nint, nint, nint, short>)handler)(O.O(appleEvent, "aeDesc"), O.O(reply, "aeDesc"), reference);
        if (error != 0)
            throw new InvalidOperationException("Private open-application handler failed: " + error);
    }
}
