using System.Runtime.InteropServices;
using Kumiko.Platform;
using Kumiko.UI;
using O = Kumiko.Platform.ObjC;

namespace Kumiko.Desktop;

internal sealed unsafe class MacTrayBackend : ITrayBackend, ITrayAppearance
{
    static readonly Dictionary<nint, MacTrayBackend> instances = new();
    readonly App app;
    readonly Action<bool> click;
    readonly nint item, target;
    readonly bool fixture;
    readonly NativeAppearance appearance;
    bool disposed;
    public bool IsDark => appearance.IsDark;
    public event Action<bool>? AppearanceChanged
    {
        add => appearance.Changed += value;
        remove => appearance.Changed -= value;
    }
    internal MacTrayBackend(App app, Action<bool> click, nint fixtureItem = 0)
    {
        this.app = app;
        this.click = click;
        using var pool = new AutoreleasePool();
        fixture = fixtureItem != 0;
        item = fixture ? O.O(fixtureItem, "retain") : O.O(O.O(O.O(O.Class("NSStatusBar"), "systemStatusBar"), "statusItemWithLength:", -1d), "retain");
        var cls = O.Define("KumikoTrayTarget", "NSObject", ("activate:", (nint)(delegate* unmanaged<nint, nint, nint, void>)&Activate, "v@:@"));
        target = O.O(cls, "new");
        instances[target] = this;
        var button = O.O(item, "button");
        O.V(button, "setTarget:", target);
        O.V(button, "setAction:", O.Sel("activate:"));
        O.V(button, "sendActionOn:", (nuint)((1 << 2) | (1 << 4)));
        try
        {
            appearance = new(app, button);
        }
        catch
        {
            instances.Remove(target);
            O.V(button, "setTarget:", (nint)0);
            if (!fixture)
                O.V(O.O(O.Class("NSStatusBar"), "systemStatusBar"), "removeStatusItem:", item);
            O.Release(target);
            O.Release(item);
            throw;
        }
    }

    public Rect Bounds
    {
        get
        {
            using var pool = new AutoreleasePool();
            var button = O.O(item, "button");
            var frame = O.R(O.O(button, "window"), "convertRectToScreen:", O.R(button, "frame"));
            var primary = O.R(O.O(O.O(O.Class("NSScreen"), "screens"), "objectAtIndex:", (nuint)0), "frame");
            return new((float)frame.Origin.X, (float)(primary.Size.Height - frame.Origin.Y - frame.Size.Height), (float)frame.Size.Width, (float)frame.Size.Height);
        }
    }

    public void Update(TrayOptions options)
    {
        using var pool = new AutoreleasePool();
        var button = O.O(item, "button");
        O.V(button, "setTitle:", O.TempString(options.Title));
        O.V(button, "setToolTip:", O.TempString(options.ToolTip));
        nint image = 0;
        if (options.Icon is { } bytes)
        {
            fixed (byte* p = bytes)
                image = O.O(O.O(O.Class("NSImage"), "alloc"), "initWithData:", O.O(O.Class("NSData"), "dataWithBytes:length:", (nint)p, (nuint)bytes.Length));
            if (image == 0)
                throw new ArgumentException("AppKit could not decode the tray PNG.");
            if (options.Icon2x is { } retina)
            {
                fixed (byte* p = retina)
                {
                    var representation = O.O(O.Class("NSBitmapImageRep"), "imageRepWithData:", O.O(O.Class("NSData"), "dataWithBytes:length:", (nint)p, (nuint)retina.Length));
                    if (representation == 0)
                    {
                        O.Release(image);
                        throw new ArgumentException("AppKit could not decode the 2x PNG.");
                    }

                    O.V(representation, "setSize:", new NativeSize(18, 18));
                    O.V(image, "addRepresentation:", representation);
                }
            }

            O.V(image, "setSize:", new NativeSize(18, 18));
            O.V(image, "setTemplate:", (byte)(options.Template ? 1 : 0));
        }

        O.V(button, "setImage:", image);
        O.Release(image);
    }

    [UnmanagedCallersOnly]
    static void Activate(nint self, nint cmd, nint sender)
    {
        if (!instances.TryGetValue(self, out var owner))
            return;
        try
        {
            var ev = O.O(O.O(O.Class("NSApplication"), "sharedApplication"), "currentEvent");
            owner.click(O.N(ev, "type") == 4);
        }
        catch (Exception ex)
        {
            owner.app.Report(ex);
        }
    }

    public void PopUpMenu(Menu menu) => O.V(item, "popUpStatusItemMenu:", menu.Native);
    public void Dispose()
    {
        app.CheckThread();
        if (disposed)
            return;
        disposed = true;
        appearance.Dispose();
        instances.Remove(target);
        O.V(O.O(item, "button"), "setTarget:", (nint)0);
        if (!fixture)
            O.V(O.O(O.Class("NSStatusBar"), "systemStatusBar"), "removeStatusItem:", item);
        O.Release(target);
        O.Release(item);
    }
}
