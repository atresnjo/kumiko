namespace CRMLite;

/// <summary>Drafting, approving and running a deal's agent plan.</summary>
public sealed partial class CrmApp
{
    static CrmModel OpenPlan(CrmModel m, string dealId)
    {
        if (m.Deals.FirstOrDefault(d => d.Id == dealId) is not { } deal)
            return m;
        var plans = m.PlanFor(dealId) is { } plan ? m.Plans.Replace(plan, plan with
        {
            Minimized = false
        }) : m.Plans.Add(AgentScript.Draft(m, deal));
        return m with
        {
            Plans = plans,
            Overlay = new Overlay.Plan(dealId)
        };
    }

    /// <summary>Closing an approved plan's dialog leaves its progress in a toast that opens it again.</summary>
    static CrmModel Minimize(CrmModel m) => WithPlan(m, p => p.Status == PlanStatus.Draft ? p : p with { Minimized = true });

    /// <summary>Changes the plan in the open dialog.</summary>
    static CrmModel WithPlan(CrmModel m, Func<AgentPlan, AgentPlan> change) =>
        m.Overlay is Overlay.Plan open && m.PlanFor(open.DealId) is { } plan ? Save(m, plan, change(plan)) : m;

    /// <summary>Changes the open plan while it is still a draft; an approved plan's steps are fixed.</summary>
    static CrmModel WithDraft(CrmModel m, Func<AgentPlan, AgentPlan> change) =>
        WithPlan(m, p => p.Status == PlanStatus.Draft ? change(p) : p);

    static CrmModel Save(CrmModel m, AgentPlan old, AgentPlan plan) => m with
    {
        Plans = m.Plans.Replace(old, plan)
    };

    /// <summary>Drops steps left without a title, except the one about to be edited.</summary>
    static AgentPlan Tidy(AgentPlan plan, string? keep = null) => plan with
    {
        Steps = [.. plan.Steps.Where(s => s.Id == keep || s.Title.Trim().Length > 0)]
    };

    static CrmModel EditStep(CrmModel m, string? id) => m.Overlay is Overlay.Plan open
        ? WithDraft(m, p => Tidy(p, id)) with
        {
            Overlay = open with
            {
                Editing = id
            }
        }
        : m;

    static CrmModel RenameStep(CrmModel m, string id, string title) =>
        WithDraft(m, p => p with { Steps = [.. p.Steps.Select(s => s.Id == id ? s with { Title = title } : s)] });

    static CrmModel RemoveStep(CrmModel m, string id)
    {
        if (m.Overlay is not Overlay.Plan open)
            return m;
        return WithDraft(m, p => p with { Steps = p.Steps.RemoveAll(s => s.Id == id) }) with
        {
            Overlay = open.Editing == id ? open with
            {
                Editing = null
            } : open
        };
    }

    static CrmModel AddStep(CrmModel m)
    {
        if (m.Overlay is not Overlay.Plan open || m.PlanFor(open.DealId) is not { Status: PlanStatus.Draft } plan)
            return m;
        var id = $"step-{plan.Sequence}";
        var added = Tidy(plan) with
        {
            Steps = Tidy(plan).Steps.Add(new(id, "")),
            Sequence = plan.Sequence + 1
        };
        return Save(m, plan, added) with
        {
            Overlay = open with
            {
                Editing = id
            }
        };
    }

    static AgentPlan MoveStep(AgentPlan plan, string id, int index)
    {
        if (plan.Steps.FirstOrDefault(s => s.Id == id) is not { } step)
            return plan;
        var rest = plan.Steps.Remove(step);
        return plan with
        {
            Steps = rest.Insert(Math.Clamp(index, 0, rest.Length), step)
        };
    }

    /// <summary>Adds the agent's next suggestion before the plan's last step.</summary>
    static CrmModel Tick(CrmModel m) => m.Plans.Where(p => p.Status == PlanStatus.Running).Aggregate(m, Advance);

