using System.Runtime.InteropServices;
using O = Kumiko.Platform.ObjC;

namespace Kumiko.Platform;

internal sealed unsafe partial class MacApp
{
    const nuint CoreEvent = 0x61657674;
    const nuint OpenApplication = 0x6f617070;
    readonly TaskCompletionSource<bool> launchProvenance = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal Task<bool> LaunchAtLogin => launchProvenance.Task;

    void InitializeLaunchProvenance(bool awaitLaunchEvent)
    {
        // Unpackaged command-line and hidden apps have no Launch Services open
        // event to await. Tests can explicitly deliver a private delayed event.
        var bundle = O.O(O.Class("NSBundle"), "mainBundle");
        var packaged = O.Text(O.O(bundle, "bundlePath")).EndsWith(".app", StringComparison.OrdinalIgnoreCase);
        if (!awaitLaunchEvent && (owner.Options.Hidden || !packaged))
            launchProvenance.TrySetResult(false);
    }

    [UnmanagedCallersOnly]
    static void OpenApplicationEvent(nint self, nint cmd, nint appleEvent, nint reply) => Guard(self, a =>
    {
        // finishLaunching precedes event processing. Only the actual first oapp
        // event can establish launch provenance; later events cannot rewrite it.
        if (O.N(appleEvent, "eventClass") == CoreEvent && O.N(appleEvent, "eventID") == OpenApplication)
            a.launchProvenance.TrySetResult(Desktop.NativeStartupItem.ReadMacLoginEvent(appleEvent));
    });
}
