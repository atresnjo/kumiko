using System.Collections.Immutable;
using Kumiko.Icons;
using Kumiko.Runtime;
using Kumiko.UI;
using static CRMLite.Palette;

namespace CRMLite;

/// <summary>The agent plan dialog's frame, header, footer and draft steps.</summary>
public sealed partial class CrmView
{
    const float ListPad = 8, DraftRow = 40, SuggestedRow = 58;
    void PlanDialog(Overlay.Plan open)
    {
        if (Model.PlanFor(open.DealId) is not { } plan)
            return;
        var width = MathF.Min(Sizes.Plan, MathF.Max(0, C.Size.Width - 32));
        using var overlay = C.Overlay("plan-dialog").Center().TabStop(false).Background(p.Scrim).Semantics(Role.Group).Enter(Transition.Fade)
            .Exit(Transition.Fade with
            {
                Motion = Motion.Quick
            });
        var backdrop = overlay.Control();
        if (C.Shortcut(Key.Escape, Modifiers.None))
            Ui.Dispatch(open.Editing is null ? new CrmMsg.CloseDialog() : new CrmMsg.EditStep(null));
        else if (backdrop.Clicked)
            Ui.Dispatch(new CrmMsg.CloseDialog());
        using var panel = C.Div(key: "panel").RaisedSurface(p).Width(width).Rounded(Corners.Plan).TabStop(false)
            .Semantics(Role.Dialog, "Agent plan").Enter(Transition.Pop).Exit(Transition.Pop with
            {
                Motion = Motion.Quick
            });
        panel.Shadow(40, 16, Color.Black.Alpha(p.Dark ? .5f : .14f));
        panel.Control();
        if (!C.FocusWithin(panel.Id))
            C.FocusNextControl();
        PlanHead(plan, width);
        using (C.Div(key: "steps").FillWidth().MaxHeight(MathF.Max(240, C.Size.Height - 220)).Scroll())
        {
            if (plan.Status == PlanStatus.Draft)
                DraftSteps(plan, open, width);
            else
                RunSteps(plan);
        }
        Rule("foot-rule");
        PlanFoot(plan);
    }
    void PlanHead(AgentPlan plan, float width)
    {
        using (C.Div(key: "head").Row().FillWidth().Items(Align.Start).Gap(14).Padding(new Edges(20, 24, 16, 24)))
        {
            using (C.Div(key: "mark").Width(22).Height(22).Center())
                C.Icon(Hugeicons.Asterisk02, 22, p.Spark, key: "icon");
            using (C.Div(key: "titles").Flex().Column().Gap(4))
            {
                var title = $"Plan · {plan.Steps.Length} {(plan.Steps.Length == 1 ? "step" : "steps")}";
                C.RichText(title, [new(0, 4, p.Text, Weight.Medium)], new(Size: TextSize.Step, Color: p.Secondary), key: "title");
                C.Text(plan.Goal, new(Size: TextSize.Step, Color: p.Secondary, Wrap: true), key: "goal");
            }
            var position = plan.Status switch
            {
                PlanStatus.Draft => AgentScript.Model,
                PlanStatus.Done => $"{plan.Steps.Length} of {plan.Steps.Length}",
                _ => $"{plan.Current + 1} of {plan.Steps.Length}"
            };
            // Each new position rises into place, like a counter ticking over.
            using (C.Div(key: position).Height(22).ItemsCenter().Row().Enter(Transition.Rise with
            {
                Y = 5
            }))
                Label(position, TextSize.Body, p.Faint, key: "text");
        }
        var done = plan.Steps.Length == 0 ? 0 : (float)plan.Finished / plan.Steps.Length;
        using var track = C.Div(key: "progress").FillWidth().Height(2).Background(p.Border).Semantics(Role.Image, "Plan progress",
            $"{plan.Finished} of {plan.Steps.Length} steps done");
        using (C.Div(key: "fill").Absolute().Width(width * done, Motion.Gentle).Height(2).Background(p.Text))
        {
        }
    }
    void PlanFoot(AgentPlan plan)
    {
        using var foot = C.Div(key: "foot").Row().FillWidth().Height(68).ItemsCenter().Gap(10).Padding(new Edges(0, 24, 0, 24));
        var step = plan.Current + 1;
        var status = plan.Status switch
        {
            PlanStatus.Draft => "",
            PlanStatus.Running => $"Working on step {step}",
            PlanStatus.Paused => $"Paused on step {step}",
            PlanStatus.Failed => $"Step {step} failed",
            _ => plan.Finished == plan.Steps.Length && plan.Steps.All(s => s.State == StepState.Done)
                ? $"All {plan.Steps.Length} steps done in {Duration(plan.Seconds)}"
                : $"Done in {Duration(plan.Seconds)}, {plan.Steps.Count(s => s.State == StepState.Skipped)} skipped"
        };
        using (C.Div(key: "status").Flex().Clip())
        {
            if (status.Length > 0)
            {
                using (C.Div(key: status).Enter(Transition.Rise with
                {
                    Y = 5
                }))
                    Label(status, TextSize.Step, p.Secondary, key: "text");
            }
        }
        switch (plan.Status)
        {
            case PlanStatus.Draft:
                var deal = Model.Deals.FirstOrDefault(d => d.Id == plan.DealId);
                var more = deal is not null && plan.Suggested < AgentScript.Suggestions(Model, deal).Length;
                Ui.OnPress(Pill("Keep planning", null, false, 36, Radius.Pill, "keep", more), new CrmMsg.KeepPlanning());
                var ready = plan.Steps.Any(s => s.Title.Trim().Length > 0);
                Ui.OnPress(Pill("Approve plan", null, true, 36, Radius.Pill, "approve", ready), new CrmMsg.ApprovePlan());
                break;
            case PlanStatus.Running:
                Ui.OnPress(Pill("Pause", Hugeicons.Pause, false, 36, Radius.Pill, "pause"), new CrmMsg.PausePlan());
                break;
            case PlanStatus.Paused:
                Ui.OnPress(Pill("Resume", Hugeicons.Play, false, 36, Radius.Pill, "resume"), new CrmMsg.ResumePlan());
                break;
            case PlanStatus.Done:
                Ui.OnPress(Pill("Close", null, true, 36, Radius.Pill, "close"), new CrmMsg.CloseDialog());
                break;
        }
    }

