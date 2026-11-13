using Kumiko.Runtime;

internal sealed partial class OpalApp : IKumiko<OpalModel, OpalMessage>
{
    public static OpalModel Init => new();

    public static (OpalModel, Cmd<OpalMessage>) Update(OpalModel model, OpalMessage message) => message switch
    {
        OpalMessage.Step step when !model.Expanded && model.Notice is null =>
            RapidTap(model with
            {
                Index = Math.Clamp(model.Index + Math.Sign(step.Direction), 1, Steps.Length - 1)
            }, step.Milliseconds),
        OpalMessage.Toggle => (model with { Expanded = !model.Expanded, TapCount = 0, LastTap = 0 }, Cmd<OpalMessage>.None),
        OpalMessage.Select selection when model.Expanded && model.Notice is null =>
            (model with
            {
                Index = Math.Max(1, Enumerable.Range(1, Steps.Length - 1).FirstOrDefault(index => Steps[index].Id == selection.Id, model.Index))
            },
                Cmd<OpalMessage>.None),
        OpalMessage.Explain notice => (model with { Notice = notice.Title }, Cmd<OpalMessage>.None),
        OpalMessage.CloseNotice => (model with { Notice = null }, Cmd<OpalMessage>.None),
        OpalMessage.Navigate navigate => (model with { Navigation = model.Navigation.Move(navigate.Request, Host) }, Cmd<OpalMessage>.None),
        OpalMessage.Back or OpalMessage.Step or OpalMessage.Select => (model, Cmd<OpalMessage>.None),
        _ => throw new ArgumentOutOfRangeException(nameof(message))
    };

    static (OpalModel, Cmd<OpalMessage>) RapidTap(OpalModel model, long time)
    {
        var count = time >= model.LastTap && time - model.LastTap <= 300 ? model.TapCount + 1 : 1;
        return (model with
        {
            Expanded = count >= 3,
            TapCount = count >= 3 ? 0 : count,
            LastTap = count >= 3 ? 0 : time
        }, Cmd<OpalMessage>.None);
    }
}
