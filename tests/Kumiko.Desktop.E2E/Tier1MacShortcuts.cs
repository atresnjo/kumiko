using Kumiko;
using Kumiko.Desktop;

static partial class Tier1Checks
{
    static void MacShortcutFlows(App app, Action<bool, string> check)
    {
        var carbon = new CarbonFixture();
        using var shortcuts = new GlobalShortcut(app, new MacGlobalShortcutBackend(app, carbon));
        int pressed = 0;
        check(shortcuts.Register("Primary+Shift+K", () => pressed++) && carbon.Key == 40 && carbon.Modifiers == ((1 << 8) | (1 << 9)),
            "Carbon adapter maps primary/shift and the physical K key through its injectable ABI");
        check(!shortcuts.Register("Primary+X", () => pressed++), "Carbon native conflicts return false without creating a registration");
        carbon.Press();
        Pump(app, () => pressed == 1, "Carbon fixture callback dispatch");
        carbon.Signature = 0;
        carbon.Press();
        app.RunFor(TimeSpan.FromMilliseconds(25));
        check(pressed == 1, "Carbon callback contexts route owned IDs and reject another hotkey signature");
        shortcuts.Unregister("Primary+Shift+K");
        shortcuts.Dispose();
        check(carbon.Unregistered == 1 && carbon.Removed, "Carbon disposal unregisters hotkeys and removes its handler before releasing callback context");
    }

    sealed unsafe class CarbonFixture : ICarbonHotKeys
    {
        nint callback, context;
        uint id;
        internal uint Key, Modifiers, Signature = 0x4b554d49;
        internal int Unregistered;
        internal bool Removed;
        public int Install(nint callback, nint context, uint eventClass, uint kind, out nint handler)
        {
            this.callback = callback;
            this.context = context;
            handler = 1;
            return 0;
        }
        public int Register(uint key, uint modifiers, uint id, uint options, out nint reference)
        {
            reference = (nint)id;
            if (key == 7)
                return -9878;
            Key = key;
            Modifiers = modifiers;
            this.id = id;
            return 0;
        }
        public int ReadId(nint ev, out uint signature, out uint id)
        {
            signature = Signature;
            id = this.id;
            return 0;
        }
        public void Unregister(nint reference) => Unregistered++;
        public void Remove(nint handler) => Removed = true;
        internal void Press() => ((delegate* unmanaged<nint, nint, nint, int>)callback)(0, 0, context);
    }
}