    /// <summary>"21s", "1m 03s".</summary>
    static string Duration(int seconds) => seconds < 60 ? $"{seconds}s" : $"{seconds / 60}m {seconds % 60:00}s";
    Interaction Pill(string label, string? icon, bool filled, float height, float radius, NodeKey key, bool enabled = true)
    {
        using var box = C.Div(key: key).Row().Height(height).ItemsCenter().Gap(8).Padding(new Edges(0, 16, 0, icon is null ? 16 : 12))
            .Rounded(radius).Background(filled ? p.Text : p.Raised).Border(filled ? p.Text : p.Border).Semantics(Role.Button, label);
        var hit = box.Control(enabled);
        var ink = filled ? p.Raised : p.Text;
        if (!enabled)
        {
            box.Background(p.Raised).Border(p.Border);
            ink = p.Faint;
        }
        else if (hit.Hovered)
        {
            box.Background(filled ? Color.Mix(p.Text, p.Raised, .16f) : Color.Mix(p.Raised, p.Text, .05f));
            C.Cursor = Cursor.Hand;
        }
        if (hit.Focused && C.FocusVisible)
            box.Border(p.Accent.Alpha(.6f), 2);
        if (icon is not null)
            C.Icon(icon, height > 32 ? 16 : 15, ink, key: "icon");
        Label(label, height > 32 ? TextSize.Step : TextSize.Body, ink, Weight.Medium, "label");
        return hit;
    }

