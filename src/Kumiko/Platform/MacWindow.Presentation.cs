using O = Kumiko.Platform.ObjC;

namespace Kumiko.Platform;

// The publication boundary is replaceable for hidden native integration flows.
// Geometry, delegates, state coordination and lifetimes still use real NSWindows.
internal interface IMacWindowPresentation
{
    bool IsVisible(nint handle);
    bool IsFullScreen(nint handle);
    bool IsMaximized(nint handle);
    void Show(nint handle, nint parent, bool modal);
    void Hide(nint handle);
    void Attach(nint parent, nint child);
    void Detach(nint parent, nint child);
    void ToggleFullScreen(nint handle);
    void ToggleMaximize(nint handle);
}

internal sealed class MacWindowPresentation : IMacWindowPresentation
{
    internal static readonly MacWindowPresentation Instance = new();
    public bool IsVisible(nint handle) => O.B(handle, "isVisible") != 0;
    public bool IsFullScreen(nint handle) => (O.N(handle, "styleMask") & (1u << 14)) != 0;
    public bool IsMaximized(nint handle) => O.B(handle, "isZoomed") != 0;
    public void Show(nint handle, nint parent, bool modal)
    {
        if (modal)
        {
            if (O.O(handle, "sheetParent") == 0)
                O.V(parent, "beginSheet:completionHandler:", handle, (nint)0);
        }
        else
            O.V(handle, "makeKeyAndOrderFront:", (nint)0);
        // A nonactivating panel must never activate its owning application.
        if ((O.N(handle, "styleMask") & (1u << 7)) == 0)
            O.V(O.O(O.Class("NSApplication"), "sharedApplication"), "activateIgnoringOtherApps:", (byte)1);
    }

    public void Hide(nint handle)
    {
        if (O.O(handle, "sheetParent") is var parent && parent != 0)
            O.V(parent, "endSheet:", handle);
        O.V(handle, "orderOut:", (nint)0);
    }

    public void Attach(nint parent, nint child) => O.V(parent, "addChildWindow:ordered:", child, (nuint)1);
    public void Detach(nint parent, nint child) => O.V(parent, "removeChildWindow:", child);
    public void ToggleFullScreen(nint handle) => O.V(handle, "toggleFullScreen:", (nint)0);
    public void ToggleMaximize(nint handle) => O.V(handle, "zoom:", (nint)0);
}
