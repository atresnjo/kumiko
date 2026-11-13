using Kumiko.Components;
using Kumiko.Runtime;
using Kumiko.UI;

namespace CRMLite;

public sealed partial class CrmView(CrmModel model, Screen? page = null) : View<CrmModel, CrmMsg>(model)
{
    bool ForcedRail => C.WidthBreakpoint < Breakpoint.Expanded;

    readonly Palette p = new(model.Dark, model.Preferences.Accent);

    protected override void Render()
    {
        if (page is not null)
        {
            RenderPage(page);
            return;
        }
        var c = Ui.Context;
        c.Kit.Apply(p.Kit);
        // One typed action set feeds keyboard shortcuts and the native menu; signed out it stays listed but disabled.
        var forcedRail = ForcedRail;
        var actions = Ui.DesktopActions(CrmApp.Actions(Model).Select(action =>
            forcedRail && action.Id == CrmCommands.Sidebar ? action with
            {
                Enabled = false,
                Label = "Show Sidebar"
            } : action));
        if (Model.Auth.Session is null)
        {
            SignInPage();
            return;
        }
        var dialog = Model.DealDraft is not null || Model.CompanyDraft is not null || Model.Overlay is Overlay.Plan;
        using (c.Div(key: "root").Row().SizeFull().Background(p.Page))
        {
            Sidebar();
            using var canvas = c.Div(key: "canvas").Flex().MinWidth(0).FillHeight().Items(Align.Center);
            using var main = c.Div(key: "main").FillWidth().MaxWidth(Palette.Sizes.Workspace).FillHeight().Column().Background(p.Page);
            if (!Model.Backend.Loaded || Model.Backend.Loading)
            {
                using var loading = c.Div(key: "loading").FillWidth().Flex().Center();
                c.Text(Model.Backend.Loading ? "Loading your CRM…" : "Your workspace could not be loaded. Try again.", key: "message");
                return;
            }
            Ui.Stack(Model.Navigation, Model, static (bound, model, entry) =>
                ScreenRoutes.Render(bound, new CrmPage(model, entry), entry.Route));
        }
        if (Model.Popup == Popup.Search)
            SearchPalette();
        if (Model.Settings.Open)
            SettingsSheet();
        if (Model.Overlay is Overlay.DealDrawer drawer)
            DealDrawer(drawer);
        else if (Model.Overlay is Overlay.DealMenu dealMenu)
            DealMenu(dealMenu);
        else if (Model.Overlay is Overlay.Plan plan)
            PlanDialog(plan);
        if (Model.DealDraft is { } deal)
            DealDialog(deal);
        else if (Model.CompanyDraft is { } company)
            CompanyDialog(company);
        PlanToasts();
        if (Model.PendingNavigation is { } pending)
            ConfirmLeave(pending.Request);
        // After the page, so text from this frame reaches its visit before the guard runs.
        // Dialogs, settings and a pending confirmation own the keyboard; Back never discards them.
        var navigation = Model.Navigation;
        if (navigation.Entries.Length > 1 && !dialog && !Model.Settings.Open && Model.PendingNavigation is null && c.BackShortcut())
            Ui.Dispatch(new CrmMsg.Back(Model.BackTo(navigation.Entries[^2].Id)));
        // Last, so editors, IME, dialogs, the palette and settings have already claimed their keys.
        actions.Shortcuts();
    }
}
