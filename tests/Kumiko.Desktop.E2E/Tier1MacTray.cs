using System.Runtime.InteropServices;
using Kumiko;
using Kumiko.Desktop;
using Kumiko.Platform;
using Kumiko.Rendering;

static partial class Tier1Checks
{
    static void MacTrayFlows(App app, Action<bool, string> check)
    {
        using var fixture = new StatusItemFixture();
        using var menu = new Menu(app, new MenuItem("Fixture menu"));
        byte[] Icon(int size)
        {
            using var png = new MemoryStream();
            var bytes = new byte[size * size * 4];
            Array.Fill(bytes, (byte)255);
            Png.Write(png, new Pixels(bytes, size, size));
            return png.ToArray();
        }
        using var tray = new Tray(app, new("native-fixture", Icon(18), Icon(36), Template: true, ToolTip: "Native tooltip", Title: "Fixture", Menu: menu), callback => new MacTrayBackend(app, callback, fixture.Handle));
        var image = ObjC.O(fixture.Button, "image");
        var retina = ObjC.O(ObjC.O(image, "representations"), "objectAtIndex:", (nuint)1);
        check(ObjC.B(image, "isTemplate") != 0 && ObjC.N(ObjC.O(image, "representations"), "count") >= 2 &&
            ObjC.S(retina, "size").Width == 18 && ObjC.N(retina, "pixelsWide") == 36 &&
            ObjC.Text(ObjC.O(fixture.Button, "toolTip")) == "Native tooltip" && ObjC.Text(ObjC.O(fixture.Button, "title")) == "Fixture",
            "native tray adapter decodes both PNG scales and sets template/title/tooltip on an offscreen button");
        int clicked = 0;
        tray.Click += () => clicked++;
        ObjC.V(ObjC.O(fixture.Button, "target"), "activate:", fixture.Button);
        Pump(app, () => clicked == 1, "native offscreen tray callback");
        tray.SetToolTip("Updated native tooltip");
        tray.SetTitle("Updated");
        tray.SetIcon(Icon(18));
        tray.PopUpMenu();
        check(fixture.Popups == 1 && fixture.Menu == menu.Native && ObjC.B(ObjC.O(fixture.Button, "image"), "isTemplate") == 0 &&
            ObjC.Text(ObjC.O(fixture.Button, "toolTip")) == "Updated native tooltip" && ObjC.Text(ObjC.O(fixture.Button, "title")) == "Updated",
            "native tray runtime changes and menu routing reach the owned status-item fixture");
        tray.Dispose();
        check(ObjC.O(fixture.Button, "target") == 0, "native tray disposal detaches its Objective-C callback target");
    }

    sealed unsafe class StatusItemFixture : IDisposable
    {
        static readonly Dictionary<nint, StatusItemFixture> instances = new();
        internal nint Handle
        {
            get;
        }
        internal nint Button
        {
            get;
        }
        internal nint Menu;
        internal int Popups;

        internal StatusItemFixture()
        {
            var cls = ObjC.Define("KumikoStatusItemFixture", "NSObject",
                ("button", (nint)(delegate* unmanaged<nint, nint, nint>)&GetButton, "@@:"),
                ("popUpStatusItemMenu:", (nint)(delegate* unmanaged<nint, nint, nint, void>)&Popup, "v@:@"));
            Handle = ObjC.O(cls, "new");
            Button = ObjC.O(ObjC.O(ObjC.Class("NSButton"), "alloc"), "initWithFrame:", new NativeRect(0, 0, 72, 22));
            instances.Add(Handle, this);
        }

        [UnmanagedCallersOnly]
        static nint GetButton(nint self, nint cmd) => instances[self].Button;
        [UnmanagedCallersOnly]
        static void Popup(nint self, nint cmd, nint menu)
        {
            instances[self].Menu = menu;
            instances[self].Popups++;
        }

        public void Dispose()
        {
            instances.Remove(Handle);
            ObjC.Release(Button);
            ObjC.Release(Handle);
        }
    }
}
