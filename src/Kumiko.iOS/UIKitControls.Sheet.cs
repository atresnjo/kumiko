using CoreGraphics;
using Kumiko.UI;
using UIKit;

namespace Kumiko.iOS;

internal sealed partial class UIKitControls
{
    internal Size SheetSize(ulong overlay, Size fallback)
    {
        var owner = entries.Values.Select(entry => entry.Owner).OfType<SheetOwner>()
            .FirstOrDefault(sheet => sheet.Overlay == overlay);
        if (owner?.Viewport is { } size)
            return size;
        if (owner?.Controller.SheetPresentationController?.SelectedDetentIdentifier == UISheetPresentationControllerDetentIdentifier.Large)
            return fallback with
            {
                Height = Math.Max(0, (float)root.Bounds.Height - 24)
            };
        return fallback;
    }

    Entry CreateSheet(NativeControlNode node, NativeSheetContent content)
    {
        var owner = new SheetOwner(this, node.Parent, content);
        return new(owner.Controller.View!, Owner: owner);
    }

    // Native controllers retain their content until UIKit finishes dismissal. Remove the
    // logical entries immediately so reopening cannot reuse a departing controller.
    void RetireSheets(HashSet<ulong> live, bool animated = true)
    {
        foreach (var pair in entries.Where(pair => !live.Contains(pair.Key) && pair.Value.Owner is SheetOwner).ToArray())
        {
            var ids = entries.Keys.Where(id => DescendsFrom(id, pair.Key)).Reverse().ToArray();
            var retired = ids.Select(id => entries[id]).ToArray();
            foreach (var id in ids)
            {
                entries.Remove(id);
                current.Remove(id);
            }
            var sheet = (SheetOwner)pair.Value.Owner!;
            var controller = DepartWith(sheet.Controller);
            sheet.Dismiss(() =>
            {
                // Release carried alerts first; disposing the entries releases this controller.
                Departed(controller);
                foreach (var entry in retired)
                    DisposeEntry(entry);
                requestFrame();
            }, animated);
        }
    }

    bool DescendsFrom(ulong id, ulong ancestor)
    {
        while (id != 0)
        {
            if (id == ancestor)
                return true;
            id = current.TryGetValue(id, out var node) ? node.Parent : 0;
        }
        return false;
    }

    sealed class SheetOwner : IDisposable
    {
        readonly UIKitControls renderer;
        readonly SheetDelegate listener;
        readonly UISheetPresentationController presentation;
        readonly UISheetPresentationControllerDetent collapsed;
        readonly UISheetPresentationControllerDetent expanded;
        float preferredHeight;
        bool stopped;
        bool dismissed;
        bool presented;
        bool presenting;
        Action? pendingDismissal;
        bool pendingAnimation;
        internal ulong Overlay
        {
            get;
        }
        internal SheetController Controller
        {
            get;
        }
        internal Size? Viewport
        {
            get; private set;
        }

        bool Animate => renderer.Root.Window is { Hidden: false } && !UIAccessibility.IsReduceMotionEnabled;
        internal bool Transitioning => presenting;

        internal SheetOwner(UIKitControls renderer, ulong overlay, NativeSheetContent content)
        {
            this.renderer = renderer;
            Overlay = overlay;
            preferredHeight = content.Height;
            Controller = new SheetController(renderer.Guard, Resized)
            {
                ModalPresentationStyle = UIModalPresentationStyle.PageSheet,
                ModalInPresentation = false,
                View = new NativeSurfaceView(renderer.Guard) { AccessibilityViewIsModal = true }
            };
            listener = new SheetDelegate(renderer.Guard, () =>
            {
                if (stopped || dismissed)
                    return;
                dismissed = true;
                renderer.Invoke(Overlay);
            }, renderer.RequestFrame);
            collapsed = OperatingSystem.IsIOSVersionAtLeast(16)
                ? UISheetPresentationControllerDetent.Create("preferred", resolution =>
                    OperatingSystem.IsIOSVersionAtLeast(16)
                        ? Math.Min(preferredHeight, (float)resolution.MaximumDetentValue) : preferredHeight)
                : UISheetPresentationControllerDetent.CreateMediumDetent();
            expanded = UISheetPresentationControllerDetent.CreateLargeDetent();
            presentation = Controller.SheetPresentationController!;
            presentation.Detents = [collapsed, expanded];
            presentation.PrefersGrabberVisible = true;
            presentation.PreferredCornerRadius = 28;
            presentation.PrefersScrollingExpandsWhenScrolledToEdge = true;
            presentation.Delegate = listener;
            Update(content);
        }

