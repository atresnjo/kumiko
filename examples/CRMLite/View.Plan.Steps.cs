using Kumiko.Icons;
using Kumiko.Runtime;
using Kumiko.UI;
using static CRMLite.Palette;

namespace CRMLite;

/// <summary>An approved plan's steps as the agent works through them.</summary>
public sealed partial class CrmView
{
    void RunSteps(AgentPlan plan)
    {
        if (Model.Deals.FirstOrDefault(d => d.Id == plan.DealId) is not { } deal)
            return;
        using var list = C.Div(key: "run").FillWidth().Column().Gap(2).Padding(new Edges(10, 12, 10, 12)).Semantics(Role.List, "Plan steps");
        foreach (var step in plan.Steps)
            RunStep(deal, step);
    }
    void RunStep(Deal deal, PlanStep step)
    {
        using var row = C.Div(key: step.Id).FillWidth().Row().Items(Align.Start).Gap(14).Padding(new Edges(9, 12, 9, 12)).Rounded(12)
            .Semantics(Role.ListItem, step.Title, Describe(step)).Enter(Transition.Rise with
            {
                Y = 6
            });
        // A step the agent added stays highlighted until the agent reaches it, then the highlight fades.
        var glow = row.Animate(step.Origin == StepOrigin.Agent && step.State == StepState.Pending ? 1 : 0, Motion.Gentle);
        if (glow > .01f)
            row.Background(Color.Mix(p.Raised, p.Menu, glow));
        StepMark(step);
        using var body = C.Div(key: "body").Flex().Column();
        using (C.Div(key: "line").Row().FillWidth().Height(24).ItemsCenter().Gap(Space.S3))
        {
            using (C.Div(key: "title").Flex().Clip())
                StepTitle(step);
            if (step.State is StepState.Done or StepState.Skipped)
            {
                using (C.Div(key: step.State.ToString()).Enter(Transition.Fade))
                    Label(step.State == StepState.Skipped ? "Skipped" : Duration(step.Seconds), TextSize.Body, p.Faint, key: "text");
            }
        }
        if (step.Origin == StepOrigin.Agent)
            AgentTag("Added by agent", "tag");
        Unfold("work", step.State is StepState.Running or StepState.Failed, () => StepWork(deal, step));
    }

    static string Describe(PlanStep step) => step.State switch
    {
        StepState.Done => "Done in " + Duration(step.Seconds),
        StepState.Running => "Working",
        StepState.Failed => "Failed",
        StepState.Skipped => "Skipped",
        _ => "Not started"
    };
    void Unfold(NodeKey key, bool open, Action content)
    {
        using var clip = C.Div(key: key).FillWidth().Clip();
        using var inner = C.Div(key: "content").Absolute().FillWidth().Column();
        var height = clip.Animate(open ? inner.PreviousRect?.Height ?? 0 : 0, Motion.Standard, MotionChannel.Height);
        clip.Height(height);
        if (open || height > .5f)
            content();
    }
    void StepWork(Deal deal, PlanStep step)
    {
        var (work, failure) = AgentScript.Work(Model, deal, step);
        // A retry is a fresh attempt, so its lines arrive anew.
        using var lines = C.Div(key: $"attempt-{step.Attempt}").FillWidth().Column().Padding(new Edges(6, 0, 2, 0));
        for (var i = 0; i < Math.Min(step.Shown, work.Length); i++)
        {
            using var line = C.Div(key: i).Row().FillWidth().Height(26).ItemsCenter().Gap(10).Enter(Transition.Rise with
            {
                Y = 6
            });
            using (C.Div(key: "icon").Width(16).Center())
                C.Icon(WorkIcon(work[i].Kind), 15, p.Faint, key: "glyph");
            Label(work[i].Text, TextSize.Body, p.Secondary, key: "text");
        }
        if (step.State != StepState.Failed || failure is null)
            return;
        using var failed = C.Div(key: "failure").FillWidth().Column().Gap(12).Padding(new Edges(6, 0, 4, 0)).Enter(Transition.Rise with
        {
            Y = 6
        });
        C.RichText("Failed  " + failure, [new(0, 6, p.Danger, Weight.Medium)], new(Size: TextSize.Body, Color: p.Secondary, Wrap: true), key: "reason");
        using (C.Div(key: "actions").Row().Gap(Space.S2))
        {
            Ui.OnPress(Pill("Retry", Hugeicons.Reload, true, 30, Corners.Step, "retry"), new CrmMsg.RetryStep());
            Ui.OnPress(Pill("Skip", Hugeicons.Next, false, 30, Corners.Step, "skip"), new CrmMsg.SkipStep());
        }
    }

