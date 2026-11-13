using CoreGraphics;
using Kumiko.UI;
using UIKit;

namespace Kumiko.iOS;

internal sealed partial class UIKitControls
{
    // Retired controllers still in UIKit's dismissal transition, with completions waiting on them
    // (an alert above a departing sheet leaves with that sheet).
    readonly Dictionary<IntPtr, List<Action>> carried = new();

    Entry CreateAlert(NativeControlNode node, NativeAlertContent content)
    {
        var overlay = new UIView { Hidden = true, IsAccessibilityElement = false };
        return new(overlay, Owner: new AlertOwner(this, overlay, node.Id, content));
    }

    // One alert or dialog may be declared, above every declared sheet. Reject before reconciling anything.
    static void ValidatePresentations(IReadOnlyList<NativeControlNode> controls)
    {
        var alert = false;
        foreach (var node in controls)
        {
            if (node.Content is NativeAlertContent)
            {
                if (alert)
                    throw new InvalidOperationException("A NativeView presents one alert or confirmation dialog at a time.");
                alert = true;
            }
            else if (alert && node.Content is NativeSheetContent)
                throw new InvalidOperationException("Declare a system sheet before the alert or dialog presented above it.");
        }
    }

    // Removed or structurally changed alerts leave immediately; UIKit keeps the controller through its
    // dismissal transition and the entry is disposed afterwards. A redeclaration creates a fresh controller.
    void RetireAlerts(IReadOnlyList<NativeControlNode> controls, bool animated = true)
    {
        var declared = controls.Where(node => node.Content is NativeAlertContent)
            .ToDictionary(node => node.Id, node => (NativeAlertContent)node.Content);
        foreach (var pair in entries.Where(pair => pair.Value.Owner is AlertOwner owner &&
            (!declared.TryGetValue(pair.Key, out var next) || !owner.Accepts(next))).ToArray())
        {
            entries.Remove(pair.Key);
            current.Remove(pair.Key);
            values.Remove(pair.Key);
            var entry = pair.Value;
            var owner = (AlertOwner)entry.Owner!;
            var handle = DepartWith(owner.Controller);
            owner.Dismiss(() =>
            {
                Departed(handle);
                DisposeEntry(entry);
                requestFrame();
            }, animated);
        }
    }

    void PlaceAlerts()
    {
        foreach (var entry in entries.Values)
            if (entry.Owner is AlertOwner alert)
                alert.Place();
    }

    IntPtr DepartWith(UIViewController controller)
    {
        carried[controller.Handle] = [];
        return controller.Handle;
    }

    void Departed(IntPtr controller)
    {
        if (!carried.Remove(controller, out var followers))
            return;
        foreach (var follower in followers)
            follower();
    }

    // The topmost Kumiko-owned controller, or null while any controller in the chain is transitioning.
    UIViewController? Presenter()
    {
        UIViewController? presenter = null;
        for (UIResponder? responder = root; responder is not null; responder = responder.NextResponder)
            if (responder is UIViewController controller)
            {
                presenter = controller;
                break;
            }
        if (presenter is null)
            throw new InvalidOperationException("A visible NativeView alert requires a UIViewController host.");
        while (presenter.PresentedViewController is { } next)
        {
            if (carried.ContainsKey(next.Handle))
                return null;
            var sheet = entries.Values.Select(entry => entry.Owner).OfType<SheetOwner>()
                .FirstOrDefault(owner => owner.Controller == next);
            var alert = entries.Values.Any(entry => entry.Owner is AlertOwner owner && owner.Controller == next);
            if (sheet is null && !alert)
                throw new InvalidOperationException("The host already presents a controller Kumiko does not own. Dismiss it first.");
            if (alert || next.IsBeingPresented || next.IsBeingDismissed || sheet!.Transitioning)
                return null;
            presenter = next;
        }
        return presenter;
    }

    sealed class AlertOwner : IDisposable
    {
        readonly UIKitControls renderer;
        readonly UIView overlay;
        readonly ulong id;
        readonly Dictionary<string, UIAlertAction> actions = new(StringComparer.Ordinal);
        readonly List<(UITextField Field, UIAction Edit)> fields = new();
        NativeAlertContent content;
        bool presented;
        bool presenting;
        bool finished;
        bool stopped;
        bool updating;
        Action? pendingDismissal;
        bool pendingAnimation;
        internal UIAlertController Controller
        {
            get;
        }

        bool Animate => renderer.Root.Window is { Hidden: false } && !UIAccessibility.IsReduceMotionEnabled;

        internal AlertOwner(UIKitControls renderer, UIView overlay, ulong id, NativeAlertContent content)
        {
            this.renderer = renderer;
            this.overlay = overlay;
            this.id = id;
            this.content = content;
            Controller = UIAlertController.Create(content.Title, content.Message,
                content.ActionSheet ? UIAlertControllerStyle.ActionSheet : UIAlertControllerStyle.Alert);
            foreach (var field in content.Fields)
            {
                var fieldId = field.Id;
                Controller.AddTextField(text =>
                {
                    text.AccessibilityIdentifier = fieldId;
                    text.SecureTextEntry = field.Secure;
                });
                var native = Controller.TextFields![^1];
                var edit = UIAction.Create(_ => renderer.Guard(() =>
                {
                    if (!stopped && !finished && !updating && native.MarkedTextRange is null)
                        renderer.QueueValue(id, new NativeAlertEvent.Edited(fieldId, native.Text ?? ""));
                }));
                native.AddAction(edit, UIControlEvent.EditingChanged);
                fields.Add((native, edit));
            }
            foreach (var action in content.Actions)
            {
                var actionId = action.Id;
                var style = action.Style switch
                {
                    NativeAlertActionStyle.Cancel => UIAlertActionStyle.Cancel,
                    NativeAlertActionStyle.Destructive => UIAlertActionStyle.Destructive,
                    _ => UIAlertActionStyle.Default
                };
                var native = UIAlertAction.Create(action.Label, style, _ => renderer.Guard(() => Choose(actionId)));
                Controller.AddAction(native);
                actions.Add(actionId, native);
            }
            Update(content);
        }

