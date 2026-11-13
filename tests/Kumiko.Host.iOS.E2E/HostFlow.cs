using Kumiko.iOS;
using Kumiko.Runtime;
using UIKit;
using static Probe;

/// <summary>Application startup validation and the reusable controller host, all in hidden windows.</summary>
internal static class HostFlow
{
    internal static void Run(string output, List<string> checks)
    {
        Startup(checks);
        SafeArea(output, checks);
        EdgeToEdge(output, checks);
        FailingFactory(checks);
    }

    static void Startup(List<string> checks)
    {
        Throws<ArgumentNullException>(() => NativeApplication.Run([], null!), "Run rejects a missing program factory");
        var misconfigured = Throws<InvalidOperationException>(() => NativeApplication.Run([], HostApp.Create),
            "Run rejects an Info.plist without KumikoSceneDelegate before UIKit starts");
        Require(misconfigured.Message.Contains("KumikoSceneDelegate", StringComparison.Ordinal), "The startup error names the required scene delegate");
        var worker = Task.Run(() => Throws<InvalidOperationException>(() => NativeApplication.Run([], HostApp.Create),
            "Run rejects a worker thread"));
        Require(worker.Wait(TimeSpan.FromSeconds(5)) && worker.Result.Message.Contains("main thread", StringComparison.Ordinal),
            "The worker-thread error names the main thread");
        checks.Add("startup-validation");
    }

    static void SafeArea(string output, List<string> checks)
    {
        var created = 0;
        Program<HostModel, HostMessage>? program = null;
        var disposed = false;
        var controller = new NativeViewController(() =>
        {
            created++;
            program = HostApp.CreateProgram();
            program.Disposing += () => disposed = true;
            return program;
        }, new NativeHostOptions { Background = UIColor.SystemGroupedBackground, InterfaceStyle = UIUserInterfaceStyle.Dark });
        controller.AdditionalSafeAreaInsets = new(47, 0, 34, 0);
        using (var window = Window(controller))
        {
            Settle(controller);
            Require(created == 1 && ReferenceEquals(controller.Program, program), "The controller creates its program exactly once");
            var content = controller.Content ?? throw new InvalidOperationException("The controller has no native content.");
            Require(content.Frame == controller.View!.SafeAreaLayoutGuide.LayoutFrame && content.Frame.Y == 47,
                $"Default hosting keeps content inside the safe area (content {content.Frame}, safe {controller.View.SafeAreaLayoutGuide.LayoutFrame}, insets {controller.View.SafeAreaInsets})");
            Require(controller.View.BackgroundColor == UIColor.SystemGroupedBackground, "The background option fills the controller view");
            Require(controller.OverrideUserInterfaceStyle == UIUserInterfaceStyle.Dark, "The interface style option overrides traits");
            Tap(content, "increment");
            PumpUntil(() => program!.Model.Count == 1, "A native tap reaches the hosted program");
            Settle(controller);
            Require(Text(content).Contains("Count 1", StringComparison.Ordinal), "The hosted view renders the updated model");
            controller.View.SetNeedsLayout();
            controller.View.LayoutIfNeeded();
            Require(created == 1, "Relayout does not recreate the program");
            Capture(controller.View, Path.Combine(output, "host-safe-area.png"));
            Detach(window, controller);
        }
        controller.Dispose();
        Require(disposed && controller.Program is null && controller.Content is null, "Disposing the controller disposes its program and native view");
        checks.Add("controller-safe-area");
    }

    static void EdgeToEdge(string output, List<string> checks)
    {
        var controller = new NativeViewController(HostApp.Create, new NativeHostOptions { EdgeToEdge = true });
        controller.AdditionalSafeAreaInsets = new(47, 0, 34, 0);
        using (var window = Window(controller))
        {
            Settle(controller);
            Require(controller.Content!.Frame == controller.View!.Bounds, "Edge-to-edge hosting fills the controller bounds");
            Require(controller.Content.SafeAreaInsets.Top == 47, "Edge-to-edge content still reports the real safe-area insets");
            Capture(controller.View, Path.Combine(output, "host-edge-to-edge.png"));
            Detach(window, controller);
        }
        controller.Dispose();
        checks.Add("controller-edge-to-edge");
    }

    static void FailingFactory(List<string> checks)
    {
        Exception? reported = null;
        var controller = new NativeViewController(() => throw new InvalidOperationException("factory failed"));
        controller.UnhandledException += error => reported = error;
        using (var window = Window(controller))
        {
            Require(reported?.Message == "factory failed", "A failing factory is reported instead of escaping ViewDidLoad");
            Require(controller.Program is null && controller.Content is null, "A failed startup leaves no partial host");
            Detach(window, controller);
        }
        controller.Dispose();
        checks.Add("factory-failure");
    }
}
