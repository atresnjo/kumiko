using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace CRMLite;

/// <summary>
/// Where a plan stands: drafted for review, running, paused by the user, stopped at a failed step, or finished.
/// </summary>
public enum PlanStatus
{
    Draft,
    Running,
    Paused,
    Failed,
    Done
}

public enum StepState
{
    Pending,
    Running,
    Done,
    Failed,
    Skipped
}

/// <summary>Who wrote a step: the first draft, a suggestion from Keep planning, or the agent while it worked.</summary>
public enum StepOrigin
{
    Planned,
    Suggested,
    Agent
}

/// <summary>The kind of one line of agent work under a step, which picks its icon.</summary>
public enum WorkKind
{
    Read,
    Searched,
    Created,
    Edited,
    Ran,
    Emailed,
    Scheduled
}

public sealed record WorkItem(WorkKind Kind, string Text);

/// <summary>
/// One plan step. <paramref name="Shown"/> counts the lines of work revealed so far in the current
/// <paramref name="Attempt"/>; <paramref name="Seconds"/> is the agent time spent on the step.
/// </summary>
public sealed record PlanStep(
    string Id,
    string Title,
    StepOrigin Origin = StepOrigin.Planned,
    StepState State = StepState.Pending,
    int Shown = 0,
    int Seconds = 0,
    int Attempt = 0
);

/// <summary>
/// The agent's plan for one deal. <paramref name="Suggested"/> counts the suggestions Keep planning has added and
/// <paramref name="Sequence"/> numbers the steps the user adds. A <paramref name="Minimized"/> plan shows as a toast
/// while its dialog is closed.
/// </summary>
public sealed record AgentPlan(
    string DealId,
    string Goal,
    ImmutableArray<PlanStep> Steps,
    PlanStatus Status = PlanStatus.Draft,
    int Suggested = 0,
    int Sequence = 0,
    bool Minimized = false
)
{
    /// <summary>The step the agent is on: the running or failed step, or the next pending one.</summary>
    [JsonIgnore]
    public int Current => Steps.IndexOf(Steps.FirstOrDefault(s => s.State is StepState.Running or StepState.Failed) ??
        Steps.FirstOrDefault(s => s.State == StepState.Pending) ?? Steps[^1]);

    [JsonIgnore] public int Finished => Steps.Count(s => s.State is StepState.Done or StepState.Skipped);

    [JsonIgnore] public int Seconds => Steps.Sum(s => s.Seconds);
}

public sealed partial record CrmModel
{
    /// <summary>Agent plans, at most one per deal.</summary>
    public ImmutableArray<AgentPlan> Plans { get; init; } = [];

    /// <summary>
    /// Whether running plans advance on a timer. Tests and snapshots turn it off and send
    /// <see cref="CrmMsg.AgentTick"/> themselves, so every step is repeatable.
    /// </summary>
    public bool LiveAgent { get; init; } = true;

    public AgentPlan? PlanFor(string dealId) => Plans.FirstOrDefault(p => p.DealId == dealId);
}
