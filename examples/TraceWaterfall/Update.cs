using Kumiko.Runtime;

namespace TraceWaterfall;

public sealed partial class TraceApp : IKumiko<TraceModel, TraceMsg>
{
    public static TraceModel Init => new(Data.Spans, 5100, true, false, []);

    // One destination; history requests never leave it, so the root has no Back or Forward.
    public static NavigationRoot<TraceMsg> Root(TraceModel model) => NavigationRoot<TraceMsg>.Create(
        NavigationHistory<string>.Start("trace"), "app", _ => new TraceScreen(model).Map<TraceMsg>(static message => message),
        static _ => throw new InvalidOperationException("The trace waterfall has one root screen."));

    public static (TraceModel, Cmd<TraceMsg>) Update(TraceModel model, TraceMsg message) =>
        (message switch
        {
            TraceMsg.Tick when model.Playing => Advance(model),
            TraceMsg.Tick => model,
            TraceMsg.TogglePlaying => model with { Playing = model.Now < 92000 && !model.Playing },
            TraceMsg.Restart => Init with { Now = 0, Dark = model.Dark },
            TraceMsg.Complete => model with { Now = 92000, Playing = false },
            TraceMsg.ToggleTheme => model with { Dark = !model.Dark },
            TraceMsg.ToggleBranch(var id) => Toggle(model, id),
            TraceMsg.Open(var id) => model.Spans.Any(s => s.Id == id && s.Start <= model.Now)
                ? model with
                {
                    OpenId = id,
                    FocusId = id,
                    RestoreFocus = false
                } : model,
            TraceMsg.Close => model with { OpenId = null, RestoreFocus = true },
            TraceMsg.FocusRestored => model with { RestoreFocus = false },
            _ => throw new ArgumentOutOfRangeException(nameof(message))
        }, Cmd<TraceMsg>.None);

    static TraceModel Advance(TraceModel model)
    {
        var next = Math.Min(92000, model.Now + 400);
        return model with
        {
            Now = next,
            Playing = next < 92000
        };
    }

    static TraceModel Toggle(TraceModel model, string id)
    {
        if (!model.Spans.Any(s => s.Parent == id))
            return model;
        var collapsed = model.Collapsed.Contains(id) ? model.Collapsed.Remove(id) : model.Collapsed.Add(id);
        return model with
        {
            Collapsed = collapsed
        };
    }
}
