using Kumiko.Components;
using Kumiko.Icons;
using Kumiko.Runtime;
using Kumiko.UI;

namespace CRMLite;

/// <summary>Minimized agent plans as toasts at the bottom right of the window.</summary>
public sealed partial class CrmView
{
    void PlanToasts()
    {
        if (Model.Settings.Open || Model.Overlay is Overlay.Plan or Overlay.DealDrawer or Overlay.NewDeal or Overlay.NewCompany)
            return;
        var toasts = Model.Plans.Where(p => p.Minimized).Select(p => PlanToast(p)).OfType<Toast>().ToArray();
        foreach (var e in C.Kit.Toaster(toasts, key: "plan-toasts"))
            Ui.Dispatch(e.Kind == ToastEventKind.Clicked ? new CrmMsg.OpenPlan(e.Id) : new CrmMsg.DismissPlanToast(e.Id));
    }
    Toast? PlanToast(AgentPlan plan)
    {
        if (Model.Deals.FirstOrDefault(d => d.Id == plan.DealId) is not { } deal || Crm.Company(Model, deal.CompanyId) is not { } company)
            return null;
        var step = plan.Current + 1;
        var total = plan.Steps.Length;
        var title = plan.Status switch
        {
            PlanStatus.Paused => $"Paused on step {step} of {total}",
            PlanStatus.Failed => $"Step {step} of {total} failed",
            PlanStatus.Done => "Plan done",
            _ => $"Working on step {step} of {total}"
        };
        var detail = plan.Status == PlanStatus.Done ? $"All steps finished in {Duration(plan.Seconds)}" : plan.Steps[plan.Current].Title;
        var props = new ToastProps(
            Description: $"{company.Name} · {detail}",
            Tone: plan.Status switch
            {
                PlanStatus.Done => Tone.Success,
                PlanStatus.Failed => Tone.Danger,
                _ => Tone.Default
            },
            Icon: plan.Status == PlanStatus.Paused ? Hugeicons.Pause : null,
            Busy: plan.Status == PlanStatus.Running,
            Progress: total == 0 ? 0 : (float)plan.Finished / total,
            Clickable: true,
            Duration: plan.Status == PlanStatus.Done ? TimeSpan.FromSeconds(8) : null);
        return new(plan.DealId, title, props);
    }
}