        internal void Update(NativeSheetContent content)
        {
            var surface = (NativeSurfaceView)Controller.View!;
            if (content.Palette is { } palette)
                surface.Configure(new(palette.Surface, Radius: 28));
            else
            {
                surface.BackgroundColor = UIColor.SecondarySystemBackground;
                surface.Layer.CornerRadius = 28;
                surface.ClipsToBounds = true;
            }
            if (preferredHeight != content.Height)
            {
                preferredHeight = content.Height;
                if (OperatingSystem.IsIOSVersionAtLeast(16))
                    presentation.InvalidateDetents();
            }
        }

        void Resized()
        {
            if (!presented || stopped || dismissed)
                return;
            var bounds = Controller.View!.Bounds;
            var size = new Size((float)bounds.Width,
                Math.Max(0, (float)(bounds.Height - Controller.View.SafeAreaInsets.Bottom)));
            if (size.Width > 0 && size.Height > 0 && Viewport != size)
            {
                Viewport = size;
                renderer.RequestFrame();
            }
        }

        internal void Place(UIView parent, NativeControlNode node)
        {
            if (stopped || dismissed)
                return;
            var view = Controller.View!;
            if (renderer.Root.Window is not null)
            {
                // The host's responder chain supplies the real scene controller.
                UIViewController? host = null;
                for (UIResponder? responder = renderer.Root; responder is not null; responder = responder.NextResponder)
                    if (responder is UIViewController controller)
                    {
                        host = controller;
                        break;
                    }
                if (host is null)
                    throw new InvalidOperationException("A visible NativeView sheet requires a UIViewController host.");
                if (renderer.entries.TryGetValue(Overlay, out var overlay))
                    overlay.Target!.Hidden = true;
                if (!presented && host.PresentedViewController is null)
                {
                    view.RemoveFromSuperview();
                    presented = true;
                    presenting = true;
                    host.PresentViewController(Controller, Animate,
                        () => renderer.Guard(() =>
                        {
                            presenting = false;
                            if (pendingDismissal is { } completed)
                            {
                                pendingDismissal = null;
                                Dismiss(completed, pendingAnimation);
                            }
                            else
                            {
                                Resized();
                                // A queued alert presents above this sheet once it settles.
                                renderer.RequestFrame();
                            }
                        }));
                }
                return;
            }
            // Detached rendering exercises the same controller and delegates without a window.
            if (view.Superview != parent)
                parent.AddSubview(view);
            var origin = renderer.current[Overlay].Bounds;
            view.Frame = new CGRect(node.Bounds.X - origin.X, node.Bounds.Y - origin.Y, node.Bounds.Width, node.Bounds.Height);
            parent.BringSubviewToFront(view);
        }

        internal void Dismiss(Action completed, bool animated = true)
        {
            if (presenting)
            {
                pendingDismissal = completed;
                pendingAnimation = animated;
                return;
            }
            // Dismissing from the presenter also removes an alert presented above this sheet.
            if (Controller.PresentingViewController is { } presenter)
                presenter.DismissViewController(animated && Animate,
                    () => renderer.Guard(completed));
            else
                completed();
        }

        public void Dispose()
        {
            if (stopped)
                return;
            stopped = true;
            presentation.Delegate = null;
            listener.Dispose();
            collapsed.Dispose();
            expanded.Dispose();
            Controller.View = null;
            Controller.Dispose();
            presentation.Dispose();
        }
    }

    sealed class SheetController(Action<Action> guard, Action resized) : UIViewController
    {
        public override void ViewDidLayoutSubviews()
        {
            base.ViewDidLayoutSubviews();
            guard(resized);
        }
    }

    sealed class SheetDelegate(Action<Action> guard, Action dismissed, Action changed) : UISheetPresentationControllerDelegate
    {
        public override void DidDismiss(UIPresentationController presentationController) => guard(dismissed);
        public override void DidChangeSelectedDetentIdentifier(UISheetPresentationController sheetPresentationController) => guard(changed);
    }
}
