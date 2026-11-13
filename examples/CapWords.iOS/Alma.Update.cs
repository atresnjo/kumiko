using Kumiko.Runtime;

internal sealed partial class AlmaApp : IKumiko<AlmaModel, AlmaMessage>
{
    public static AlmaModel Init => new();

    public static (AlmaModel, Cmd<AlmaMessage>) Update(AlmaModel model, AlmaMessage message) =>
        (message switch
        {
            AlmaMessage.Tick when model.Playing && !model.Notice => model with { Index = (model.Index + 1) % Items.Length },
            AlmaMessage.Tick => model,
            AlmaMessage.Back => model,
            AlmaMessage.Next => model with { Index = (model.Index + 1) % Items.Length },
            AlmaMessage.Previous => model with { Index = (model.Index + Items.Length - 1) % Items.Length },
            AlmaMessage.Select selected => model with { Index = selected.Index, Playing = false },
            AlmaMessage.TogglePlayback => model with { Playing = !model.Playing },
            AlmaMessage.Continue => model with { Notice = true },
            AlmaMessage.CloseNotice => model with { Notice = false },
            AlmaMessage.Navigate navigate => model with { Navigation = model.Navigation.Move(navigate.Request, Host) },
            _ => throw new ArgumentOutOfRangeException(nameof(message))
        }, Cmd<AlmaMessage>.None);

    public static IEnumerable<Sub<AlmaMessage>> Subscriptions(AlmaModel model) => model.Playing && !model.Notice
        ? [Sub<AlmaMessage>.Every("alma-advance", TimeSpan.FromMilliseconds(2500), () => new AlmaMessage.Tick())]
        : [];
}
