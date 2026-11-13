using Kumiko;
using Kumiko.Platform;
using O = Kumiko.Platform.ObjC;

internal static partial class ReviewWindows
{
    static void Ownership(App app, Action<bool, string> check)
    {
        using var parent = app.NewWindow(new(Visible: false), c => c.Text("Parent"));
        using var child = app.NewWindow(new(Visible: false)
        {
            Parent = parent
        }, c => c.Text("Child"));
        check(O.O(child.NativeHandle, "parentWindow") == 0 && !child.IsVisible && !parent.IsVisible,
            "hidden child construction leaves both windows unmapped and detached");
        child.SetOpacity(.8f);
        check(O.O(child.NativeHandle, "parentWindow") == 0, "reconfiguring a hidden child does not attach it");
        var backend = MacWindow.Find(child.NativeHandle)!;
        var endpoint = new Presentation();
        backend.Presentation = endpoint;
        backend.Show();
        backend.Show();
        backend.Hide();
        backend.Hide();
        check(endpoint.Events.SequenceEqual(new[] { "attach", "show", "show", "detach", "hide", "hide" }),
            "child Show attaches once and Hide detaches before ordering out through private endpoints");
        check(O.O(child.NativeHandle, "parentWindow") == 0 && O.B(parent.NativeHandle, "isVisible") == 0,
            "presentation fixtures never attach or order actual native windows");
    }

    static void Transitions(App app, Action<bool, string> check)
    {
        using var window = app.NewWindow(new(Visible: false), c => c.Text("Transitions"));
        var backend = MacWindow.Find(window.NativeHandle)!;
        var endpoint = new Presentation { Visible = true };
        backend.Presentation = endpoint;
        var state = new WindowState(30, 40, 450, 310, true, true);
        backend.Restore(state);
        check(endpoint.FullScreenRequests == 0 && endpoint.ZoomRequests == 0,
            "native Restore applies geometry without duplicating the public deferred state transitions");
        backend.Perform(WindowAction.Maximize);
        backend.Perform(WindowAction.Maximize);
        check(endpoint.ZoomRequests == 1, "repeated maximize requests produce one native zoom");
        backend.Perform(WindowAction.EnterFullScreen);
        backend.Perform(WindowAction.EnterFullScreen);
        check(endpoint.FullScreenRequests == 1, "repeated enter requests do not toggle an in-flight full-screen transition");
        backend.Perform(WindowAction.LeaveFullScreen);
        check(endpoint.FullScreenRequests == 1, "opposite full-screen request waits for the in-flight transition");
        endpoint.FullScreen = true;
        var target = O.O(window.NativeHandle, "delegate");
        O.V(target, "windowDidEnterFullScreen:", (nint)0);
        check(endpoint.FullScreenRequests == 2, "completion issues exactly one queued opposite full-screen transition");
        backend.Perform(WindowAction.LeaveFullScreen);
        check(endpoint.FullScreenRequests == 2, "pending full-screen exit is not toggled twice");
        endpoint.FullScreen = false;
        O.V(target, "windowDidExitFullScreen:", (nint)0);
        backend.Perform(WindowAction.EnterFullScreen);
        O.V(target, "windowDidFailToEnterFullScreen:", window.NativeHandle);
        backend.Perform(WindowAction.EnterFullScreen);
        check(endpoint.FullScreenRequests == 4, "failed full-screen entry clears the pending guard so a later request can retry");
        check(O.B(window.NativeHandle, "isVisible") == 0, "transition fixtures never map the actual native window");
    }

    sealed class Presentation : IMacWindowPresentation
    {
        internal readonly List<string> Events = new();
        internal bool Visible, FullScreen, Maximized;
        internal int FullScreenRequests, ZoomRequests;
        public bool IsVisible(nint handle) => Visible;
        public bool IsFullScreen(nint handle) => FullScreen;
        public bool IsMaximized(nint handle) => Maximized;
        public void Show(nint handle, nint parent, bool modal)
        {
            Events.Add("show");
            Visible = true;
        }

        public void Hide(nint handle)
        {
            Events.Add("hide");
            Visible = false;
        }

        public void Attach(nint parent, nint child) => Events.Add("attach");
        public void Detach(nint parent, nint child) => Events.Add("detach");
        public void ToggleFullScreen(nint handle) => FullScreenRequests++;
        public void ToggleMaximize(nint handle)
        {
            ZoomRequests++;
            Maximized = !Maximized;
        }
    }
}
