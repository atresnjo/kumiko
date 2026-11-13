using System.Runtime.InteropServices;
using Kumiko.Platform;
using O = Kumiko.Platform.ObjC;

namespace Kumiko.Desktop;

internal interface IMacPowerAssertions
{
    int Create(nint type, nint reason, out uint id);
    void Release(uint id);
}

internal sealed unsafe class MacPowerBackend(nint fixtureCenter = 0, IMacPowerAssertions? fixtureAssertions = null) : IPowerSessionBackend
{
    internal const string IOKit = "/System/Library/Frameworks/IOKit.framework/IOKit";
    const string CF = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    const string CG = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    readonly List<(nint Token, NativeBlock Block)> observers = new();
    nint center;
    nint source;
    GCHandle context;
    Action<PowerStatus>? changed;
    readonly IMacPowerAssertions assertions = fixtureAssertions ?? new NativeAssertions();
    public PowerCapabilities Capabilities => PowerCapabilities.SystemKeepAwake | PowerCapabilities.DisplayKeepAwake |
        PowerCapabilities.SuspendResume | PowerCapabilities.SessionActivity | PowerCapabilities.Battery | PowerCapabilities.IdleTime;
    public TimeSpan? IdleTime
    {
        get
        {
            double seconds = CGEventSourceSecondsSinceLastEventType(1, uint.MaxValue);
            return double.IsFinite(seconds) && seconds >= 0 && seconds <= TimeSpan.MaxValue.TotalSeconds ? TimeSpan.FromSeconds(seconds) : null;
        }
    }
    public PowerStatus Read()
    {
        using var pool = new AutoreleasePool();
        var info = IOPSCopyPowerSourcesInfo();
        if (info == 0)
            return new();
        nint list = 0;
        try
        {
            list = IOPSCopyPowerSourcesList(info);
            if (list == 0)
                return new();
            var providing = O.Text(IOPSGetProvidingPowerSourceType(info));
            bool? onBattery = providing == "Battery Power" ? true : providing == "AC Power" ? false : null;
            for (nuint i = 0; i < O.N(list, "count"); i++)
            {
                var description = IOPSGetPowerSourceDescription(info, O.O(list, "objectAtIndex:", i));
                if (description == 0 || O.Text(Value(description, "Type")) != "InternalBattery")
                    continue;
                nint present = Value(description, "Is Present");
                nint charging = Value(description, "Is Charging");
                nint capacity = Value(description, "Current Capacity");
                nint maximum = Value(description, "Max Capacity");
                double max = maximum == 0 ? 0 : O.D(maximum, "doubleValue");
                double remaining = capacity == 0 ? double.NaN : O.D(capacity, "doubleValue");
                double? percent = double.IsFinite(remaining) && double.IsFinite(max) && max > 0 ? Math.Clamp(remaining / max * 100, 0, 100) : null;
                if (present != 0 && O.B(present, "boolValue") == 0)
                    percent = null;
                return new(present == 0 ? null : O.B(present, "boolValue") != 0, onBattery, percent,
                    charging == 0 ? null : O.B(charging, "boolValue") != 0);
            }
            return new(false, onBattery);
        }
        finally
        {
            if (list != 0)
                CFRelease(list);
            CFRelease(info);
        }
    }
    static nint Value(nint dictionary, string key) => O.O(dictionary, "objectForKey:", O.TempString(key));
    public void Start(Action<PowerStatus> callback, Action<SessionChange> session)
    {
        using var pool = new AutoreleasePool();
        changed = callback;
        center = fixtureCenter != 0 ? fixtureCenter : O.O(O.O(O.Class("NSWorkspace"), "sharedWorkspace"), "notificationCenter");
        Observe("NSWorkspaceWillSleepNotification", SessionChange.Suspending, session);
        Observe("NSWorkspaceDidWakeNotification", SessionChange.Resumed, session);
        Observe("NSWorkspaceSessionDidBecomeActiveNotification", SessionChange.Activated, session);
        Observe("NSWorkspaceSessionDidResignActiveNotification", SessionChange.Deactivated, session);
        context = GCHandle.Alloc(this);
        source = IOPSNotificationCreateRunLoopSource(&PowerChanged, GCHandle.ToIntPtr(context));
        if (source == 0)
            throw new PlatformNotSupportedException("IOKit power-source notifications are unavailable.");
        CFRunLoopAddSource(CFRunLoopGetMain(), source, commonModes);
    }
    void Observe(string name, SessionChange value, Action<SessionChange> session)
    {
        var block = NativeBlock.Pointer(_ => session(value));
        nint token = O.O(center, "addObserverForName:object:queue:usingBlock:", O.TempString(name), (nint)0, 0, block.Handle);
        if (token == 0)
        {
            block.Dispose();
            throw new PlatformNotSupportedException("Workspace notification registration failed.");
        }
        O.O(token, "retain");
        observers.Add((token, block));
    }
    [UnmanagedCallersOnly]
    static void PowerChanged(nint data)
    {
        try
        {
            var owner = (MacPowerBackend)GCHandle.FromIntPtr(data).Target!;
            owner.changed?.Invoke(owner.Read());
        }
        catch
        {
        }
    }
    public IDisposable KeepAwake(KeepAwakeKind kind, string reason)
    {
        using var pool = new AutoreleasePool();
        var type = O.TempString(kind == KeepAwakeKind.Display ? "PreventUserIdleDisplaySleep" : "PreventUserIdleSystemSleep");
        int result = assertions.Create(type, O.TempString(reason), out uint assertion);
        if (result != 0)
            throw new InvalidOperationException("The native power assertion was denied.");
        return new Assertion(assertions, assertion);
    }
    sealed class Assertion(IMacPowerAssertions api, uint id) : IDisposable
    {
        bool disposed;
        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            api.Release(id);
        }
    }
    sealed class NativeAssertions : IMacPowerAssertions
    {
        public int Create(nint type, nint reason, out uint id) => IOPMAssertionCreateWithDescription(type, reason, 0, 0, 0, 0, 0, out id);
        public void Release(uint id) => IOPMAssertionRelease(id);
    }
    public void Dispose()
    {
        foreach (var (token, block) in observers)
        {
            O.V(center, "removeObserver:", token);
            O.Release(token);
            block.Dispose();
        }
        observers.Clear();
        if (source != 0)
        {
            CFRunLoopRemoveSource(CFRunLoopGetMain(), source, commonModes);
            CFRunLoopSourceInvalidate(source);
            CFRelease(source);
            source = 0;
        }
        if (context.IsAllocated)
            context.Free();
        changed = null;
    }
    static readonly nint commonModes = Marshal.ReadIntPtr(NativeLibrary.GetExport(NativeLibrary.Load(CF), "kCFRunLoopCommonModes"));
    [DllImport(IOKit)]
    static extern nint IOPSCopyPowerSourcesInfo();
    [DllImport(IOKit)]
    static extern nint IOPSCopyPowerSourcesList(nint info);
    [DllImport(IOKit)]
    static extern nint IOPSGetPowerSourceDescription(nint info, nint source);
    [DllImport(IOKit)]
    static extern nint IOPSGetProvidingPowerSourceType(nint info);
    [DllImport(IOKit)]
    static extern nint IOPSNotificationCreateRunLoopSource(delegate* unmanaged<nint, void> callback, nint context);
    [DllImport(IOKit)]
    static extern int IOPMAssertionCreateWithDescription(nint type, nint name, nint details, nint reason, nint bundle, double timeout, nint action, out uint id);
    [DllImport(IOKit)]
    static extern int IOPMAssertionRelease(uint id);
    [DllImport(CF)]
    static extern nint CFRunLoopGetMain();
    [DllImport(CF)]
    static extern void CFRunLoopAddSource(nint loop, nint source, nint mode);
    [DllImport(CF)]
    static extern void CFRunLoopRemoveSource(nint loop, nint source, nint mode);
    [DllImport(CF)]
    static extern void CFRunLoopSourceInvalidate(nint source);
    [DllImport(CF)]
    static extern void CFRelease(nint value);
    [DllImport(CG)]
    static extern double CGEventSourceSecondsSinceLastEventType(int state, uint type);
}
