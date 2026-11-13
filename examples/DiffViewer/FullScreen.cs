using Kumiko;

namespace DiffViewer;

/// <summary>The window's full screen, as zen mode holds it. Calls arrive from subscriptions on the UI thread.</summary>
internal interface IFullScreen
{
    /// <summary>
    /// Enters full screen until the handle is disposed; <paramref name="left"/> runs when the window leaves full
    /// screen on its own first. A window that was already full screen stays so after the release.
    /// </summary>
    IDisposable Hold(Action left);
}

/// <summary>Offscreen runs and tests have no native window; zen mode only changes the layout there.</summary>
internal sealed class NoFullScreen : IFullScreen
{
    public IDisposable Hold(Action left) => new Release(static () => { });
}

/// <summary>
/// The interactive app's window, attached once it is created. Subscriptions start and stop while Kumiko forbids
/// window calls, so the window is entered and released from the App's queue right after.
/// </summary>
internal sealed class WindowFullScreen(App app) : IFullScreen
{
    public Window? Window
    {
        get; set;
    }

    public IDisposable Hold(Action left)
    {
        var hold = new Holding(left);
        app.Dispatch(() => hold.Enter(Window));
        return new Release(() => app.Dispatch(hold.Leave));
    }

    sealed class Holding(Action left)
    {
        Window? window;
        bool already, released;

        public void Enter(Window? target)
        {
            // A hold released before it ran, a missing window and a hidden one have nothing to enter.
            if (released || target is not { IsVisible: true })
                return;
            window = target;
            already = target.IsFullScreen;
            target.LeftFullScreen += left;
            if (!already)
                target.EnterFullScreen();
        }

        public void Leave()
        {
            released = true;
            if (window is not { } target)
                return;
            target.LeftFullScreen -= left;
            // A closing window has nothing left to restore. A pending entry is reversed once it completes.
            if (!already && target.IsVisible)
                target.LeaveFullScreen();
        }
    }
}

internal sealed class Release(Action release) : IDisposable
{
    public void Dispose() => release();
}
