using System.Runtime.InteropServices;

namespace Kumiko.Desktop;

// Windows 10 1809–1909 fallback (no connectivity hints). Checked against mingw-w64 netioapi.h, netlistmgr.h and
// ocidl.h: NotifyIpInterfaceChange(family, callback, context, initial, handle); INetworkListManager derives from
// IDispatch, so GetConnectivity is slot 13; INetworkCostManager GetCost 3; IConnectionPointContainer
// FindConnectionPoint 4; IConnectionPoint Advise 5, Unadvise 6; INetworkListManagerEvents ConnectivityChanged 3;
// INetworkCostManagerEvents CostChanged 3, DataPlanStatusChanged 4.
internal sealed unsafe class WindowsLegacyNetworkBackend : INetworkStatusBackend
{
    static readonly Guid ManagerClass = new("dcb00c01-570f-4a9b-8d69-199fdba5723b");
    static readonly Guid Manager = new("dcb00000-570f-4a9b-8d69-199fdba5723b");
    static readonly Guid CostManager = new("dcb00008-570f-4a9b-8d69-199fdba5723b");
    static readonly Guid ManagerEvents = new("dcb00001-570f-4a9b-8d69-199fdba5723b");
    static readonly Guid CostEvents = new("dcb00009-570f-4a9b-8d69-199fdba5723b");
    static readonly Guid PointContainer = new("b196b284-bab4-101a-b69c-00aa00341d07");
    static readonly Guid Unknown = new("00000000-0000-0000-c000-000000000046");
    const int QueryInterface = 0, Release = 2, GetConnectivity = 13, GetCost = 3, FindConnectionPoint = 4, Advise = 5, Unadvise = 6;
    const uint AllContexts = 0x17;
    const ushort Unspecified = 0;
    const int Internet = 0x40 | 0x400, LocalNetwork = 0x10 | 0x20 | 0x100 | 0x200;
    const uint Unrestricted = 0x1, OverLimit = 0x10000, Roaming = 0x40000, NearLimit = 0x80000;
    static readonly nint* ListTable = Table((nint)(delegate* unmanaged<nint, int, int>)&ConnectivityChanged, 0);
    static readonly nint* CostTable = Table((nint)(delegate* unmanaged<nint, uint, nint, int>)&CostChanged, (nint)(delegate* unmanaged<nint, nint, int>)&DataPlanChanged);

    // A minimal COM event sink. Its GCHandle lives until COM releases the last reference.
    [StructLayout(LayoutKind.Sequential)]
    struct Sink
    {
        public nint* Table;
        public int References;
        public nint Owner;
        public Guid Events;
    }

    Action<NetworkStatus>? changed;
    GCHandle context;
    nint registration, manager;
    readonly List<(nint Point, uint Cookie)> connections = new();
    int requests;
    volatile bool disposed;

    public void Start(Action<NetworkStatus> callback)
    {
        changed = callback;
        context = GCHandle.Alloc(this);
        // Interface changes are the baseline wake source; the initial notification publishes the first snapshot.
        if (NotifyIpInterfaceChange(Unspecified, &InterfaceChanged, GCHandle.ToIntPtr(context), 1, out registration) != 0)
            throw new PlatformNotSupportedException("Native network interface notifications are unavailable.");
        // Network List Manager events add internet-probe and cost transitions that change no IP interface.
        // This App-thread manager lives in the owner's apartment; queries use their own instances on workers.
        Guid managerClass = ManagerClass, managerId = Manager;
        nint created = 0;
        if (CoCreateInstance(&managerClass, 0, AllContexts, &managerId, &created) < 0)
            return;
        manager = created;
        Connect(ManagerEvents, ListTable);
        Connect(CostEvents, CostTable);
    }

    void Connect(Guid events, nint* table)
    {
        var sink = (Sink*)NativeMemory.Alloc((nuint)sizeof(Sink));
        sink->Table = table;
        sink->References = 1;
        sink->Owner = GCHandle.ToIntPtr(GCHandle.Alloc(this));
        sink->Events = events;
        nint container = 0, point = 0;
        try
        {
            Guid containerId = PointContainer;
            if (((delegate* unmanaged<nint, Guid*, nint*, int>)Slot(manager, QueryInterface))(manager, &containerId, &container) < 0 ||
                ((delegate* unmanaged<nint, Guid*, nint*, int>)Slot(container, FindConnectionPoint))(container, &events, &point) < 0)
                return;
            uint cookie;
            if (((delegate* unmanaged<nint, nint, uint*, int>)Slot(point, Advise))(point, (nint)sink, &cookie) < 0)
                return;
            connections.Add((point, cookie));
            point = 0;
        }
        finally
        {
            ReleaseObject(point);
            ReleaseObject(container);
            ReleaseObject((nint)sink);
        }
    }

    // Bursts coalesce: one worker drains every request that arrived while it queried.
    void Schedule()
    {
        if (disposed)
            return;
        if (Interlocked.Increment(ref requests) == 1)
            ThreadPool.UnsafeQueueUserWorkItem(static owner => owner.Drain(), this, false);
    }

    void Drain()
    {
        do
        {
            Interlocked.Exchange(ref requests, 1);
            if (disposed)
                continue;
            var status = Query();
            if (!disposed)
                changed?.Invoke(status);
        }
        while (Interlocked.Decrement(ref requests) != 0);
    }

