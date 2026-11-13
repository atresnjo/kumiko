using CoreGraphics;
using Foundation;
using Kumiko.iOS;
using Kumiko.Runtime;
using UIKit;

internal static class EntryPoint
{
    internal static string Output => Environment.GetEnvironmentVariable("KUMIKO_CAPWORDS_ARTIFACTS")
        ?? throw new InvalidOperationException("Set KUMIKO_CAPWORDS_ARTIFACTS.");

    static int Main(string[] args)
    {
        try
        {
            if (Environment.GetEnvironmentVariable("KUMIKO_CAPWORDS_LIVE") == "1")
                UIApplication.Main(args, null, typeof(AppDelegate));
            else if (Environment.GetEnvironmentVariable("KUMIKO_UNWIND_ONLY") == "1")
                UnwindFlow.Run(Output);
            else
            {
                if (Environment.GetEnvironmentVariable("KUMIKO_OPAL_ONLY") != "1")
                {
                    CapWordsFlow.Run(Output);
                    GalleryFlow.Run(Output);
                    SlackFlow.Run(Output);
                    UnwindFlow.Run(Output);
                }
                OpalFlow.Run(Output);
            }
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }
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
        get;
        set;
    }

    public override void WillConnect(UIScene scene, UISceneSession session, UISceneConnectionOptions connectionOptions)
    {
        if (scene is not UIWindowScene windowScene)
            return;
        Window = new UIWindow(windowScene) { RootViewController = new LiveController() };
        Window.MakeKeyAndVisible();
    }
}

internal sealed class LiveController : UIViewController
{
    readonly Program<CapWordsModel, CapWordsMessage> program = CapWordsApp.CreateProgram();
    NativeView? content;
    bool started;

    public override void ViewDidLoad()
    {
        base.ViewDidLoad();
        content = new NativeView(program);
        View!.AddSubview(content);
    }

    public override void ViewDidLayoutSubviews()
    {
        base.ViewDidLayoutSubviews();
        if (content is not null)
            content.Frame = View!.SafeAreaLayoutGuide.LayoutFrame;
    }

    public override async void ViewDidAppear(bool animated)
    {
        base.ViewDidAppear(animated);
        if (started)
            return;
        started = true;
        try
        {
            await Task.Delay(300);
            if (Environment.GetEnvironmentVariable("KUMIKO_SLACK_PERFORMANCE") == "1")
            {
                content!.RemoveFromSuperview();
                await SlackPerformanceFlow.Run(View!, content.Frame, EntryPoint.Output);
            }
            else if (Environment.GetEnvironmentVariable("KUMIKO_OPAL_PERFORMANCE") == "1")
            {
                content!.RemoveFromSuperview();
                await OpalPerformanceFlow.Run(View!, content.Frame, EntryPoint.Output);
            }
            else
            {
                if (Environment.GetEnvironmentVariable("KUMIKO_OPAL_ONLY") != "1")
                {
                    await CapWordsLiveFlow.Run(content!, program, EntryPoint.Output);
                    await GalleryLiveFlow.Run(View!, content!.Frame, EntryPoint.Output);
                    await SlackLiveFlow.Run(View!, content!.Frame, EntryPoint.Output);
                }
                await OpalLiveFlow.Run(View!, content!.Frame, EntryPoint.Output);
            }
            Environment.Exit(0);
        }
        catch (Exception error)
        {
            File.WriteAllText(Path.Combine(EntryPoint.Output, "live-error.txt"), error.ToString());
            Environment.Exit(1);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            content?.Dispose();
            program.Dispose();
        }
        base.Dispose(disposing);
    }
}
