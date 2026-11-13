using Kumiko.Runtime;

internal sealed partial class UnwindApp : IKumiko<UnwindModel, UnwindMessage>
{
    public static UnwindModel Init => new();

    public static (UnwindModel, Cmd<UnwindMessage>) Update(UnwindModel model, UnwindMessage message) =>
        (message switch
        {
            UnwindMessage.Select select when select.Index >= 0 && select.Index < Routines.Length => model with { Index = select.Index },
            UnwindMessage.Play play when play.Index >= 0 && play.Index < Routines.Length =>
                model with
                {
                    Notice = Routines[play.Index].Category
                },
            UnwindMessage.Explain explain => model with { Notice = explain.Topic },
            UnwindMessage.CloseNotice => model with { Notice = null },
            UnwindMessage.Navigate navigate => model with { Navigation = model.Navigation.Move(navigate.Request, Host) },
            UnwindMessage.Select or UnwindMessage.Play or UnwindMessage.Back => model,
            _ => throw new ArgumentOutOfRangeException(nameof(message))
        }, Cmd<UnwindMessage>.None);
}
