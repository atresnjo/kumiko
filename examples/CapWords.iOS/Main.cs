using Foundation;
using Kumiko.iOS;
using Kumiko.Runtime;
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
        if (scene is not UIWindowScene windowScene)
            return;
        Window = new UIWindow(windowScene)
        {
            RootViewController = new CapWordsController()
        };
        Window.MakeKeyAndVisible();
    }

    public override void DidDisconnect(UIScene scene)
    {
        var controller = Window?.RootViewController;
        Window?.Dispose();
        Window = null;
        controller?.Dispose();
    }
}

internal sealed class CapWordsController : UIViewController
{
    IProgram? program;
    NativeView? content;
    Action? updateChrome;

    public override void ViewDidLoad()
    {
        base.ViewDidLoad();
        var gallery = AnimationGalleryApp.CreateProgram();
        if (Environment.GetEnvironmentVariable("KUMIKO_STUDY") == "alma")
            gallery.Send(new GalleryMessage.Open(GalleryScreen.Alma));
        if (Environment.GetEnvironmentVariable("KUMIKO_STUDY") == "slack")
            gallery.Send(new GalleryMessage.Open(GalleryScreen.Slack));
        if (Environment.GetEnvironmentVariable("KUMIKO_STUDY") == "opal")
            gallery.Send(new GalleryMessage.Open(GalleryScreen.Opal));
        if (Environment.GetEnvironmentVariable("KUMIKO_STUDY") == "unwind")
            gallery.Send(new GalleryMessage.Open(GalleryScreen.Unwind));
        program = gallery;
        content = new NativeView(program);
        updateChrome = () =>
        {
            var opal = gallery.Model.Screen == GalleryScreen.Opal;
            var unwind = gallery.Model.Screen == GalleryScreen.Unwind;
            View!.BackgroundColor = opal ? UIColor.Black : unwind ? UIColor.FromRGB(0xf4, 0xf2, 0xee) : UIColor.FromRGB(248, 248, 248);
            OverrideUserInterfaceStyle = opal ? UIUserInterfaceStyle.Dark : UIUserInterfaceStyle.Unspecified;
            SetNeedsStatusBarAppearanceUpdate();
        };
        gallery.ModelChanged += updateChrome;
        updateChrome();
        View!.AddSubview(content);
    }

    public override void ViewDidLayoutSubviews()
    {
        base.ViewDidLayoutSubviews();
        if (content is not null)
            content.Frame = View!.SafeAreaLayoutGuide.LayoutFrame;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (program is not null && updateChrome is not null)
                program.ModelChanged -= updateChrome;
            content?.Dispose();
            program?.Dispose();
        }
        base.Dispose(disposing);
    }
}
