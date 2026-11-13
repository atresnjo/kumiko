using System.Text.Json;
using Kumiko;
using Kumiko.Desktop;

static unsafe class ReviewShortcuts
{
    internal static void Run(string output, Action<bool, string> check)
    {
        if (!OperatingSystem.IsMacOS())
            return;
        using var app = new App(new("Shortcut review", Hidden: true, QuitOnLastWindow: false));
        var bus = new CarbonBus();
        var first = new CarbonEndpoint(bus);
        var second = new CarbonEndpoint(bus);
        using var left = new GlobalShortcut(app, new MacGlobalShortcutBackend(app, first));
        using var right = new GlobalShortcut(app, new MacGlobalShortcutBackend(app, second));
        int a = 0, b = 0;
        check(left.Register("Primary+K", () => a++) && right.Register("Primary+L", () => b++), "two shortcut owners register separate chords");
        check(first.Id != second.Id && first.Kind == 5 && second.Kind == 5 && first.Class == 0x6b657962 && first.Options == 1,
            "native Carbon boundary requests pressed events and exclusive process-wide identities");
        bus.Id = first.Id;
        bus.Fire();
        bus.Id = second.Id;
        bus.Fire();
        app.RunFor(TimeSpan.FromMilliseconds(30));
        check(a == 1 && b == 1, "Carbon handler chain delivers each chord only to its owning service");
        bus.Signature = 0;
        check(bus.Fire() == -9874, "unrelated native hotkeys propagate through every owned handler");
        check(!left.Register("Primary+X", () => a++), "exclusive native conflicts retain no callback or competitor ownership");
        using var stream = File.Create(Path.Combine(output, "review-shortcuts.json"));
        using var json = new Utf8JsonWriter(stream);
        json.WriteStartObject();
        json.WriteNumber("firstCallbacks", a);
        json.WriteNumber("secondCallbacks", b);
        json.WriteNumber("nativeEventKind", first.Kind);
        json.WriteNumber("nativeOptions", first.Options);
        json.WriteBoolean("distinctIdentities", first.Id != second.Id);
        json.WriteEndObject();
    }

    sealed class CarbonBus
    {
        internal readonly List<(nint Callback, nint Context)> Handlers = [];
        internal uint Id, Signature = 0x4b554d49;
        internal int Fire()
        {
            foreach (var handler in Handlers.AsEnumerable().Reverse())
            {
                int status = ((delegate* unmanaged<nint, nint, nint, int>)handler.Callback)(0, 0, handler.Context);
                if (status != -9874)
                    return status;
            }
            return -9874;
        }
    }

    sealed class CarbonEndpoint(CarbonBus bus) : ICarbonHotKeys
    {
        internal uint Id, Kind, Class, Options;
        (nint Callback, nint Context) handler;
        public int Install(nint callback, nint context, uint eventClass, uint kind, out nint reference)
        {
            Class = eventClass;
            Kind = kind;
            handler = (callback, context);
            bus.Handlers.Add(handler);
            reference = bus.Handlers.Count;
            return 0;
        }
        public int Register(uint key, uint modifiers, uint id, uint options, out nint reference)
        {
            Options = options;
            reference = 0;
            if (key == 7 && options == 1)
                return -9878;
            Id = id;
            reference = (nint)id;
            return 0;
        }
        public int ReadId(nint ev, out uint signature, out uint id)
        {
            signature = bus.Signature;
            id = bus.Id;
            return 0;
        }
        public void Unregister(nint reference)
        {
        }
        public void Remove(nint reference) => bus.Handlers.Remove(handler);
    }
}
