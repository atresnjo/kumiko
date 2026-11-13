using System.Runtime.InteropServices;
using Kumiko;
using Kumiko.Desktop;
using Kumiko.Platform;
using Kumiko.Rendering;
using Kumiko.Web;

static partial class Tier1Checks
{
    static unsafe void MacLifetimeFlows(App app, string output, Action<bool, string> check)
    {
        int completed = 0;
        var block = NativeBlock.Permission((granted, error) =>
        {
            if (granted && error is null)
                completed++;
        });
        var retained = BlockCopy(block.Handle);
        block.Dispose();
        try
        {
            var invoke = Marshal.ReadIntPtr(retained, 16);
            ((delegate* unmanaged<nint, byte, nint, void>)invoke)(retained, 1, 0);
        }
        finally
        {
            BlockRelease(retained);
        }

        check(completed == 1, "native Objective-C block copies retain callbacks independently of the managed block owner");
        var list = new MenuItem("List", Id: "list") { Radio = true, Checked = true };
        var grid = new MenuItem("Grid", Id: "grid") { Radio = true };
        var separate = new MenuItem("Separate", Id: "separate") { Radio = true, Checked = true };
        using var menu = new Menu(app, list, grid, new(Separator: true), separate);
        grid.SetChecked(true);
        var first = ObjC.O(menu.Native, "itemAtIndex:", (nuint)0);
        var second = ObjC.O(menu.Native, "itemAtIndex:", (nuint)1);
        check(ObjC.N(first, "state") == 0 && ObjC.N(second, "state") == 1 && separate.Checked,
            "runtime radio setters update native adjacent groups without crossing separators");
        grid.SetLabel("Grid view");
        grid.SetEnabled(false);
        grid.SetVisible(false);
        check(ObjC.Text(ObjC.O(second, "title")) == "Grid view" && ObjC.B(second, "isEnabled") == 0 && ObjC.B(second, "isHidden") != 0,
            "native menu labels, enabled state and visibility refresh at runtime");
        var nativeApp = ObjC.O(ObjC.Class("NSApplication"), "sharedApplication");
        var previousMenu = ObjC.O(nativeApp, "mainMenu");
        menu.Append(new("Appended", Id: "appended"));
        check(menu.Find("appended") is not null && ObjC.N(menu.Native, "numberOfItems") == 5 && ObjC.O(nativeApp, "mainMenu") == previousMenu,
            "appending to an owned menu preserves the installed app menu");
        RecentMenuFlows(app, output, check);
        StandardMenuOwnership(app, check);
        using (var page = app.NewPage(new(Title: "Hidden page shell", Width: 320, Height: 220)
        {
            MaxWidth = 640,
            MaxHeight = 480,
            Opacity = .75f
        }, new PageOptions(Url: "/index.html", Assets: [Asset.Text("/index.html", "<!doctype html><title>Hidden shell fixture</title><p>Private asset</p>", "text/html")], Inspectable: false)))
        {
            int pageReady = 0;
            page.ReadyToShow += () => pageReady++;
            Pump(app, () => pageReady == 1, "hidden web window readiness");
            var limits = ObjC.S(page.NativeHandle, "contentMaxSize");
            check(!page.IsVisible && pageReady == 1 && page.Page!.IsReady,
                "hidden local-asset WKWebView windows publish readiness without mapping despite default Visible");
            check(limits.Width == 640 && limits.Height == 480 && ObjC.D(page.NativeHandle, "alphaValue") == .75,
                "WKWebView windows apply shared shell limits and opacity before loading");
            check(!page.Page!.UsesPersistentDataStore, "hidden WKWebView fixtures use an isolated nonpersistent website data store");
        }
        using (var unprotected = app.NewWindow(new(Visible: false), c => c.Text("Content-protection fixture")))
        {
            bool unsupported = false;
            try
            {
                unprotected.SetContentProtection(true);
            }
            catch (PlatformNotSupportedException)
            {
                unsupported = true;
            }
            check(unsupported && !unprotected.Options.ContentProtection,
                "macOS rejects content protection instead of claiming a legacy sharing hint prevents capture");
        }
        using var window = app.NewWindow(new(Title: "Material fixture", Width: 320, Height: 220, Visible: false)
        {
            Transparent = true,
            Material = WindowMaterial.Sidebar
        }, c =>
        {
            using var root = c.Div(key: "material").SizeFull().Padding(24);
            c.Text("Transparent material fixture");
        });
        app.RunFor(TimeSpan.FromMilliseconds(30));
        var pixels = window.Capture();
        Png.Write(Path.Combine(output, "tier1-material.png"), pixels);
        check(!window.IsVisible && pixels.Bytes[3] == 0, "hidden NSVisualEffectView capture preserves transparent backing without displaying a window");
    }

