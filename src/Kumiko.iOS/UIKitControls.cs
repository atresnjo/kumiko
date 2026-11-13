using CoreGraphics;
using Kumiko.UI;
using UIKit;

namespace Kumiko.iOS;

internal sealed partial class UIKitControls(UIView root, Action<ulong> invoke,
    Action<Action> guard, Action requestFrame) : IControlRenderer, IDisposable
{
    void Guard(Action action) => guard(action);
    void RequestFrame() => requestFrame();
    void Invoke(ulong id)
    {
        RememberNavigationOpener(id);
        invoke(id);
    }
    UIView Root => root;

    sealed record Entry(UIView View, UIAction? Action = null, IDisposable? Owner = null, UIButton? Target = null)
    {
        internal NativeControlNode? Presented;
        internal CGRect? PlacedFrame;
    }
    ulong[] placedOrder = [];
    object? presentedTextMetrics;
    readonly Dictionary<ulong, Entry> entries = new();
    readonly Dictionary<ulong, NativeControlNode> current = new();
    readonly Queue<(ulong Id, string Row)> selections = new();

    internal string? TakeSelection(ulong id)
    {
        if (selections.TryPeek(out var next) && next.Id == id)
        {
            selections.Dequeue();
            if (selections.Count > 0)
                requestFrame();
            return current.TryGetValue(id, out var node) && !node.Inert ? next.Row : null;
        }
        return null;
    }

    internal void Select(ulong id, string row) => guard(() =>
    {
        if (current.TryGetValue(id, out var node) && !node.Inert && node.Enabled &&
            IsSelectable(node.Content, row))
        {
            RememberNavigationOpener(id, row);
            selections.Enqueue((id, row));
            requestFrame();
        }
    });

    static bool IsSelectable(object content, string id) => content switch
    {
        NativeListContent list => list.Items.Any(item => item.Id == id && item.Enabled),
        NativeTabBarContent tabs => tabs.Items.Any(item => item.Id == id),
        NativeCarouselContent { Interactive: true } carousel => carousel.Items.Any(item => item.Id == id),
        NativeArcMorphContent arc => arc.Cards.Any(card => card.Id == id || NativeComponents.PlayPrefix + card.Id == id),
        NativeSwipeDeckContent { Preview: false, Disabled: false, Index: >= 0 } deck => NativeComponents.ParseSwipe(id) is { } swipe &&
            swipe.Id == deck.Ids[deck.Index],
        _ => false
    };

    internal UIUserInterfaceStyle? PendingInterfaceStyle;

    public void Present(IReadOnlyList<NativeControlNode> controls)
    {
        var style = PendingInterfaceStyle ?? UIUserInterfaceStyle.Unspecified;
        PendingInterfaceStyle = null;
        if (root.OverrideUserInterfaceStyle != style)
            root.OverrideUserInterfaceStyle = style;
        var motion = PrepareMotion(controls);
        var allowMotion = !suppressNavigationMotion && !UIAccessibility.IsReduceMotionEnabled && root.Window is { Hidden: false };
        var textMetrics = TextMetricsKey;
        var fontsChanged = !Equals(presentedTextMetrics, textMetrics);
        var reorder = !placedOrder.SequenceEqual(controls.Select(node => node.Id)) ||
            controls.Any(node => current.TryGetValue(node.Id, out var previous) && previous.Parent != node.Parent);
        var live = controls.Select(node => node.Id).ToHashSet();
        ValidatePresentations(controls);
        CommitCoveredInput(controls);
        RetireSheets(live);
        RetireAlerts(controls);
        RetireNavigationPages(live);
        // Children were inserted after parents. Dispose them first.
        foreach (var id in entries.Keys.Reverse().Where(id => !live.Contains(id)).ToArray())
            Remove(id);
        foreach (var node in controls)
            if (current.TryGetValue(node.Id, out var previous) && !SameKind(previous.Content, node.Content))
            {
                Remove(node.Id);
                reorder = true;
            }
        current.Clear();
        foreach (var node in controls)
            current.Add(node.Id, node);
        PruneValues();
        var queued = selections.Where(selection => live.Contains(selection.Id)).ToArray();
        selections.Clear();
        foreach (var selection in queued)
            selections.Enqueue(selection);
        foreach (var node in controls)
        {
            if (!entries.TryGetValue(node.Id, out var entry))
            {
                entry = Create(node);
                entries.Add(node.Id, entry);
            }
            var created = !currentMotionIds.Contains(node.Id);
            // Static appearance stays valid until its content or native font environment changes.
            // Stateful controls still reconcile pending edits and native interaction every frame.
            if (!StaticAppearance(node.Content) || fontsChanged || !Equals(entry.Presented?.Content, node.Content))
                Update(entry, node);
            if (node.Content is NativeNavigationPageContent)
            {
                entry.Presented = node;
                continue;
            }
            if (entry.Owner is StackOwner nativeStack && nativeStack.Controller.ParentViewController is null)
            {
                for (UIResponder? responder = root; responder is not null; responder = responder.NextResponder)
                    if (responder is UIViewController host)
                    {
                        host.AddChildViewController(nativeStack.Controller);
                        break;
                    }
            }
            var parent = node.Parent == 0 ? root : entries[node.Parent].View;
            if (parent is NativePresentationView presentation)
                parent = presentation.ContentView;
            if (entry.Owner is SheetOwner sheet)
            {
                sheet.Place(parent, node);
                entry.View.AccessibilityIdentifier = node.Key ?? node.Id.ToString("x");
                continue;
            }
            var reparented = entry.View.Superview != parent;
            if (reparented)
            {
                parent.AddSubview(entry.View);
                if (entry.Owner is StackOwner attached && attached.Controller.ParentViewController is { } host)
                    attached.Controller.DidMoveToParentViewController(host);
            }
            var origin = node.Parent == 0 ? default : current[node.Parent].Bounds;
            var frame = new CGRect(node.Bounds.X - origin.X, node.Bounds.Y - origin.Y,
                node.Bounds.Width, node.Bounds.Height);
            // Compare logical frames, not UIView.Frame: compositor transforms change the
            // latter during a gesture without changing the framework's layout.
            if (reparented || entry.PlacedFrame != frame)
            {
                if (entry.View is NativeSwipeCardView)
                    entry.View.Layer.Transform = CoreAnimation.CATransform3D.Identity;
                entry.View.Frame = frame;
                entry.PlacedFrame = frame;
            }
            PlaceMotion(entry.View, node, created, motion, allowMotion);
            var enabled = node.Enabled && !node.Inert;
            if (entry.Presented is not { } old || old.Enabled != node.Enabled || old.Inert != node.Inert || reparented)
            {
                entry.View.UserInteractionEnabled = enabled &&
                    entry.View is not (UILabel or UIImageView) && parent is not UIButton;
                entry.View.AccessibilityElementsHidden = node.Inert;
            }
            if (entry.Presented is not { } identified || identified.Key != node.Key)
            {
                entry.View.AccessibilityIdentifier = node.Key ?? node.Id.ToString("x");
                if (entry.Owner is IntrinsicOwner intrinsic)
                {
                    intrinsic.Control.AccessibilityIdentifier = entry.View.AccessibilityIdentifier;
                    entry.View.AccessibilityIdentifier += "-container";
                }
            }
            if (reorder || reparented)
                parent.BringSubviewToFront(entry.View);
            entry.Presented = node;
        }
        placedOrder = controls.Select(node => node.Id).ToArray();
        presentedTextMetrics = textMetrics;
        FinishMotion(controls);
        FinishNavigation();
        PlaceAlerts();
        suppressNavigationMotion = false;
        foreach (var entry in entries.Values)
            if (entry.View is NativeSwipeDeckView deck)
                deck.Reconcile();
    }

    static bool StaticAppearance(object content) => content is NativeBoxContent or NativeTextContent or
        NativeLabelContent or NativeIconContent or NativeImageContent or NativeGradientContent or NativeSwipeCardContent or
        NativeOrbContent or NativeDashContent;

    Entry Create(NativeControlNode node)
    {
        if (node.Content is NativeButtonContent or NativeBackdrop or NativeActionContent or NativePressableContent)
        {
            UIButton button = node.Content is NativeActionContent ? new NativeActionButton(guard) :
                node.Content is NativePressableContent ? new NativePressableButton(guard) :
                new UIButton(node.Content is NativeBackdrop ? UIButtonType.Custom : UIButtonType.System);
            var id = node.Id;
            void Activate() => guard(() =>
            {
                if (current.TryGetValue(id, out var latest) && latest.Enabled && !latest.Inert)
                {
                    if (latest.Content is NativePressableContent { Haptics: true } && root.Window is { Hidden: false })
                    {
                        using var feedback = new UISelectionFeedbackGenerator();
                        feedback.SelectionChanged();
                    }
                    if (latest.Content is NativePressableContent { Timed: true })
                        QueueValue(id, Environment.TickCount64);
                    else
                        Invoke(id);
                }
            });
            var action = UIAction.Create(_ => guard(() =>
            {
                if (button is not NativePressableButton press || !press.ConsumeRelease())
                    Activate();
            }));
            if (button is NativePressableButton composed)
                composed.PressIn = Activate;
            button.AddAction(action, UIControlEvent.PrimaryActionTriggered);
            if (node.Content is NativeBackdrop)
            {
                var container = new UIView();
                button.AutoresizingMask = UIViewAutoresizing.FlexibleWidth | UIViewAutoresizing.FlexibleHeight;
                button.Frame = container.Bounds;
                container.AddSubview(button);
                return new(container, action, Target: button);
            }
            return new(button, action, Target: button);
        }
        if (node.Content is NativeTextContent)
            return new(new UILabel());
        if (node.Content is NativeListContent)
        {
            var table = new UITableView(CGRect.Empty, UITableViewStyle.InsetGrouped);
            var source = new ListSource(this, node.Id);
            table.Source = source;
            table.RowHeight = UITableView.AutomaticDimension;
            table.EstimatedRowHeight = 60;
            return new(table, Owner: source);
        }
        if (node.Content is NativeNavigationContent or NativeNavigationPageContent)
            return CreateNavigation(node);
        if ((CreateAppearance(node) ?? CreateHosted(node) ?? CreateStyled(node) ?? CreateStandard(node) ?? CreatePresentation(node)) is { } appearance)
            return appearance;
        throw new NotSupportedException($"Unsupported native content: {node.Content.GetType().Name}.");
    }

    static void Update(Entry entry, NativeControlNode node)
    {
        switch (node.Content)
        {
            case NativeButtonContent button:
                Configure((UIButton)entry.View, button.Label, button.Options);
                ((UIButton)entry.View).Enabled = node.Enabled && !node.Inert;
                break;
            case NativePressableContent pressable:
                ((NativePressableButton)entry.View).PressOnTouchDown = pressable.PressOnTouchDown;
                ((NativePressableButton)entry.View).Configure(pressable.Label, node.Enabled && !node.Inert,
                    pressable.PressedScale, pressable.ReduceMotion);
                break;
            case NativeTextContent text:
                Configure((UILabel)entry.View, text);
                break;
            case NativeBackdrop:
                entry.Target!.BackgroundColor = UIColor.Black.ColorWithAlpha(.35f);
                entry.Target.AccessibilityLabel = "Dismiss drawer";
                entry.Target.AccessibilityIdentifier = (node.Key ?? node.Id.ToString("x")) + "-backdrop";
                break;
            case NativeListContent list:
                var table = (UITableView)entry.View;
                ((ListSource)entry.Owner!).Update(table, list);
                table.AccessibilityLabel = list.Label;
                break;
            default:
                UpdateAppearance(entry, node);
                UpdateHosted(entry, node);
                UpdateStyled(entry, node);
                UpdateStandard(entry, node);
                UpdatePresentation(entry, node);
                break;
        }
    }

    void Remove(ulong id)
    {
        var entry = entries[id];
        entry.View.Layer.RemoveAllAnimations();
        entries.Remove(id);
        current.Remove(id);
        values.Remove(id);
        navigationCommits.Remove(id);
        DisposeEntry(entry);
    }

    static void DisposeEntry(Entry entry)
    {
        if (entry.Action is { } action)
        {
            entry.Target!.RemoveAction(action, UIControlEvent.PrimaryActionTriggered);
            action.Dispose();
        }
        if (entry.View is UITableView table)
            table.Source = null;
        if (entry.View is UITabBar bar)
            bar.Delegate = null;
        if (entry.Target is { } target && target != entry.View)
        {
            target.RemoveFromSuperview();
            target.Dispose();
        }
        entry.Owner?.Dispose();
        entry.View.RemoveFromSuperview();
        entry.View.Dispose();
    }

    public void Dispose()
    {
        RetireSheets([], animated: false);
        RetireAlerts([], animated: false);
        RetireNavigationPages([]);
        foreach (var id in entries.Keys.Reverse().ToArray())
            Remove(id);
        selections.Clear();
        currentMotionIds.Clear();
        values.Clear();
        navigationCommits.Clear();
        measure.Dispose();
        measureLabel.Dispose();
        measureTabBar.Dispose();
        measureSwitch.Dispose();
        measureStepper.Dispose();
    }
}
