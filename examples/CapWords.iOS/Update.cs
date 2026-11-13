using Kumiko.Runtime;

internal sealed partial class CapWordsApp : IKumiko<CapWordsModel, CapWordsMessage>
{
    public static CapWordsModel Init => new(Words: []);

    public static (CapWordsModel, Cmd<CapWordsMessage>) Update(CapWordsModel model, CapWordsMessage message) =>
        (message switch
        {
            CapWordsMessage.Loaded loaded => model with { Words = loaded.Words },
            CapWordsMessage.LoadFailed failed => model with { Error = failed.Error },
            CapWordsMessage.Open open when !model.Words.IsDefaultOrEmpty && model.Words.Any(word => word.Id == open.Id) => model with { SelectedId = open.Id, Notice = null },
            CapWordsMessage.Open => model,
            CapWordsMessage.Gallery => model,
            CapWordsMessage.Back => model with { SelectedId = null, Notice = null },
            CapWordsMessage.Notice notice => model with { Notice = notice.Text },
            CapWordsMessage.DismissNotice => model with { Notice = null },
            CapWordsMessage.Navigate navigate => model with { Navigation = model.Navigation.Move(navigate.Request, Host) },
            _ => throw new ArgumentOutOfRangeException(nameof(message))
        }, Cmd<CapWordsMessage>.None);
}
