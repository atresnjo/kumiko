using System.Collections.Immutable;
using Kumiko.Runtime;

/// <summary>What the persistent shell needs: the selected tab for the bar and the open drawer, if any.</summary>
internal sealed record FocusShellProps(FocusTab Tab, FocusDrawerProps? Drawer);
internal sealed record FocusDrawerProps(string Kind, int Minutes, ImmutableHashSet<string> Blocked);
internal sealed record NowProps(int Minutes, int Remaining, bool Running, bool Started, string BlockingSummary);
internal sealed record BlockingProps(ImmutableHashSet<string> Blocked);
internal sealed record SectionProps;

internal sealed partial class FocusApp
{
    /// <summary>The navigation host key; history requests from any other host are ignored.</summary>
    internal const string Host = "focus";

    /// <summary>The shell (header, tab bar, drawer) stays mounted; only the tab's content changes per visit.</summary>
    public static NavigationRoot<FocusMessage> Root(FocusModel model) => NavigationRoot<FocusMessage>.Create(
        model.Navigation, Host, tab => Screen(model, tab), static request => new FocusMessage.Navigate(request),
        new FocusShell(new(model.Navigation.Current.Route, model.Drawer is { } kind ? new(kind, model.Minutes, model.Blocked) : null))
            .Map<FocusMessage>(static message => message));

    static Screen<FocusMessage> Screen(FocusModel model, FocusTab tab) => tab switch
    {
        FocusTab.Now => new NowScreen(new(model.Minutes, model.Remaining, model.Running, model.Started,
            BlockingSummary(model.Blocked))).Map<FocusMessage>(static message => message),
        FocusTab.Blocking => new BlockingScreen(new(model.Blocked)).Map<FocusMessage>(static message => message),
        FocusTab.Activity => new ActivityScreen(new()).Map<FocusMessage>(static message => message),
        FocusTab.Health => new HealthScreen(new()).Map<FocusMessage>(static message => message),
        FocusTab.Settings => new SettingsScreen(new()).Map<FocusMessage>(static message => message),
        _ => throw new ArgumentOutOfRangeException(nameof(tab))
    };
}
