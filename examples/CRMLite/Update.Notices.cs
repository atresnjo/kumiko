using System.Collections.Immutable;
using Kumiko.Runtime;

namespace CRMLite;

/// <summary>
/// Which desktop notices to post or withdraw, decided from the change one update made. Notices follow transitions,
/// never states, so a later tick, render, replay or restore cannot post the same news twice.
/// </summary>
public sealed partial class CrmApp
{
    // Windows balloons, the strictest backend, allow 63 title and 255 body characters.
    const int TitleLimit = 63, BodyLimit = 255;

    static (CrmModel, Cmd<CrmMsg>) Notify(CrmModel before, CrmModel after, Cmd<CrmMsg> command)
    {
        ImmutableArray<Cmd<DesktopMsg>> effects = [];
        var posted = after.Desktop.Posted;
        var leaving = before.Auth.Session is { } session && after.Auth.Session?.Id != session.Id;
        var silenced = before.Preferences.DesktopAlerts && !after.Preferences.DesktopAlerts;
        // Customer text never outlives its session, and turning notices off takes them down.
        if (!posted.IsEmpty && (leaving || silenced))
        {
            effects = effects.Add(Clear());
            posted = [];
        }
        foreach (var stale in posted.Where(notice => Stale(before, after, notice)).ToArray())
        {
            effects = effects.Add(Withdraw(stale.Id));
            posted = posted.Remove(stale);
        }
        foreach (var (notice, record) in Fresh(before, after, posted))
        {
            effects = effects.Add(Post(notice));
            posted = posted.RemoveAll(n => n.Id == record.Id).Add(record);
        }
        if (effects.IsEmpty)
            return (after, command);
        var shell = Cmd<DesktopMsg>.Batch([.. effects]).Map<CrmMsg>(static child => new CrmMsg.Desktop(child));
        return (WithDesktop(after, d => d with { Posted = posted }), Cmd<CrmMsg>.Batch(command, shell));
    }

    /// <summary>A notice goes stale once its subject is gone, resolved, or opened in the app.</summary>
    static bool Stale(CrmModel before, CrmModel after, PostedNotice notice)
    {
        if (notice.Kind == NoticeKind.SaveFailed)
            return !after.Backend.Dirty;
        var plan = notice.DealId is { } id ? after.PlanFor(id) : null;
        if (plan is null || !after.Deals.Any(d => d.Id == plan.DealId))
            return true;
        var opened = after.Overlay is Overlay.Plan now && now.DealId == plan.DealId &&
            !(before.Overlay is Overlay.Plan was && was.DealId == plan.DealId);
        return opened || notice.Kind == NoticeKind.PlanFailed && plan.Status != PlanStatus.Failed;
    }

    /// <summary>News from this update, only while CRM Lite is in the background and the user and host allow it.</summary>
    static IEnumerable<(CrmNotice Notice, PostedNotice Record)> Fresh(CrmModel before, CrmModel after, ImmutableArray<PostedNotice> posted)
    {
        if (!after.Preferences.DesktopAlerts || after.Desktop.Access != NoticeAccess.Ready || after.Desktop.Active || after.Auth.Session is null)
            yield break;
        foreach (var plan in after.Plans)
        {
            if (before.PlanFor(plan.DealId) is not { Status: PlanStatus.Running } || plan.Status is not (PlanStatus.Done or PlanStatus.Failed))
                continue;
            if (PlanNoticeFor(after, plan) is { } notice)
                yield return (notice, new(notice.Id, plan.Status == PlanStatus.Done ? NoticeKind.PlanDone : NoticeKind.PlanFailed, plan.DealId));
        }
        // One notice per unsaved stretch: later failures while it is posted would only repeat it.
        if (before.Backend.Saving && !after.Backend.Saving && after.Backend.Dirty && after.Backend.Error is { } error && !posted.Any(n => n.Id == SaveNotice))
            yield return (new(SaveNotice, "Changes not saved",
                Fit($"CRMLite couldn't save your latest edits: {error.TrimEnd('.')}. Open CRMLite to retry.", BodyLimit)),
                new(SaveNotice, NoticeKind.SaveFailed));
    }

    static CrmNotice? PlanNoticeFor(CrmModel m, AgentPlan plan)
    {
        if (m.Deals.FirstOrDefault(d => d.Id == plan.DealId) is not { } deal || Crm.Company(m, deal.CompanyId) is not { } company)
            return null;
        var subject = $"{company.Name} · {deal.Name}";
        if (plan.Status == PlanStatus.Done)
            return new(PlanNotice(plan.DealId), "Agent plan finished", Fit($"{subject}: all {plan.Finished} steps are done.", BodyLimit));
        var step = plan.Steps[plan.Current];
        return new(PlanNotice(plan.DealId), Fit("Agent plan needs attention", TitleLimit),
            Fit($"{subject}: step {plan.Current + 1}, “{step.Title}”, failed. Open the plan to retry or skip it.", BodyLimit));
    }

    static string Fit(string text, int limit) => text.Length <= limit ? text : text[..(limit - 1)] + "…";
}
