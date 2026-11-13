using Kumiko.iOS;
using Kumiko.Runtime;

internal sealed partial class SlackApp : IKumiko<SlackModel, SlackMessage>
{
    public static SlackModel Init => new(Dispositions: ["", "", "", "", ""]);

    public static (SlackModel, Cmd<SlackMessage>) Update(SlackModel model, SlackMessage message) => message switch
    {
        SlackMessage.MarkRead when !model.Busy && model.Index >= 0 => Commit(model, NativeSwipeDirection.Right, 500),
        SlackMessage.KeepUnread when !model.Busy && model.Index >= 0 => Commit(model, NativeSwipeDirection.Left, 500),
        SlackMessage.Swipe swipe when !model.Busy && model.Index >= 0 && Ids[model.Index] == swipe.Id => Commit(model, swipe.Direction, 300),
        SlackMessage.Undo when model.Index < 4 && !model.Undoing => Hold(model with
        {
            Index = model.Index + 1,
            Undoing = true,
            Dispositions = model.Dispositions.SetItem(model.Index + 1, "")
        }, 650),
        SlackMessage.Ready ready when ready.Revision == model.Revision => (model with { Busy = false, Undoing = false }, Cmd<SlackMessage>.None),
        SlackMessage.Navigate navigate => (model with { Navigation = model.Navigation.Move(navigate.Request, Host) }, Cmd<SlackMessage>.None),
        SlackMessage.MarkRead or SlackMessage.KeepUnread or SlackMessage.Swipe or SlackMessage.Undo or SlackMessage.Ready or
            SlackMessage.Back or SlackMessage.Done => (model, Cmd<SlackMessage>.None),
        _ => throw new ArgumentOutOfRangeException(nameof(message))
    };

    static (SlackModel, Cmd<SlackMessage>) Commit(SlackModel model, NativeSwipeDirection direction, int delay) => Hold(model with
    {
        Index = model.Index - 1,
        Direction = direction,
        Dispositions = model.Dispositions.SetItem(model.Index, direction == NativeSwipeDirection.Right ? "read" : "unread")
    }, delay);

    static (SlackModel, Cmd<SlackMessage>) Hold(SlackModel model, int delay)
    {
        var revision = model.Revision + 1;
        var command = Cmd<SlackMessage>.Debounce("slack-settle", TimeSpan.FromMilliseconds(delay),
            _ => Task.FromResult<SlackMessage>(new SlackMessage.Ready(revision)), _ => new SlackMessage.Ready(revision));
        return (model with
        {
            Revision = revision,
            Busy = true
        }, command);
    }
}
