using Kumiko.iOS;
using Kumiko.Runtime;
using UIKit;
using static Probe;

/// <summary>Application phase messages from UIKit notifications, without polling.</summary>
internal static class LifecycleFlow
{
    internal static void Run(List<string> checks)
    {
        using var first = HostApp.CreateProgram();
        using var second = HostApp.CreateProgram();
        using var firstView = new NativeView(first) { Frame = new(0, 0, 393, 600) };
        using var secondView = new NativeView(second) { Frame = new(0, 0, 393, 600) };
        firstView.Render();
        secondView.Render();
        Pump(TimeSpan.FromMilliseconds(60));
        Require(first.Model.Phases.IsEmpty, "Subscribing does not invent a transition");
        Post(UIApplication.WillResignActiveNotification);
        Post(UIApplication.DidEnterBackgroundNotification);
        Post(UIApplication.DidEnterBackgroundNotification);
        Post(UIApplication.WillEnterForegroundNotification);
        Post(UIApplication.DidBecomeActiveNotification);
        Post(UIApplication.DidBecomeActiveNotification);
        NativeAppPhase[] expected = [NativeAppPhase.Inactive, NativeAppPhase.Background, NativeAppPhase.Inactive, NativeAppPhase.Active];
        PumpUntil(() => first.Model.Phases.Length >= 4 && second.Model.Phases.Length >= 4, "Both programs receive the phase sequence");
        Pump(TimeSpan.FromMilliseconds(60));
        Require(first.Model.Phases.SequenceEqual(expected), "Phases arrive in order and repeated notifications are not duplicated");
        Require(second.Model.Phases.SequenceEqual(expected), "A second program receives the same transitions independently");
        var worker = Task.Run(() => Post(UIApplication.WillResignActiveNotification));
        Require(worker.Wait(TimeSpan.FromSeconds(5)), "A worker can post a lifecycle notification");
        PumpUntil(() => first.Model.Phases.Length == 5, "A transition posted from a worker reaches the program");
        first.Send(new HostMessage.SetTracking(false));
        firstView.Render();
        Post(UIApplication.DidBecomeActiveNotification);
        PumpUntil(() => second.Model.Phases.Length == 6, "The still-subscribed program sees the next transition");
        Pump(TimeSpan.FromMilliseconds(60));
        Require(first.Model.Phases.Length == 5, "A removed phase subscription stops sending");
        checks.Add("lifecycle-phases");
    }
}