    static string WorkIcon(WorkKind kind) => kind switch
    {
        WorkKind.Searched => Hugeicons.FileSearch,
        WorkKind.Created => Hugeicons.FileAdd,
        WorkKind.Edited => Hugeicons.FileEdit,
        WorkKind.Ran => Hugeicons.CommandLine,
        WorkKind.Emailed => Hugeicons.MailSend01,
        WorkKind.Scheduled => Hugeicons.Calendar03,
        _ => Hugeicons.File01
    };
    void StepMark(PlanStep step)
    {
        using var box = C.Div(key: "mark").Width(20).Height(24).Center();
        var pop = new Transition(Scale: .4f, Motion: Motion.Quick);
        switch (step.State)
        {
            case StepState.Done:
                using (C.Div(key: "done").Width(20).Height(20).Center().Rounded(Radius.Pill).Background(p.Success).Enter(pop))
                    C.Icon(Hugeicons.Tick02, 13, Color.White, key: "icon");
                break;
            case StepState.Failed:
                using (C.Div(key: "failed").Width(20).Height(20).Center().Rounded(Radius.Pill).Background(p.Alarm).Enter(pop))
                    Label("!", TextSize.Body, Color.White, Weight.Bold, "icon");
                break;
            case StepState.Running:
                Spinner();
                break;
            case StepState.Skipped:
                using (C.Div(key: "skipped").Enter(pop))
                    C.Icon(Hugeicons.Next, 16, p.Faint, key: "icon");
                break;
            default:
                using (C.Div(key: "pending").Width(19).Height(19).Rounded(Radius.Pill).Border(Color.Mix(p.Raised, p.Faint, .55f), 1.5f))
                {
                }
                break;
        }
    }
    void Spinner()
    {
        var turn = C.ReduceMotion ? 0 : (float)(C.Time % .8 / .8 * 360);
        using var ring = C.Div(key: "spinner").Width(19).Height(19).Rounded(Radius.Pill).Border(Color.Mix(p.Raised, p.Faint, .3f), 2).Rotate(turn)
            .Semantics(Role.Image, "Working");
        using (C.Div(key: "quarter").Absolute().Offset(9.5f, 0).Width(9.5f).Height(9.5f).Clip())
        using (C.Div(key: "arc").Absolute().Offset(-9.5f, 0).Width(19).Height(19).Rounded(Radius.Pill).Border(p.Text, 2))
        {
        }
        if (!C.ReduceMotion)
            C.FrameIn(TimeSpan.FromSeconds(1 / 60d));
    }
    void StepTitle(PlanStep step)
    {
        var color = step.State switch
        {
            StepState.Done => p.Secondary,
            StepState.Skipped => p.Faint,
            _ => p.Text
        };
        var style = new TextStyle(Size: TextSize.Step, Color: color);
        if (step.State != StepState.Running || C.ReduceMotion)
        {
            C.Text(step.Title, style, key: "text");
            return;
        }
        const float Band = 7, Seconds = 1.8f;
        var length = step.Title.Length;
        var at = (float)(C.Time % Seconds / Seconds) * (length + 2 * Band) - Band;
        var spans = new TextSpan[length];
        for (var i = 0; i < length; i++)
        {
            var near = Math.Clamp(1 - MathF.Abs(i - at) / Band, 0, 1);
            spans[i] = new(i, i + 1, Color.Mix(p.Faint, p.Text, near * near * (3 - 2 * near)));
        }
        C.RichText(step.Title, spans, style with
        {
            Color = p.Faint
        }, key: "text");
    }
}
