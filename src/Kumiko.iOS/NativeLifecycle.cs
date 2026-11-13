using Foundation;
using Kumiko.Runtime;
using UIKit;

namespace Kumiko.iOS;

/// <summary>The application's foreground state, as UIKit reports it.</summary>
public enum NativeAppPhase
{
    /// <summary>In the foreground and receiving events.</summary>
    Active,
    /// <summary>Visible but not receiving events, for example during a call or the app switcher.</summary>
    Inactive,
    /// <summary>Not visible. The system may suspend the process at any time after this message.</summary>
    Background
}

/// <summary>Application lifecycle as Kumiko subscriptions.</summary>
public static class NativeLifecycle
{
    /// <summary>
    /// Sends a message for each phase change. Nothing is sent when subscribing, and repeated notifications for
    /// the same phase are dropped. Driven by UIKit notifications; there is no polling.
    /// </summary>
    public static Sub<TMessage> Phases<TMessage>(Func<NativeAppPhase, TMessage> message) where TMessage : notnull
    {
        ArgumentNullException.ThrowIfNull(message);
        return new("kumiko:ios-phase", send =>
        {
            var gate = new object();
            var last = Current();
            void Changed(NativeAppPhase phase)
            {
                lock (gate)
                {
                    if (phase == last)
                        return;
                    last = phase;
                }
                send(message(phase));
            }
            var center = NSNotificationCenter.DefaultCenter;
            NSObject[] observers =
            [
                center.AddObserver(UIApplication.WillResignActiveNotification, _ => Changed(NativeAppPhase.Inactive)),
                center.AddObserver(UIApplication.DidEnterBackgroundNotification, _ => Changed(NativeAppPhase.Background)),
                center.AddObserver(UIApplication.WillEnterForegroundNotification, _ => Changed(NativeAppPhase.Inactive)),
                center.AddObserver(UIApplication.DidBecomeActiveNotification, _ => Changed(NativeAppPhase.Active))
            ];
            return new Observers(observers);
        });
    }

    static NativeAppPhase Current()
    {
        if (!NSThread.IsMain)
            return NativeAppPhase.Active;
        // A process without UIApplicationMain (such as a test executable) has no shared application.
        UIApplication? application = UIApplication.SharedApplication;
        return application?.ApplicationState switch
        {
            UIApplicationState.Inactive => NativeAppPhase.Inactive,
            UIApplicationState.Background => NativeAppPhase.Background,
            _ => NativeAppPhase.Active
        };
    }

    sealed class Observers(NSObject[] observers) : IDisposable
    {
        public void Dispose()
        {
            foreach (var observer in observers)
            {
                NSNotificationCenter.DefaultCenter.RemoveObserver(observer);
                observer.Dispose();
            }
        }
    }
}