        // Actions, styles and fields are fixed for a UIAlertController's lifetime.
        internal bool Accepts(NativeAlertContent next) => next.ActionSheet == content.ActionSheet &&
            next.Actions.Select(action => (action.Id, action.Label, action.Style))
                .SequenceEqual(content.Actions.Select(action => (action.Id, action.Label, action.Style))) &&
            next.Fields.Select(field => (field.Id, field.Placeholder, field.Secure))
                .SequenceEqual(content.Fields.Select(field => (field.Id, field.Placeholder, field.Secure)));

        internal void Update(NativeAlertContent next)
        {
            content = next;
            if (Controller.Title != next.Title)
                Controller.Title = next.Title;
            if (Controller.Message != next.Message)
                Controller.Message = next.Message;
            foreach (var action in next.Actions)
                actions[action.Id].Enabled = action.Enabled;
            updating = true;
            try
            {
                for (var index = 0; index < fields.Count; index++)
                {
                    var (native, _) = fields[index];
                    var field = next.Fields[index];
                    if (!renderer.values.ContainsKey(id) && native.Text != field.Value && native.MarkedTextRange is null)
                        native.Text = field.Value;
                    native.Placeholder = field.Placeholder;
                    native.AccessibilityLabel = field.Placeholder ?? field.Id;
                }
            }
            finally
            {
                updating = false;
            }
        }

        void Choose(string actionId)
        {
            if (stopped || finished || !content.Actions.Any(action => action.Id == actionId && action.Enabled))
                return;
            finished = true;
            renderer.QueueValue(id, new NativeAlertEvent.Chosen(actionId));
        }

        internal void Place()
        {
            if (stopped)
                return;
            if (presented)
            {
                // A departing sheet beneath this alert took it along: report the dismissal once.
                if (!presenting && !finished && Controller.PresentingViewController is null && !Controller.IsBeingDismissed)
                {
                    finished = true;
                    renderer.QueueValue(id, new NativeAlertEvent.Dismissed());
                }
                else if (!finished)
                    Anchor();
                return;
            }
            if (finished)
                return;
            if (renderer.Root.Window is null)
            {
                // A detached host keeps the same controller inside its hidden overlay, as sheets do.
                if (Controller.View!.Superview != overlay)
                    overlay.AddSubview(Controller.View);
                return;
            }
            if (renderer.Presenter() is not { } presenter)
                return;
            Controller.ViewIfLoaded?.RemoveFromSuperview();
            Anchor();
            presented = true;
            presenting = true;
            presenter.PresentViewController(Controller, Animate, () => renderer.Guard(() =>
            {
                presenting = false;
                if (pendingDismissal is { } completed)
                {
                    pendingDismissal = null;
                    Dismiss(completed, pendingAnimation);
                }
                else
                    renderer.RequestFrame();
            }));
        }

        // Regular-width hosts present action sheets as popovers; UIKit throws without a source.
        void Anchor()
        {
            if (!content.ActionSheet || Controller.PopoverPresentationController is not { } popover)
                return;
            var root = renderer.Root;
            var source = renderer.entries.TryGetValue(content.Source, out var entry) && !entry.View.Hidden &&
                entry.View.Window is { } window && window == root.Window ? entry.View : null;
            var view = source ?? root;
            var rect = source?.Bounds ?? new CGRect(root.Bounds.GetMidX(), root.Bounds.GetMidY(), 0, 0);
            if (popover.SourceView != view)
                popover.SourceView = view;
            if (popover.SourceRect != rect)
                popover.SourceRect = rect;
            var arrows = source is null ? 0 : UIPopoverArrowDirection.Any;
            if (popover.PermittedArrowDirections != arrows)
                popover.PermittedArrowDirections = arrows;
        }

        internal void Dismiss(Action completed, bool animated = true)
        {
            finished = true;
            if (presenting)
            {
                pendingDismissal = completed;
                pendingAnimation = animated;
                return;
            }
            var presenter = Controller.PresentingViewController;
            if (presenter is not null && renderer.carried.TryGetValue(presenter.Handle, out var followers))
                followers.Add(completed);
            else if (Controller.IsBeingDismissed)
            {
                if (Controller.GetTransitionCoordinator() is not { } coordinator ||
                    !coordinator.AnimateAlongsideTransition(_ => { }, _ => renderer.Guard(completed)))
                    completed();
            }
            else if (presenter is not null)
                Controller.DismissViewController(animated && Animate, () => renderer.Guard(completed));
            else
                completed();
        }

        public void Dispose()
        {
            if (stopped)
                return;
            stopped = true;
            foreach (var (native, edit) in fields)
            {
                native.RemoveAction(edit, UIControlEvent.EditingChanged);
                edit.Dispose();
            }
            fields.Clear();
            foreach (var action in actions.Values)
                action.Dispose();
            actions.Clear();
            if (!presented)
                Controller.ViewIfLoaded?.RemoveFromSuperview();
            Controller.Dispose();
        }
    }
}
