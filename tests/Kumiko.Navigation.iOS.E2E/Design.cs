using CoreGraphics;
using CRMLite;
using Kumiko.iOS;
using UIKit;

/// <summary>
/// Renders every CRMLite section in light and dark through the real UIKit host and keeps PNG/TXT artifacts.
/// Fails if a view throws (an unsupported native style or icon) or the Harbor parts are not native views, and checks
/// that the parts shared with desktop draw exact text.
/// </summary>
internal static class DesignFlow
{
    internal static void Run(string output)
    {
        var initial = Data.Initial();
        using var program = CrmApp.CreateProgram(initial, new FakeCrmBackend(seed: initial).Services());
        var errors = new List<Exception>();
        using var view = new NativeView(program) { Frame = new CGRect(0, 0, 393, 852) };
        view.UnhandledException += errors.Add;
#pragma warning disable CA1422 // Private hidden window, never activated or shown.
        using var window = new UIWindow(new CGRect(0, 0, 393, 852));
#pragma warning restore CA1422
        using var host = new UIViewController();
        window.Hidden = true;
        window.RootViewController = host;
        host.LoadViewIfNeeded();
        window.AddSubview(host.View!);
        host.View!.Frame = window.Bounds;
        host.View.AddSubview(view);
        var shots = new List<string>();
        Settle(view);
        foreach (var dark in new[] { false, true })
        {
            var suffix = dark ? "-dark" : "";
            program.Send(new CrmMsg.SetDark(dark));
            Show(program, view, ScreenKind.Companies);
            shots.Add(Shot(view, output, "design-companies" + suffix));
            Require(StackFlow.All(view).Any(v => v.GetType().Name == "NativeIconView"), "Hugeicons render as native icon views");
            var bar = StackFlow.All(view).OfType<UITabBar>().Single();
            var list = StackFlow.All(view).OfType<UIScrollView>().First(v => v.AccessibilityIdentifier == "directory-list" && v.Window is not null);
            var listRect = list.ConvertRectToView(list.Bounds, view);
            Require(listRect.Bottom >= bar.Frame.Y + bar.Frame.Height - .5 && list.AdjustedContentInset.Bottom >= bar.Frame.Height - .5 &&
                bar.Frame.Height < 100, "the list runs under the floating tab bar and insets its content to scroll clear of it");
            StackFlow.Press(view, "Northwind Labs");
            Settle(view);
            Require(program.Model.CompanyId == "northwind", "a company row opens its company");
            shots.Add(Shot(view, output, "design-company" + suffix));
            foreach (var (offset, name) in new[] { (640, "design-company-activity"), (1280, "design-company-timeline") })
            {
                var scroll = StackFlow.All(view).OfType<UIScrollView>().First(v => v.AccessibilityIdentifier == "details-scroll" && v.Window is not null);
                scroll.ContentOffset = new CGPoint(0, Math.Min(offset, Math.Max(0, (double)(scroll.ContentSize.Height - scroll.Bounds.Height))));
                Settle(view);
                shots.Add(Shot(view, output, name + suffix));
                scroll.ContentOffset = CGPoint.Empty;
            }
            Show(program, view, ScreenKind.Deals);
            shots.Add(Shot(view, output, "design-deals" + suffix));
            var deal = Crm.BoardDeals(program.Model).First();
            var company = Crm.Company(program.Model, deal.CompanyId)!.Name;
            Require(StackFlow.All(view).OfType<UILabel>().Any(label => label.Text == company && label.Font.PointSize == Harbor.Scale.Body) &&
                StackFlow.All(view).Any(v => v.IsAccessibilityElement && v.AccessibilityLabel == Crm.Member(deal.OwnerId).Name &&
                    v.AccessibilityTraits.HasFlag(UIAccessibilityTrait.Image)),
                "the shared deal card draws exact phone text and announces its owner as one image");
            Show(program, view, ScreenKind.People);
            shots.Add(Shot(view, output, "design-people" + suffix));
            Show(program, view, ScreenKind.Inbox);
            shots.Add(Shot(view, output, "design-inbox" + suffix));
        }
        var tabs = StackFlow.All(view).OfType<UITabBar>().Single();
        Require(tabs.Items!.Single(item => item.AccessibilityIdentifier == nameof(ScreenKind.Inbox)).BadgeValue ==
            Crm.UnreadCount(program.Model).ToString(System.Globalization.CultureInfo.CurrentCulture), "the Inbox tab badge shows the unread count");
        Require(tabs.Frame.Bottom == view.Bounds.Bottom && tabs.TraitCollection.UserInterfaceStyle == UIUserInterfaceStyle.Dark,
            "the tab bar reaches the bottom edge and follows the app's dark theme");
        Require(errors.Count == 0, "design views render without native errors: " + string.Join("; ", errors.Select(e => e.Message)));
        Require(window.Hidden, "design captures never show the private window");
        Console.WriteLine($"Design captures: {string.Join(", ", shots)}");
    }

    /// <summary>Selects a section through the system tab bar's own delegate, as a tap does.</summary>
    static void Show(Kumiko.Runtime.Program<CrmModel, CrmMsg> program, NativeView view, ScreenKind kind)
    {
        var bar = StackFlow.All(view).OfType<UITabBar>().Single();
        var item = bar.Items!.Single(item => item.AccessibilityIdentifier == kind.ToString());
        bar.Delegate!.ItemSelected(bar, item);
        Settle(view);
        Require(ScreenRoutes.Kind(program.Model.Navigation.Entries[0].Route) == kind && ReferenceEquals(bar.SelectedItem, item),
            $"the system tab bar selects {kind}");
    }

    static string Shot(NativeView view, string output, string name)
    {
        StackFlow.Capture(view, output, name);
        return name;
    }

    static void Settle(NativeView view)
    {
        for (var i = 0; i < 4; i++)
            view.Render();
    }

    static void Require(bool value, string description)
    {
        if (!value)
            throw new InvalidOperationException(description);
    }
}
