using Kumiko.Runtime;

internal sealed partial class AnimationGalleryApp : IKumiko<GalleryModel, GalleryMessage>
{
    public static GalleryModel Init => new(CapWordsApp.Init, AlmaApp.Init, SlackApp.Init, OpalApp.Init, UnwindApp.Init);

    public static (GalleryModel, Cmd<GalleryMessage>) Update(GalleryModel model, GalleryMessage message) =>
        message switch
        {
            GalleryMessage.Open open => (Open(model, open.Screen), Cmd<GalleryMessage>.None),
            GalleryMessage.Home => (Return(model), Cmd<GalleryMessage>.None),
            GalleryMessage.Navigate navigate => (Navigate(model, navigate.Request), Cmd<GalleryMessage>.None),
            GalleryMessage.CapWords { Message: CapWordsMessage.Gallery } when model.Screen == GalleryScreen.CapWords => (Return(model), Cmd<GalleryMessage>.None),
            GalleryMessage.CapWords child => CapWordsApp.Update(model.CapWords, child.Message).Fold(
                next => model with { CapWords = next }, next => (GalleryMessage)new GalleryMessage.CapWords(next)),
            GalleryMessage.Alma { Message: AlmaMessage.Back } when model.Screen == GalleryScreen.Alma => (Return(model), Cmd<GalleryMessage>.None),
            GalleryMessage.Alma child when model.Screen == GalleryScreen.Alma => AlmaApp.Update(model.Alma, child.Message).Fold(
                next => model with { Alma = next }, next => (GalleryMessage)new GalleryMessage.Alma(next)),
            GalleryMessage.Alma => (model, Cmd<GalleryMessage>.None),
            GalleryMessage.Slack { Message: SlackMessage.Back or SlackMessage.Done } when model.Screen == GalleryScreen.Slack => (Return(model), Cmd<GalleryMessage>.None),
            GalleryMessage.Slack child when model.Screen == GalleryScreen.Slack || child.Message is SlackMessage.Ready =>
                SlackApp.Update(model.Slack, child.Message).Fold(next => model with { Slack = next },
                    next => (GalleryMessage)new GalleryMessage.Slack(next)),
            GalleryMessage.Slack => (model, Cmd<GalleryMessage>.None),
            GalleryMessage.Opal { Message: OpalMessage.Back } when model.Screen == GalleryScreen.Opal => (Return(model), Cmd<GalleryMessage>.None),
            GalleryMessage.Opal child when model.Screen == GalleryScreen.Opal => OpalApp.Update(model.Opal, child.Message).Fold(
                next => model with { Opal = next }, next => (GalleryMessage)new GalleryMessage.Opal(next)),
            GalleryMessage.Opal => (model, Cmd<GalleryMessage>.None),
            GalleryMessage.Unwind { Message: UnwindMessage.Back } when model.Screen == GalleryScreen.Unwind => (Return(model), Cmd<GalleryMessage>.None),
            GalleryMessage.Unwind child when model.Screen == GalleryScreen.Unwind => UnwindApp.Update(model.Unwind, child.Message).Fold(
                next => model with { Unwind = next }, next => (GalleryMessage)new GalleryMessage.Unwind(next)),
            GalleryMessage.Unwind => (model, Cmd<GalleryMessage>.None),
            _ => throw new ArgumentOutOfRangeException(nameof(message))
        };

    /// <summary>The gallery pushes a study visit; another study replaces it, so Back always returns to the gallery.</summary>
    static GalleryModel Open(GalleryModel model, GalleryScreen screen)
    {
        if (!Enum.IsDefined(screen) || screen == model.Screen)
            return model;
        if (screen == GalleryScreen.Home)
            return Return(model);
        return model with
        {
            Navigation = model.Screen == GalleryScreen.Home ? model.Navigation.Push(screen) : model.Navigation.Replace(screen)
        };
    }

    /// <summary>Moves Back to the gallery's retained visit, keeping the study as a Forward entry.</summary>
    static GalleryModel Return(GalleryModel model)
    {
        var history = model.Navigation;
        if (model.Screen == GalleryScreen.Home)
            return model;
        return model with
        {
            Navigation = history.BackRequest(Host) is { } back && history.Path.Entries[history.Position - 1].Route == GalleryScreen.Home
                ? history.Move(back, Host) : history.Reset(GalleryScreen.Home)
        };
    }

    /// <summary>Applies an adjacent, current request from this host; stale or foreign requests change nothing.</summary>
    static GalleryModel Navigate(GalleryModel model, HistoryRequest request)
    {
        var history = model.Navigation.Move(request, Host);
        if (ReferenceEquals(history, model.Navigation))
            return model;
        return model with
        {
            Navigation = history
        };
    }

    public static IEnumerable<Sub<GalleryMessage>> Subscriptions(GalleryModel model)
    {
        foreach (var subscription in CapWordsApp.Subscriptions(model.CapWords))
            yield return subscription.Map<GalleryMessage>(message => new GalleryMessage.CapWords(message));
        if (model.Screen == GalleryScreen.Alma)
            foreach (var subscription in AlmaApp.Subscriptions(model.Alma))
                yield return subscription.Map<GalleryMessage>(message => new GalleryMessage.Alma(message));
    }
}