    static NetworkStatus Query()
    {
        // Balance only our own initialization; RPC_E_CHANGED_MODE means COM is already usable here.
        var com = CoInitializeEx(0, 0);
        nint list = 0, costs = 0;
        try
        {
            Guid managerClass = ManagerClass, managerId = Manager, costId = CostManager;
            int connectivity;
            if (CoCreateInstance(&managerClass, 0, AllContexts, &managerId, &list) < 0 ||
                ((delegate* unmanaged<nint, int*, int>)Slot(list, GetConnectivity))(list, &connectivity) < 0)
                return new();
            uint cost = 0;
            if (((delegate* unmanaged<nint, Guid*, nint*, int>)Slot(list, QueryInterface))(list, &costId, &costs) < 0 ||
                ((delegate* unmanaged<nint, uint*, nint, int>)Slot(costs, GetCost))(costs, &cost, 0) < 0)
                cost = 0;
            return Status(connectivity, cost);
        }
        finally
        {
            ReleaseObject(costs);
            ReleaseObject(list);
            if (com >= 0)
                CoUninitialize();
        }
    }

    /// <summary>Maps NLM_CONNECTIVITY and NLM_CONNECTION_COST like the connectivity-hint backend; unknown cost is null.</summary>
    internal static NetworkStatus Status(int connectivity, uint cost)
    {
        var level = (connectivity & Internet) != 0 ? Connectivity.Online : (connectivity & LocalNetwork) != 0 ? Connectivity.Local : Connectivity.Offline;
        if (cost == 0)
            return new(level);
        return new(level, (cost & Unrestricted) == 0 || (cost & (Roaming | OverLimit)) != 0, (cost & (NearLimit | OverLimit)) != 0);
    }

    [UnmanagedCallersOnly]
    static void InterfaceChanged(nint data, nint row, int kind)
    {
        try
        {
            ((WindowsLegacyNetworkBackend)GCHandle.FromIntPtr(data).Target!).Schedule();
        }
        catch
        {
            // No managed exception may cross the IP Helper callback boundary.
        }
    }

    static void Wake(nint self)
    {
        try
        {
            (GCHandle.FromIntPtr(((Sink*)self)->Owner).Target as WindowsLegacyNetworkBackend)?.Schedule();
        }
        catch
        {
            // No managed exception may cross the COM callback boundary.
        }
    }

    [UnmanagedCallersOnly]
    static int ConnectivityChanged(nint self, int connectivity)
    {
        Wake(self);
        return 0;
    }

    [UnmanagedCallersOnly]
    static int CostChanged(nint self, uint cost, nint address)
    {
        Wake(self);
        return 0;
    }

    [UnmanagedCallersOnly]
    static int DataPlanChanged(nint self, nint address)
    {
        Wake(self);
        return 0;
    }

    [UnmanagedCallersOnly]
    static int SinkQueryInterface(nint self, Guid* iid, nint* result)
    {
        if (*iid == Unknown || *iid == ((Sink*)self)->Events)
        {
            Interlocked.Increment(ref ((Sink*)self)->References);
            *result = self;
            return 0;
        }
        *result = 0;
        return unchecked((int)0x80004002);
    }

    [UnmanagedCallersOnly]
    static uint SinkAddRef(nint self) => (uint)Interlocked.Increment(ref ((Sink*)self)->References);

    [UnmanagedCallersOnly]
    static uint SinkRelease(nint self)
    {
        var remaining = Interlocked.Decrement(ref ((Sink*)self)->References);
        if (remaining == 0)
        {
            GCHandle.FromIntPtr(((Sink*)self)->Owner).Free();
            NativeMemory.Free((void*)self);
        }
        return (uint)remaining;
    }

    static nint* Table(nint third, nint fourth)
    {
        var table = (nint*)NativeMemory.Alloc(5, (nuint)sizeof(nint));
        table[QueryInterface] = (nint)(delegate* unmanaged<nint, Guid*, nint*, int>)&SinkQueryInterface;
        table[1] = (nint)(delegate* unmanaged<nint, uint>)&SinkAddRef;
        table[Release] = (nint)(delegate* unmanaged<nint, uint>)&SinkRelease;
        table[3] = third;
        table[4] = fourth;
        return table;
    }

    public void Dispose()
    {
        disposed = true;
        if (registration != 0)
        {
            CancelMibChangeNotify2(registration); // Waits for in-flight callbacks; never called inside a native callback.
            registration = 0;
        }
        foreach (var (point, cookie) in connections)
        {
            _ = ((delegate* unmanaged<nint, uint, int>)Slot(point, Unadvise))(point, cookie);
            ReleaseObject(point);
        }
        connections.Clear();
        ReleaseObject(manager);
        manager = 0;
        changed = null;
        if (context.IsAllocated)
            context.Free();
    }

    static nint Slot(nint value, int index) => (*(nint**)value)[index];
    static void ReleaseObject(nint value)
    {
        if (value != 0)
            ((delegate* unmanaged<nint, uint>)Slot(value, Release))(value);
    }

    [DllImport("iphlpapi.dll")]
    static extern uint NotifyIpInterfaceChange(ushort family, delegate* unmanaged<nint, nint, int, void> callback, nint context, byte initial, out nint handle);
    [DllImport("iphlpapi.dll")]
    static extern uint CancelMibChangeNotify2(nint handle);
    [DllImport("ole32.dll")]
    static extern int CoInitializeEx(nint reserved, uint model);
    [DllImport("ole32.dll")]
    static extern void CoUninitialize();
    [DllImport("ole32.dll")]
    static extern int CoCreateInstance(Guid* cls, nint outer, uint context, Guid* iid, nint* instance);
}
