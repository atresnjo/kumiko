using System.Runtime.InteropServices;
using O = Kumiko.Platform.ObjC;

namespace Kumiko.Platform;

internal sealed unsafe partial class MacApp
{
    static readonly Dictionary<nint, MacApp> delegates = new();
    nint appDelegate, previousDelegate;
    bool launching = true;
    internal Action<bool>? TerminationReply
    {
        get; set;
    }
    const nuint GetUrl = 0x4755524c;
    void InstallDelegate()
    {
        var cls = O.Define("KumikoApplicationDelegate", "NSObject", ("applicationWillFinishLaunching:", (nint)(delegate* unmanaged<nint, nint, nint, void>)&WillFinishLaunching, "v@:@"), ("applicationDidFinishLaunching:", (nint)(delegate* unmanaged<nint, nint, nint, void>)&DidFinishLaunching, "v@:@"), ("applicationShouldTerminate:", (nint)(delegate* unmanaged<nint, nint, nint, nuint>)&ShouldTerminate, "Q@:@"), ("applicationShouldHandleReopen:hasVisibleWindows:", (nint)(delegate* unmanaged<nint, nint, nint, byte, byte>)&Reopen, "B@:@B"), ("applicationDidBecomeActive:", (nint)(delegate* unmanaged<nint, nint, nint, void>)&BecameActive, "v@:@"), ("applicationDidResignActive:", (nint)(delegate* unmanaged<nint, nint, nint, void>)&ResignedActive, "v@:@"), ("application:openURLs:", (nint)(delegate* unmanaged<nint, nint, nint, nint, void>)&OpenUrls, "v@:@@"), ("application:openFiles:", (nint)(delegate* unmanaged<nint, nint, nint, nint, void>)&OpenFiles, "v@:@@"), ("handleOpenApplication:withReplyEvent:", (nint)(delegate* unmanaged<nint, nint, nint, nint, void>)&OpenApplicationEvent, "v@:@@"), ("handleGetURL:withReplyEvent:", (nint)(delegate* unmanaged<nint, nint, nint, nint, void>)&GetURL, "v@:@@"));
        previousDelegate = O.O(O.O(app, "delegate"), "retain");
        appDelegate = O.O(cls, "new");
        delegates.Add(appDelegate, this);
        O.V(app, "setDelegate:", appDelegate);
        InstallOpenEvents();
    }

    void InstallOpenEvents()
    {
        var manager = O.O(O.Class("NSAppleEventManager"), "sharedAppleEventManager");
        O.V(manager, "setEventHandler:andSelector:forEventClass:andEventID:", appDelegate, O.Sel("handleGetURL:withReplyEvent:"), GetUrl, GetUrl);
        O.V(manager, "setEventHandler:andSelector:forEventClass:andEventID:", appDelegate, O.Sel("handleOpenApplication:withReplyEvent:"), CoreEvent, OpenApplication);
    }

    static void Guard(nint self, Action<MacApp> callback)
    {
        if (!delegates.TryGetValue(self, out var instance))
            return;
        try
        {
            callback(instance);
        }
        catch (Exception ex)
        {
            instance.owner.Report(ex);
        }
    }

    [UnmanagedCallersOnly]
    static nuint ShouldTerminate(nint self, nint cmd, nint sender)
    {
        Guard(self, a => a.owner.Dispatch(() => a.owner.RequestNativeQuit(approved =>
        {
            if (a.TerminationReply is { } reply)
                reply(approved);
            else
                O.V(a.app, "replyToApplicationShouldTerminate:", (byte)(approved ? 1 : 0));
        })));
        return 2; // NSTerminateLater: reply after model close messages have resolved.
    }

    [UnmanagedCallersOnly]
    static void WillFinishLaunching(nint self, nint cmd, nint notification) => Guard(self, a =>
    {
        a.launching = true;
        a.InstallOpenEvents();
    });
    [UnmanagedCallersOnly]
    static void DidFinishLaunching(nint self, nint cmd, nint notification) => Guard(self, a => a.launching = false);

    void DeliverOpen(string value, bool url)
    {
        if (launching)
            owner.DeliverLaunchOpen(value, url);
        else
            owner.DeliverOpen(value, url);
    }

    [UnmanagedCallersOnly]
    static byte Reopen(nint self, nint cmd, nint sender, byte visible)
    {
        Guard(self, a => a.owner.NativeActivated());
        return 1;
    }

    [UnmanagedCallersOnly]
    static void BecameActive(nint self, nint cmd, nint notification) => Guard(self, a => a.owner.NativeActiveChanged(true));
    [UnmanagedCallersOnly]
    static void ResignedActive(nint self, nint cmd, nint notification) => Guard(self, a => a.owner.NativeActiveChanged(false));
    [UnmanagedCallersOnly]
    static void OpenUrls(nint self, nint cmd, nint sender, nint urls) => Guard(self, a =>
    {
        for (nuint i = 0; i < O.N(urls, "count"); i++)
        {
            var url = O.O(urls, "objectAtIndex:", i);
            var file = O.B(url, "isFileURL") != 0;
            a.DeliverOpen(O.Text(O.O(url, file ? "path" : "absoluteString")), !file);
        }
    });
    [UnmanagedCallersOnly]
    static void OpenFiles(nint self, nint cmd, nint sender, nint files) => Guard(self, a =>
    {
        for (nuint i = 0; i < O.N(files, "count"); i++)
            a.DeliverOpen(O.Text(O.O(files, "objectAtIndex:", i)), false);
        O.V(a.app, "replyToOpenOrPrint:", (nuint)0);
    });
    [UnmanagedCallersOnly]
    static void GetURL(nint self, nint cmd, nint appleEvent, nint reply) => Guard(self, a =>
    {
        var descriptor = O.O(appleEvent, "paramDescriptorForKeyword:", (nuint)0x2d2d2d2d);
        var value = O.Text(O.O(descriptor, "stringValue"));
        if (value.Length != 0)
            a.DeliverOpen(value, true);
    });
    void DisposeDelegate()
    {
        using var pool = new AutoreleasePool();
        launchProvenance.TrySetCanceled();
        O.V(O.O(O.Class("NSAppleEventManager"), "sharedAppleEventManager"), "removeEventHandlerForEventClass:andEventID:", GetUrl, GetUrl);
        O.V(O.O(O.Class("NSAppleEventManager"), "sharedAppleEventManager"), "removeEventHandlerForEventClass:andEventID:", CoreEvent, OpenApplication);
        if (O.O(app, "delegate") == appDelegate)
            O.V(app, "setDelegate:", previousDelegate);
        delegates.Remove(appDelegate);
        O.Release(appDelegate);
        O.Release(previousDelegate);
    }
}