    static CrmModel KeepPlanning(CrmModel m) => WithDraft(m, p =>
    {
        var deal = m.Deals.FirstOrDefault(d => d.Id == p.DealId);
        var ideas = deal is null ? [] : AgentScript.Suggestions(m, deal);
        if (p.Suggested >= ideas.Length)
            return p;
        var step = new PlanStep($"suggest-{p.Suggested}", ideas[p.Suggested], StepOrigin.Suggested);
        return p with
        {
            Steps = p.Steps.Insert(Math.Max(0, p.Steps.Length - 1), step),
            Suggested = p.Suggested + 1
        };
    });

    static CrmModel Approve(CrmModel m)
    {
        if (m.Overlay is not Overlay.Plan open || m.PlanFor(open.DealId) is not { Status: PlanStatus.Draft } plan || Tidy(plan).Steps.IsEmpty)
            return m;
        return Save(m, plan, Start(Tidy(plan) with
        {
            Status = PlanStatus.Running
        })) with
        {
            Overlay = open with
            {
                Editing = null
            }
        };
    }

    /// <summary>Starts the next pending step, or finishes the plan when none is left.</summary>
    static AgentPlan Start(AgentPlan plan)
    {
        if (plan.Steps.FirstOrDefault(s => s.State == StepState.Pending) is not { } next)
            return plan with
            {
                Status = PlanStatus.Done
            };
        return plan with
        {
            Status = PlanStatus.Running,
            Steps = plan.Steps.Replace(next, next with
            {
                State = StepState.Running
            })
        };
    }

    static AgentPlan Retry(AgentPlan plan)
    {
        if (plan.Status != PlanStatus.Failed || plan.Steps.FirstOrDefault(s => s.State == StepState.Failed) is not { } failed)
            return plan;
        return plan with
        {
            Status = PlanStatus.Running,
            Steps = plan.Steps.Replace(failed, failed with
            {
                State = StepState.Running,
                Shown = 0,
                Attempt = failed.Attempt + 1
            })
        };
    }

    static AgentPlan Skip(AgentPlan plan)
    {
        if (plan.Status != PlanStatus.Failed || plan.Steps.FirstOrDefault(s => s.State == StepState.Failed) is not { } failed)
            return plan;
        return Start(plan with
        {
            Steps = plan.Steps.Replace(failed, failed with
            {
                State = StepState.Skipped
            })
        });
    }

    /// <summary>
    /// One beat of agent work: the running step shows its next line of work, or, when all are shown, finishes or
    /// fails. A finished step starts the next one in the same beat, so the agent never idles between steps.
    /// </summary>
    static CrmModel Advance(CrmModel m, AgentPlan plan)
    {
        if (m.Deals.FirstOrDefault(d => d.Id == plan.DealId) is not { } deal)
            return m;
        if (plan.Steps.FirstOrDefault(s => s.State == StepState.Running) is not { } step)
            return Save(m, plan, Start(plan));
        var index = plan.Steps.IndexOf(step);
        var (work, failure) = AgentScript.Work(m, deal, step);
        var spent = step with
        {
            Seconds = step.Seconds + AgentScript.Pace(step)
        };
        if (step.Shown < work.Length)
            return Save(m, plan, plan with
            {
                Steps = plan.Steps.SetItem(index, spent with
                {
                    Shown = step.Shown + 1
                })
            });
        if (failure is not null)
            return Save(m, plan, plan with
            {
                Status = PlanStatus.Failed,
                Steps = plan.Steps.SetItem(index, spent with
                {
                    State = StepState.Failed
                })
            });
        var done = plan with
        {
            Steps = plan.Steps.SetItem(index, spent with
            {
                State = StepState.Done
            })
        };
        if (step.Id == AgentScript.AddsAfter && !done.Steps.Any(s => s.Origin == StepOrigin.Agent))
            done = done with
            {
                Steps = done.Steps.Insert(index + 1, AgentScript.Addition)
            };
        // The agent's stage change lands on the board and timeline without closing whatever the user has open.
        var moved = step.Id == AgentScript.Advances ? MoveDeal(m, deal.Id, AgentScript.Next(deal.Stage)) with
        {
            Overlay = m.Overlay
        } : m;
        return Save(moved, plan, Start(done));
    }
}
