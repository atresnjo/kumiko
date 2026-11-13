using CoreGraphics;
using Kumiko.iOS;
using Kumiko.Runtime;
using UIKit;

internal static partial class FocusFlow
{
    /// <summary>Checks a tab switch: one replaced visit, no Back entry, the shell's retained bar and ignored stale requests.</summary>
    static void CheckTabVisits(NativeView view, Program<FocusModel, FocusMessage> program, NavigationHistory<FocusTab> replaced,
        (UITabBar Bar, UITabBarDelegate Owner, UITabBarItem[] Items) kept)
    {
        var history = program.Model.Navigation;
        Require(history.Path.Entries.Length == 1 && !history.CanGoBack && !history.CanGoForward && history.Current.Id != replaced.Current.Id,
            "Tab switch replaces the visit and leaves no Back entry");
        var bar = Find<UITabBar>(view, "bottom-bar");
        Require(All(view).OfType<UITabBar>().Count() == 1 && ReferenceEquals(bar, kept.Bar) && ReferenceEquals(bar.Delegate, kept.Owner) &&
            bar.Items!.SequenceEqual(kept.Items), "The navigation shell keeps the same UITabBar, delegate and items across tab visits");
        Require(bar.SelectedItem?.AccessibilityIdentifier == "blocking", "The retained bar shows the new native selection");
        var steps = program.History.Count;
        Tap(view, "blocking");
        Pump();
        program.Send(new FocusMessage.SelectTab("blocking"));
        program.Send(new FocusMessage.Navigate(new("other", history.Path.Revision, history.Current.Id, history.Current.Id)));
        program.Send(new FocusMessage.Navigate(new(FocusApp.Host, replaced.Path.Revision, replaced.Current.Id, history.Current.Id)));
        program.Send(new FocusMessage.Navigate(new(FocusApp.Host, history.Path.Revision, history.Current.Id, replaced.Current.Id)));
        PumpUntil(() => program.History.Count >= steps + 4);
        Require(ReferenceEquals(program.Model.Navigation, history),
            "Reselecting the tab and foreign, stale or nonadjacent history requests keep the visit");
    }

    /// <summary>Restores the model saved on Settings in a separate program and host.</summary>
    static void CheckRestore(string output, CGRect frame)
    {
        using var program = FocusApp.CreateProgram();
        Require(program.RestoreModel(Path.Combine(output, "focus-settings-model.json")), "Saved Focus model restores");
        using var view = new NativeView(program) { Frame = frame };
        view.Render();
        Pump();
        view.Render();
        Require(program.Model.Tab == "settings" && program.History.Count == 1 &&
            Find<UITabBar>(view, "bottom-bar").SelectedItem?.AccessibilityIdentifier == "settings" &&
            All(view).Any(child => child.AccessibilityIdentifier == "demo-note"),
            "Restore renders the saved tab with native selection and sends no messages");
        Capture(view, output, "restored");
    }

    static bool SameVisit(NavigationHistory<FocusTab> left, NavigationHistory<FocusTab> right) =>
        left.Current == right.Current && left.Position == right.Position && left.Path.Revision == right.Path.Revision &&
        left.Path.NextId == right.Path.NextId && left.Path.Entries.SequenceEqual(right.Path.Entries);
}
