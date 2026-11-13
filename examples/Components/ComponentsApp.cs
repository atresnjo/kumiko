using Kumiko.Components;
using Kumiko.Runtime;
using Kumiko.UI;

namespace Components;

public sealed partial class ComponentsApp : IKumiko<ShowcaseModel, ShowcaseMsg>
{
    public static ShowcaseModel Init => new();

    public static (ShowcaseModel, Cmd<ShowcaseMsg>) Update(ShowcaseModel m, ShowcaseMsg msg) => (msg switch
    {
        ShowcaseMsg.SetDark d => m with { Dark = d.On },
        ShowcaseMsg.SetAccent a => m with { Accent = a.Accent },
        ShowcaseMsg.SetRadius r => m with { Radius = r.Radius },
        ShowcaseMsg.Navigate n => m with { Section = n.Section },
        ShowcaseMsg.SelectTab t => m with { Tab = t.Tab },
        ShowcaseMsg.SetPeriod p => m with { Period = p.Period },
        ShowcaseMsg.SetFilter f => m with { Filter = f.Filter },
        ShowcaseMsg.Click c => m with { Clicks = m.Clicks + 1, LastAction = $"Clicked {c.Label}" },
        ShowcaseMsg.SetName n => m with { Name = n.Text },
        ShowcaseMsg.SetEmail e => m with { Email = e.Text },
        ShowcaseMsg.SetTerms t => m with { Terms = t.On },
        ShowcaseMsg.SetMarketing mk => m with { Marketing = mk.On },
        ShowcaseMsg.SetAirplane a => m with { Airplane = a.On },
        ShowcaseMsg.SetWifi w => m with { Wifi = w.On },
        ShowcaseMsg.SetPlan p => m with { Plan = p.Plan },
        ShowcaseMsg.SetVolume v => m with { Volume = v.Value },
        ShowcaseMsg.SetLayout l => m with { Layout = l.Layout },
        ShowcaseMsg.Expand e => m with { Expanded = e.Id },
        ShowcaseMsg.OpenDialog => m with { DialogOpen = true },
        ShowcaseMsg.CloseDialog => m with { DialogOpen = false },
        ShowcaseMsg.ConfirmDelete => m with { DialogOpen = false, LastAction = "Project deleted" },
        ShowcaseMsg.SetPage p => m with { Page = p.Page },
        ShowcaseMsg.SetRating r => m with { Rating = r.Stars },
        ShowcaseMsg.SetAccountOpen a => m with { AccountOpen = a.Open, SearchOpen = false },
        ShowcaseMsg.AccountAction a => m with { AccountOpen = false, LastAction = a.Label },
        ShowcaseMsg.SetSearchOpen s => m with { SearchOpen = s.Open, AccountOpen = false, Query = "" },
        ShowcaseMsg.SetQuery q => m with { Query = q.Text },
        ShowcaseMsg.SearchResult r => m with { Section = r.Section, SearchOpen = false, Query = "" },
        ShowcaseMsg.SetDue d => m with { Due = d.Date },
        ShowcaseMsg.SetDueOpen o => m with { DueOpen = o.Open, StartOpen = false },
        ShowcaseMsg.SetStart s => m with { Start = s.Time },
        ShowcaseMsg.SetStartOpen o => m with { StartOpen = o.Open, DueOpen = false },
        ShowcaseMsg.SetBudget b => m with { Budget = b.Amount },
        ShowcaseMsg.SetFolder f => m with { Folders = f.Open ? Folders(m).Add(f.Id) : Folders(m).Remove(f.Id) },
        ShowcaseMsg.SelectFile f => m with { File = f.Id },
        ShowcaseMsg.SetPane p => m with { Pane = p.Width },
        _ => throw new ArgumentOutOfRangeException(nameof(msg))
    }, Cmd<ShowcaseMsg>.None);

    /// <summary>The kit theme for the current appearance choices.</summary>
    public static KitTheme ThemeFor(ShowcaseModel m)
    {
        var theme = m.Dark ? KitTheme.Dark : KitTheme.Light;
        Color? accent = m.Accent switch
        {
            "blue" => Palette.Blue,
            "violet" => Palette.Violet,
            "green" => Palette.Green,
            "orange" => Palette.Orange,
            "rose" => Palette.Rose,
            _ => null
        };
        if (accent is { } color)
            theme = theme with
            {
                Primary = KitTheme.Brand(color, m.Dark),
                SliderBar = color,
                ProgressBar = color,
                Ring = color.Alpha(.6f)
            };
        return theme with
        {
            Radius = m.Radius
        };
    }
}
