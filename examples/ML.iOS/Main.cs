using Foundation;
using UIKit;

internal static class EntryPoint
{
    static void Main(string[] args) => UIApplication.Main(args, null, typeof(AppDelegate));
}

[Register("AppDelegate")]
internal sealed class AppDelegate : UIApplicationDelegate
{
    public override bool FinishedLaunching(UIApplication application, NSDictionary? launchOptions) => true;
}

[Register("SceneDelegate")]
internal sealed class SceneDelegate : UIWindowSceneDelegate
{
    public override UIWindow? Window
    {
        get; set;
    }

    public override void WillConnect(UIScene scene, UISceneSession session, UISceneConnectionOptions connectionOptions)
    {
        try
        {
            if (scene is not UIWindowScene windowScene)
                return;
            Window = new UIWindow(windowScene)
            {
                RootViewController = new MLController()
            };
            Window.MakeKeyAndVisible();
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
        }
    }

    public override void DidDisconnect(UIScene scene)
    {
        var controller = Window?.RootViewController;
        Window?.Dispose();
        Window = null;
        controller?.Dispose();
    }
}
