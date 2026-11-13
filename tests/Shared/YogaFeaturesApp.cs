using Kumiko.Runtime;
using Kumiko.UI;

internal sealed record YogaFeaturesModel(bool Hidden = false, bool Rtl = false)
{
    public NavigationHistory<string> Navigation { get; init; } = NavigationHistory<string>.Start("features");
}
internal abstract partial record YogaFeaturesMsg
{
    internal sealed record Toggle : YogaFeaturesMsg;
    internal sealed record HiddenAction : YogaFeaturesMsg;
    internal sealed record Navigate(HistoryRequest Request) : YogaFeaturesMsg;
}

internal sealed partial class YogaFeaturesApp : IKumiko<YogaFeaturesModel, YogaFeaturesMsg>
{
    public static NavigationRoot<YogaFeaturesMsg> Root(YogaFeaturesModel model) => NavigationRoot<YogaFeaturesMsg>.Create(
        model.Navigation, "app", _ => new YogaFeaturesView(new(model.Hidden, model.Rtl)).Map<YogaFeaturesMsg>(static message => message),
        static request => new YogaFeaturesMsg.Navigate(request));
    public static YogaFeaturesModel Init => new();
    public static (YogaFeaturesModel, Cmd<YogaFeaturesMsg>) Update(YogaFeaturesModel model, YogaFeaturesMsg message) =>
        (message switch
        {
            YogaFeaturesMsg.Toggle => model with { Hidden = !model.Hidden },
            YogaFeaturesMsg.Navigate navigate => model with { Navigation = model.Navigation.Move(navigate.Request, "app") },
            _ => model
        }, Cmd<YogaFeaturesMsg>.None);

}

internal sealed record YogaFeaturesProps(bool Hidden, bool Rtl);

internal sealed class YogaFeaturesView(YogaFeaturesProps props) : Screen<YogaFeaturesProps, YogaFeaturesMsg>(props)
{
    static readonly Color Blue = Color.Hex(0x2368aa);
    static readonly Color Pale = Color.Hex(0xeaf1f8);
    static readonly LayoutOptions Card = new(Padding: LengthEdges.All(8), Border: LengthEdges.All(2),
        BoxSizing: BoxSizing.ContentBox);

    protected override void Render()
    {
        var c = C;
        var model = Model;
        var ui = Ui;
        c.Theme = Theme.Light with
        {
            Accent = Blue
        };
        using var page = c.Div(key: "features").SizeFull().Padding(12).Gap(10).Background(Color.White)
            .Flow(model.Rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight);
        c.Text("Yoga layout", new(Size: 24, Weight: Weight.Bold, Color: Color.Black), key: "heading");
        c.Text("Auto spacing · relative units · box sizing · positioning", new(Size: 11, Color: Color.Black), key: "intro");
        using (c.Div(key: "auto-row").Row().FillWidth().Height(40).ItemsCenter().Background(Pale))
        {
            using (c.Div(key: "auto-first").Width(48).Height(24).Background(Blue))
            {
            }
            using (c.Div(key: "auto-last").Width(64).Height(24).Margin(LayoutEdge.Start, Length.Auto).Background(Blue))
            {
            }
        }
        using (c.Div(key: "percent-box").FillWidth().Height(92).Padding(Length.Percent(5))
            .LayoutBorder(Edges.All(2)).Border(Blue, 2))
        {
            using (c.Div(key: "percent-margin").Width(100).Height(16).Margin(LayoutEdge.Start, Length.Percent(5)).Background(Blue))
            {
            }
            using (c.Div(key: "percent-child").FillWidth().Height(16).Background(Pale))
            {
            }
        }
        using (c.Div(key: "bounds").Row().FillWidth().Height(32).Gap(Length.Percent(4)))
        {
            using (c.Div(key: "minimum").Width(10).MinWidth(Length.Percent(25)).Height(24).Background(Blue))
            {
            }
            using (c.Div(key: "maximum").FillWidth().MaxWidth(Length.Percent(40)).Height(24).Background(Blue))
            {
            }
        }
        using (c.Div(key: "boxes").Row().FillWidth().Height(70).Gap(12))
        {
            using (c.Div(key: "content-box").Layout(Card).Width(80).Height(40).Border(Blue, 2).Background(Pale))
                c.Text("Content", new(Size: 11, Color: Color.Black), key: "content-text");
            using (c.Div(key: "border-box").Layout(Card with
            {
                BoxSizing = BoxSizing.BorderBox
            })
                .Width(80).Height(40).Border(Blue, 2).Background(Pale))
                c.Text("Border", new(Size: 11, Color: Color.Black), key: "border-text");
        }
        using (c.Div(key: "insets").Position(PositionMode.Relative).FillWidth().Height(72).Padding(8).Background(Pale))
        {
            using (c.Div(key: "static-scope").Position(PositionMode.Static).Width(100).Height(36).Inset(30).Background(Pale))
            using (c.Div(key: "static-marker").Position(PositionMode.Absolute).Width(12).Height(12)
                .Inset(new LengthEdges(Left: 5, Top: 5)).Background(Blue))
            {
            }
            using (c.Div(key: "pinned").Position(PositionMode.Absolute).Width(64).Height(24)
                .Inset(new LengthEdges(Top: Length.Percent(10), End: 8)).Background(Blue))
            {
            }
            using (c.Div(key: "stretched").Position(PositionMode.Absolute).Height(12)
                .Inset(new LengthEdges(Left: 8, Right: 8, Bottom: 8)).Background(Blue))
            {
            }
        }
        using (c.Div(key: "overflow").FillWidth().Height(12)
            .Overflow(model.Hidden ? OverflowMode.Visible : OverflowMode.Hidden))
        using (c.Div(key: "overflow-fill").Absolute().Offset(-8, 0).Width(320).Height(12).Background(Blue))
        {
        }
        using (c.Div(key: "display-row").Row().FillWidth().Height(48).Gap(8).ItemsCenter())
        {
            using (c.Div(key: "contents").Display(DisplayMode.Contents).Padding(40).Background(Color.Hex(0xff0000)))
            {
                using (c.Div(key: "contents-first").Width(32).Height(24).Background(Blue))
                {
                }
                using (c.Div(key: "hidden-scope").Display(model.Hidden ? DisplayMode.None : DisplayMode.Flex))
                    ui.Button("Hidden action", new YogaFeaturesMsg.HiddenAction(), key: "hidden-action");
            }
            using (c.Div(key: "contents-last").Width(32).Height(24).Background(Blue))
            {
            }
        }
        using (c.Div(key: "baseline").Row().FillWidth().Items(Align.Baseline).Gap(10))
        {
            c.Text("Reference", new(Size: 24, Color: Color.Black), key: "reference");
            using var group = c.Div(key: "baseline-group").Gap(4);
            c.Text("Caption", new(Size: 10, Color: Color.Black), key: "caption");
            using (c.Div(key: "baseline-reference").ReferenceBaseline())
                c.Text("Selected baseline", new(Size: 14, Color: Color.Black), key: "selected-baseline");
        }
        ui.Button("Toggle visibility", new YogaFeaturesMsg.Toggle(), key: "toggle");
    }
}
