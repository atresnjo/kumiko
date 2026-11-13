using System.Collections.Immutable;
using Kumiko.iOS;
using Kumiko.Runtime;
using Kumiko.UI;

internal enum HostRoute
{
    Home,
    Details
}

/// <summary>Counts taps, records lifecycle phases and owns a two-destination history.</summary>
internal sealed record HostModel(int Count = 0, bool TrackPhases = true)
{
    public ImmutableArray<NativeAppPhase> Phases { get; init; } = [];
    public NavigationHistory<HostRoute> Navigation { get; init; } = NavigationHistory<HostRoute>.Start(HostRoute.Home);
}

internal abstract partial record HostMessage
{
    public sealed record Increment : HostMessage;
    public sealed record PhaseChanged(NativeAppPhase Phase) : HostMessage;
    public sealed record SetTracking(bool On) : HostMessage;
    public sealed record Open(HostRoute Route) : HostMessage;
    public sealed record Navigate(HistoryRequest Request) : HostMessage;
}

internal sealed record HomeProps(int Count);
internal sealed record DetailsProps(int Count, int Phases);

internal sealed partial class HostApp : IKumiko<HostModel, HostMessage>
{
    internal const string Host = "app";

    public static HostModel Init => new();

    public static NavigationRoot<HostMessage> Root(HostModel model) => NavigationRoot<HostMessage>.Create(
        model.Navigation, Host, route => route switch
        {
            HostRoute.Home => new HomeScreen(new(model.Count)).Map<HostMessage>(static message => message),
            HostRoute.Details => new DetailsScreen(new(model.Count, model.Phases.Length)).Map<HostMessage>(static message => message),
            _ => throw new ArgumentOutOfRangeException(nameof(route))
        }, static request => new HostMessage.Navigate(request));

    public static (HostModel, Cmd<HostMessage>) Update(HostModel model, HostMessage message) => (message switch
    {
        HostMessage.Increment => model with { Count = model.Count + 1 },
        HostMessage.PhaseChanged changed => model with { Phases = model.Phases.Add(changed.Phase) },
        HostMessage.SetTracking tracking => model with { TrackPhases = tracking.On },
        HostMessage.Open open => model with { Navigation = model.Navigation.Push(open.Route) },
        HostMessage.Navigate navigate => model with { Navigation = model.Navigation.Move(navigate.Request, Host) },
        _ => throw new ArgumentOutOfRangeException(nameof(message))
    }, Cmd<HostMessage>.None);

    public static IEnumerable<Sub<HostMessage>> Subscriptions(HostModel model) => model.TrackPhases
        ? [NativeLifecycle.Phases<HostMessage>(static phase => new HostMessage.PhaseChanged(phase))]
        : [];
}

internal sealed class HomeScreen(HomeProps props) : Screen<HomeProps, HostMessage>(props)
{
    protected override void Render()
    {
        using var page = C.Div(key: "home").SizeFull().Padding(Space.S4).Gap(Space.S3);
        C.Heading("Host checks", key: "title");
        Ui.Button($"Count {Model.Count}", new HostMessage.Increment(), key: "increment");
        Ui.Button("Details", new HostMessage.Open(HostRoute.Details), key: "details");
    }
}

internal sealed class DetailsScreen(DetailsProps props) : Screen<DetailsProps, HostMessage>(props)
{
    protected override void Render()
    {
        using var page = C.Div(key: "details").SizeFull().Padding(Space.S4).Gap(Space.S3);
        C.Heading("Details", key: "title");
        C.Text($"Count {Model.Count} · {Model.Phases} phases", key: "summary");
    }
}