    static float RowHeight(PlanStep step) => step.Origin == StepOrigin.Suggested ? SuggestedRow : DraftRow;
    void DraftSteps(AgentPlan plan, Overlay.Plan open, float width)
    {
        using var list = C.Div(key: "draft").FillWidth().Semantics(Role.List, "Plan steps");
        var drop = list.DropTarget("plan-step");
        var held = C.Dragging is { Kind: "plan-step" } dragging ? dragging.Value : drop.Dropped?.Item is { Kind: "plan-step" } dropped ? dropped.Value : null;
        var lifted = plan.Steps.FirstOrDefault(s => s.Id == held);
        var shown = plan.Steps.Where(s => s.Id != held).ToImmutableArray();
        var index = -1;
        if (lifted is not null && (drop.IsOver || drop.Dropped is not null) && C.DraggedRect is { } box && list.PreviousRect is { } top)
        {
            // The slot is the gap whose top edge is nearest the held row's top, as the rows are drawn around it.
            var heldTop = box.Y - top.Y - ListPad;
            var y = 0f;
            for (var gap = 0; gap <= shown.Length; gap++)
            {
                if (index < 0 || MathF.Abs(heldTop - y) < MathF.Abs(heldTop - Gap(index)))
                    index = gap;
                if (gap < shown.Length)
                    y += RowHeight(shown[gap]);
            }
        }
        float Gap(int at) => shown.Take(at).Sum(RowHeight);
        if (drop.Dropped is { Item: { Kind: "plan-step" } item })
            Ui.Dispatch(new CrmMsg.MoveStep(item.Value, Math.Max(0, index)));
        var slot = index >= 0 && drop.Dropped is null ? RowHeight(lifted!) : 0;
        list.Height(2 * ListPad + shown.Sum(RowHeight) + slot + DraftRow, Motion.Quick);
        var at = ListPad;
        for (var i = 0; i < shown.Length; i++)
        {
            if (i == index)
                at += slot;
            DraftStep(shown[i], i, at, width, open.Editing == shown[i].Id);
            at += RowHeight(shown[i]);
        }
        if (index == shown.Length)
            at += slot;
        using var add = C.Div(key: "add").Absolute().Offset(0, at, Motion.Quick).Width(width).Height(DraftRow).Px(ListPad + 16).Row()
            .ItemsCenter().Gap(14).Semantics(Role.Button, "Add step");
        var hit = add.Control();
        if (hit.Hovered)
            C.Cursor = Cursor.Hand;
        if (hit.Focused && C.FocusVisible)
            add.AccentOutline(p).Rounded(Corners.Step);
        using (C.Div(key: "icon").Width(18).Center())
            C.Icon(Hugeicons.Add01, 17, hit.Hovered ? p.Text : p.Secondary, key: "glyph");
        Label("Add step", TextSize.Step, hit.Hovered ? p.Text : p.Secondary, key: "label");
        Ui.OnPress(hit, new CrmMsg.AddStep());
    }
    void DraftStep(PlanStep step, int index, float y, float width, bool editing)
    {
        var name = step.Title.Trim().Length > 0 ? step.Title : "New step";
        // Inset, rounded and filled with the dialog surface, so the lifted row reads as a card with a soft shadow.
        using var row = C.Div(key: step.Id).Absolute().Offset(ListPad, y, Motion.Quick).Width(width - 2 * ListPad).Height(RowHeight(step)).Row()
            .Items(Align.Start).Gap(10).Padding(new Edges(6, 8, 6, 8)).Rounded(Corners.Step).Background(p.Raised);
        var over = row.PreviousRect is { } rect && rect.Contains(C.Input.Pointer) && C.Dragging is null;
        if (!editing)
        {
            row.Semantics(Role.Button, name, step.Origin == StepOrigin.Suggested ? "Suggested" : null)
                .DragSource("plan-step", step.Id, DragStyle.Flat with
                {
                    Placeholder = DragPlaceholder.Hidden,
                    Shadow = .45f
                });
            var hit = row.Control();
            Ui.OnPress(hit, new CrmMsg.EditStep(step.Id));
            if (hit.Focused && C.Shortcut(Key.Up, Modifiers.Alt))
                Ui.Dispatch(new CrmMsg.MoveStep(step.Id, index - 1));
            else if (hit.Focused && C.Shortcut(Key.Down, Modifiers.Alt))
                Ui.Dispatch(new CrmMsg.MoveStep(step.Id, index + 1));
            else if (hit.Focused && (C.Shortcut(Key.Delete, Modifiers.None) || C.Shortcut(Key.Backspace, Modifiers.None)))
                Ui.Dispatch(new CrmMsg.RemoveStep(step.Id));
            if (hit.Hovered)
                C.Cursor = Cursor.Hand;
            if (hit.Focused && C.FocusVisible)
                row.AccentOutline(p);
        }
        using (C.Div(key: "handle").Width(18).Height(28).Center())
            C.Icon(Hugeicons.DragDropVertical, 17, over ? p.Secondary : p.Faint, key: "icon");
        using (C.Div(key: "content").Flex().Column().Gap(2))
        {
            if (editing)
            {
                using var field = C.Div(key: "edit").FillWidth();
                // The field takes focus once, when editing starts, and Enter finishes the edit.
                if (field.PreviousRect is null)
                    C.FocusNextControl();
                Ui.TextField(key: "input", label: "Step", value: step.Title, onChange: text => new CrmMsg.RenameStep(step.Id, text),
                    options: new("Describe the step", width - 2 * (ListPad + 8) - 18 - 28 - 20, Height: 28));
                if (C.FocusWithin(field.Id) && C.Shortcut(Key.Enter, Modifiers.None))
                    Ui.Dispatch(new CrmMsg.EditStep(null));
            }
            else
            {
                using (C.Div(key: "title").FillWidth().Height(28).Row().ItemsCenter().Clip())
                    Label(name, TextSize.Step, step.Title.Trim().Length > 0 ? p.Text : p.Faint, key: "text");
            }
            if (step.Origin == StepOrigin.Suggested)
                AgentTag("Suggested", "tag");
        }
        if (editing || over)
        {
            using var remove = C.Div(key: "remove").IconAction().Semantics(Role.Button, "Remove " + name);
            var hit = remove.Control();
            Feedback(remove, hit);
            C.Icon(Hugeicons.Cancel01, 14, hit.Hovered ? p.Text : p.Faint, key: "icon");
            Ui.OnPress(hit, new CrmMsg.RemoveStep(step.Id));
        }
    }
    void AgentTag(string text, NodeKey key)
    {
        using var tag = C.Div(key: key).Row().Height(20).ItemsCenter().Gap(6);
        C.Icon(Hugeicons.Asterisk02, 14, p.Spark, key: "icon");
        Label(text, TextSize.Small, p.Faint, key: "label");
    }
}
