using System.Globalization;
using System.Text.Json;
using CoreGraphics;
using CRMLite;
using Kumiko.iOS;
using Kumiko.Runtime;
using UIKit;

internal static class ResponsiveRtlFlow
{
    internal static void Run(string output)
    {
        var previous = CultureInfo.CurrentCulture;
        var results = new List<string>();
        var passed = true;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("he-IL");
            var initial = Data.Initial();
            using (var program = CrmApp.CreateProgram(initial, new FakeCrmBackend(seed: initial).Services()))
            {
                program.Send(new CrmMsg.OpenCompany("northwind"));
                Scene(program, "rtl-crm", view =>
                {
                    var directory = ResponsiveEnvironmentFlow.Find<UIScrollView>(view, "directory-list");
                    var stack = ResponsiveEnvironmentFlow.Find<UIView>(view, "crm-stack");
                    var list = directory.ConvertRectToView(directory.Bounds, view);
                    var detail = stack.ConvertRectToView(stack.Bounds, view);
                    Record(list.X >= detail.Right, "RTL directory occupies the trailing right pane without overlap");
                    Record(Math.Abs(detail.X - 40) < .1 && Math.Abs(list.Right - (1024 - 12)) < .1,
                        "CRM preserves physical left=40 and right=12 safe-area padding in RTL");
                    var bar = ResponsiveFlow.All(view).OfType<UITabBar>().Single();
                    var barRect = bar.ConvertRectToView(bar.Bounds, view);
                    Record(detail.Y >= 20 && detail.Bottom <= 900 + .1 && barRect.Y <= 900 - 24 && barRect.Bottom >= 900 - .1,
                        "RTL details respect the top inset; the floating tab bar covers the bottom inset");
                });
            }
            using (var program = ResponsiveApp.Create())
                Scene(program, "rtl-environment", view =>
                {
                    var select = ResponsiveEnvironmentFlow.Find<UIButton>(view, "select");
                    var save = ResponsiveEnvironmentFlow.Find<UIButton>(view, "save");
                    var list = select.ConvertRectToView(select.Bounds, view);
                    var detail = save.ConvertRectToView(save.Bounds, view);
                    Record(list.X > detail.Right, "responsive probe inherits culture direction for pane ordering");
                    var half = (1024 - 40 - 12 - Kumiko.UI.Space.S3) / 2f;
                    Record(Math.Abs(list.Right - (1024 - 12)) < .1 && Math.Abs(detail.Right - (40 + half)) < .1,
                        "responsive probe preserves asymmetric physical safe-area edges in RTL");
                });
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
        File.WriteAllText(Path.Combine(output, "responsive-rtl-report.json"), JsonSerializer.Serialize(
            new ResponsiveReport(passed, results), ResponsiveJson.Default.ResponsiveReport));
        ResponsiveEnvironmentFlow.Require(passed, string.Join("; ", results.Where(result => result.StartsWith("FAIL", StringComparison.Ordinal))));

        void Record(bool condition, string name)
        {
            passed &= condition;
            results.Add($"{(condition ? "PASS" : "FAIL")} {name}");
            Console.WriteLine(results[^1]);
        }
        void Scene(IProgram program, string name, Action<NativeView> verify)
        {
            using var parent = new UIViewController();
#pragma warning disable CA1422 // Private hidden controller integration, never shown or activated.
            using var window = new UIWindow(new CGRect(0, 0, 1024, 900))
            {
                RootViewController = parent,
                Hidden = true,
                OverrideUserInterfaceStyle = UIUserInterfaceStyle.Light
            };
#pragma warning restore CA1422
            parent.LoadViewIfNeeded();
            window.AddSubview(parent.View!);
            parent.View!.Frame = window.Bounds;
            using var controller = new UIViewController();
            parent.AddChildViewController(controller);
            parent.View.AddSubview(controller.View!);
            controller.DidMoveToParentViewController(parent);
            controller.View!.Frame = parent.View.Bounds;
            controller.AdditionalSafeAreaInsets = new UIEdgeInsets(20, 40, 24, 12);
            ResponsiveEnvironmentFlow.SetTraits(parent, controller, UIUserInterfaceSizeClass.Regular, UIUserInterfaceSizeClass.Regular);
            using var view = new NativeView(program) { Frame = controller.View.Bounds };
            controller.View.AddSubview(view);
            var errors = new List<Exception>();
            view.UnhandledException += errors.Add;
            parent.View.LayoutIfNeeded();
            controller.View.LayoutIfNeeded();
            for (var i = 0; i < 4; i++)
            {
                view.Render();
                view.LayoutIfNeeded();
            }
            ResponsiveFlow.Capture(view, output, name);
            verify(view);
            Record(window.Hidden && errors.Count == 0, name + " remains hidden without callback exceptions");
        }
    }
}