    static void StandardMenuOwnership(App app, Action<bool, string> check)
    {
        var nativeApp = ObjC.O(ObjC.Class("NSApplication"), "sharedApplication");
        var previous = ObjC.O(ObjC.O(nativeApp, "mainMenu"), "retain");
        var previousWindows = ObjC.O(ObjC.O(nativeApp, "windowsMenu"), "retain");
        var previousHelp = ObjC.O(ObjC.O(nativeApp, "helpMenu"), "retain");
        try
        {
            using var menu = new Menu(app, new(Role: MenuRole.WindowMenu), new(Role: MenuRole.HelpMenu));
            menu.Install();
            var windows = ObjC.O(ObjC.O(menu.Native, "itemAtIndex:", (nuint)0), "submenu");
            var help = ObjC.O(ObjC.O(menu.Native, "itemAtIndex:", (nuint)1), "submenu");
            check(ObjC.O(nativeApp, "windowsMenu") == windows && ObjC.O(nativeApp, "helpMenu") == help,
                "standard Window and Help roles register their native submenus without displaying them");
            ObjC.O(windows, "retain");
            menu.Dispose();
            bool retired = ObjC.O(nativeApp, "windowsMenu") != windows || ObjC.N(windows, "numberOfItems") == 0;
            ObjC.Release(windows);
            check(retired && ObjC.O(nativeApp, "helpMenu") != help,
                "disposing standard menus detaches Help and clears callbacks from AppKit's retained Window menu");
        }
        finally
        {
            ObjC.V(nativeApp, "setMainMenu:", previous);
            ObjC.V(nativeApp, "setWindowsMenu:", previousWindows);
            ObjC.V(nativeApp, "setHelpMenu:", previousHelp);
            ObjC.Release(previous);
            ObjC.Release(previousWindows);
            ObjC.Release(previousHelp);
        }
    }

    static void RecentMenuFlows(App app, string output, Action<bool, string> check)
    {
        app.ClearRecentDocuments();
        using var standard = new Menu(app, new(Role: MenuRole.AppMenu, Id: "app"), new(Role: MenuRole.FileMenu, Id: "file"));
        string path = Path.GetFullPath(Path.Combine(output, "recent-fixture.txt"));
        File.WriteAllText(path, "Private recent-document fixture");
        app.AddRecentDocument(path);
        app.AddRecentDocument(path);
        var appItem = ObjC.O(standard.Native, "itemAtIndex:", (nuint)0);
        var fileItem = ObjC.O(standard.Native, "itemAtIndex:", (nuint)1);
        var recentItem = ObjC.O(ObjC.O(fileItem, "submenu"), "itemAtIndex:", (nuint)0);
        var recentMenu = ObjC.O(recentItem, "submenu");
        var document = ObjC.O(recentMenu, "itemAtIndex:", (nuint)0);
        check(ObjC.O(appItem, "submenu") != 0 && app.RecentDocuments.SequenceEqual(new[] { path }) && ObjC.Text(ObjC.O(document, "title")) == "recent-fixture.txt",
            "standard app/file roles expand and recent additions deduplicate in a private hidden-app history");
        string? received = null;
        void Open(string value) => received = value;
        app.OpenFile += Open;
        ObjC.V(ObjC.O(document, "target"), "activate:", document);
        app.OpenFile -= Open;
        check(received == path, "native recent-document action delivers OpenFile through its guarded menu callback");
        var clear = ObjC.O(recentMenu, "itemAtIndex:", (nuint)2);
        ObjC.V(ObjC.O(clear, "target"), "activate:", clear);
        check(app.RecentDocuments.Count == 0 && standard.Find("file")?.Submenu?[0].Submenu is { Length: 1 } empty && empty[0].Disabled,
            "Clear Menu removes only the private fixture history and disables the empty recent menu");
    }

    [DllImport("/usr/lib/libSystem.B.dylib", EntryPoint = "_Block_copy")]
    static extern nint BlockCopy(nint block);
    [DllImport("/usr/lib/libSystem.B.dylib", EntryPoint = "_Block_release")]
    static extern void BlockRelease(nint block);
}
