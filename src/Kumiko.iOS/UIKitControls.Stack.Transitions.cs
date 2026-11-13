using Foundation;
using UIKit;

namespace Kumiko.iOS;

internal sealed partial class UIKitControls
{
    sealed partial class StackOwner
    {
        /// <summary>False while UIKit still runs a transition; recovers from one that ended without didShow.</summary>
        bool Settled()
        {
            if (transitioning && !InTransition())
            {
                transitioning = false;
                gestureRequest = null;
            }
            return !transitioning;
        }
        bool BeginPop(UIGestureRecognizer recognizer, IUIGestureRecognizerDelegate? original)
        {
            if (stopped || applying || !Settled() || content.Generation != renderer.NavigationGeneration)
                return false;
            var before = Desired.Select(n => ((NativeNavigationPageContent)n.Content).Id).ToArray();
            var generation = content.Generation;
            renderer.SettleNavigationInput();
            if (generation != content.Generation || !before.SequenceEqual(Desired.Select(n => ((NativeNavigationPageContent)n.Content).Id)))
                return false;
            var desired = Desired;
            if (stopped || desired.Length < 2)
                return false;
            var target = ((NativeNavigationPageContent)desired[^2].Content).Id;
            var request = Request(target);
            if (request is null)
                return false;
            if (((NativeNavigationPageContent)desired[^1].Content).Guarded)
            {
                QueueBack(request);
                return false;
            }
            if (original is not null && !original.ShouldBegin(recognizer))
                return false;
            gestureRequest = request;
            return true;
        }
        void DidShow(UIViewController controller)
        {
            if (stopped || content.Generation != renderer.NavigationGeneration)
                return;
            transitioning = false;
            var shown = ReferenceEquals(Controller.TopViewController, controller);
            if (!applying && gestureRequest is { } request)
            {
                gestureRequest = null;
                var target = pages.FirstOrDefault(pair => ReferenceEquals(pair.Value.Controller, controller));
                if (shown && target.Value is not null && target.Key == request.To)
                {
                    suppressNextAnimation = true;
                    QueueBack(request);
                }
            }
            else if (!applying && shown && UserPop(controller) is { } target && Request(target) is { } back)
            {
                // UIKit popped on its own (e.g. an accessibility escape or native command).
                suppressNextAnimation = true;
                QueueBack(back);
            }
            renderer.RequestFrame();
            UIAccessibility.PostNotification(UIAccessibilityPostNotification.ScreenChanged, FocusTarget(controller));
        }
        /// <summary>
        /// The page UIKit popped to by itself: the native stack is a strict prefix of the applied path.
        /// The stack's own transitions, and late callbacks for pages no longer on top, are not user pops.
        /// </summary>
        int? UserPop(UIViewController controller)
        {
            var native = Controller.ViewControllers ?? [];
            if (native.Length == 0 || native.Length >= appliedControllers.Length ||
                !native.Select((c, i) => ReferenceEquals(c, appliedControllers[i])).All(same => same))
                return null;
            var index = Array.FindIndex(appliedControllers, c => ReferenceEquals(c, controller));
            return index == native.Length - 1 ? appliedIds[index] : null;
        }
        void WillShow(UIViewController controller, bool animated)
        {
            transitioning = animated;
            if (!animated || Controller.GetTransitionCoordinator() is not { } coordinator)
                return;
            var generation = content.Generation;
            // An interactive pop can be cancelled without didShow. Its coordinator always completes.
            coordinator.AnimateAlongsideTransition(static _ => { }, context => renderer.Guard(() =>
            {
                if (stopped || generation != content.Generation)
                    return;
                transitioning = false;
                if (context.IsCancelled)
                    gestureRequest = null;
                renderer.RequestFrame();
            }));
        }
        sealed class NavigationListener(StackOwner owner) : UINavigationControllerDelegate
        {
            public override void WillShowViewController(UINavigationController navigationController, UIViewController viewController, bool animated) =>
                owner.renderer.Guard(() => owner.WillShow(viewController, animated));
            public override void DidShowViewController(UINavigationController navigationController, UIViewController viewController, bool animated) =>
                owner.renderer.Guard(() => owner.DidShow(viewController));
        }
        sealed class PopListener(StackOwner owner, IUIGestureRecognizerDelegate? original) : UIGestureRecognizerDelegate
        {
            bool Forward(string selector, Func<bool> call, bool fallback)
            {
                if (owner.stopped)
                    return false;
                var result = fallback;
                owner.renderer.Guard(() =>
                {
                    var method = new ObjCRuntime.Selector(selector);
                    if (original is NSObject target && target.RespondsToSelector(method))
                        result = call();
                });
                return result;
            }
            public override bool ShouldReceiveTouch(UIGestureRecognizer recognizer, UITouch touch) =>
                Forward("gestureRecognizer:shouldReceiveTouch:", () => original!.ShouldReceiveTouch(recognizer, touch), true);
            public override bool ShouldReceivePress(UIGestureRecognizer recognizer, UIPress press) =>
                Forward("gestureRecognizer:shouldReceivePress:", () => original!.ShouldReceivePress(recognizer, press), true);
            public override bool ShouldReceiveEvent(UIGestureRecognizer recognizer, UIEvent value) =>
                Forward("gestureRecognizer:shouldReceiveEvent:", () => original!.ShouldReceiveEvent(recognizer, value), true);
            public override bool ShouldRecognizeSimultaneously(UIGestureRecognizer recognizer, UIGestureRecognizer other) =>
                Forward("gestureRecognizer:shouldRecognizeSimultaneouslyWithGestureRecognizer:", () => original!.ShouldRecognizeSimultaneously(recognizer, other), false);
            public override bool ShouldRequireFailureOf(UIGestureRecognizer recognizer, UIGestureRecognizer other) =>
                Forward("gestureRecognizer:shouldRequireFailureOfGestureRecognizer:", () => original!.ShouldRequireFailureOf(recognizer, other), false);
            public override bool ShouldBeRequiredToFailBy(UIGestureRecognizer recognizer, UIGestureRecognizer other) =>
                Forward("gestureRecognizer:shouldBeRequiredToFailByGestureRecognizer:", () => original!.ShouldBeRequiredToFailBy(recognizer, other), false);
            public override bool ShouldBegin(UIGestureRecognizer recognizer)
            {
                var result = false;
                owner.renderer.Guard(() => result = owner.BeginPop(recognizer, original));
                return result;
            }
        }
        sealed class PageOwner : IDisposable
        {
            readonly StackOwner stack;
            readonly UIButton back;
            readonly UIAction action;
            readonly UIBarButtonItem item;
            UIView? surface;
            readonly PageController controller;
            readonly int id;
            internal Entry[] Retired = [];
            internal ulong Opener;
            internal string? OpenerRow;
            internal UIViewController Controller => controller;
            internal Kumiko.UI.Size? Viewport => surface is not null && (double)surface.Bounds.Width > 0 && (double)surface.Bounds.Height > 0
                ? new((float)surface.Bounds.Width, (float)surface.Bounds.Height) : null;
            internal PageOwner(StackOwner stack, int id)
            {
                this.stack = stack;
                this.id = id;
                controller = new PageController(stack.renderer.Guard, Layout);
                controller.LoadViewIfNeeded();
                controller.View!.AccessibilityIdentifier = "navigation-page-" + id;
                controller.View.BackgroundColor = UIColor.SystemBackground;
                back = new UIButton(UIButtonType.System);
                back.Frame = new CoreGraphics.CGRect(0, 0, 64, 44);
                action = UIAction.Create(_ => stack.renderer.Guard(() =>
                {
                    var desired = stack.Desired;
                    var index = Array.FindIndex(desired, n => ((NativeNavigationPageContent)n.Content).Id == id);
                    if (index > 0)
                        stack.Back(((NativeNavigationPageContent)desired[index - 1].Content).Id);
                }));
                back.AddAction(action, UIControlEvent.PrimaryActionTriggered);
                item = new UIBarButtonItem(back);
                Restyle();
            }
            /// <summary>Applies the stack's appearance: page background and a chevron Back in the bar tint.</summary>
            internal void Restyle()
            {
                var appearance = stack.Appearance;
                using var background = appearance is null ? null : NativePaint.Color(appearance.Background);
                controller.View!.BackgroundColor = background ?? UIColor.SystemBackground;
                if (surface is not null)
                    surface.BackgroundColor = background ?? UIColor.SystemBackground;
                var title = back.Title(UIControlState.Normal) ?? "";
                if (appearance is null)
                {
                    back.SetAttributedTitle(null, UIControlState.Normal);
                    back.SizeToFit();
                    return;
                }
                // The plain title stays the button's semantic title; the attributed one adds the system chevron.
                using var tint = NativePaint.Color(appearance.Tint);
                using var font = NativePaint.Font(17);
                using var configuration = UIImageSymbolConfiguration.Create(font, UIImageSymbolScale.Medium);
                using var chevron = UIImage.GetSystemImage("chevron.backward", configuration)?.ApplyTintColor(tint, UIImageRenderingMode.AlwaysOriginal);
                var label = new NSMutableAttributedString();
                if (chevron is not null)
                {
                    using var attachment = new NSTextAttachment { Image = chevron };
                    var bounds = chevron.Size;
                    attachment.Bounds = new CoreGraphics.CGRect(0, (font.CapHeight - bounds.Height) / 2, bounds.Width, bounds.Height);
                    using var glyph = NSAttributedString.FromAttachment(attachment);
                    label.Append(glyph);
                }
                var attributes = new UIStringAttributes { ForegroundColor = tint, Font = font };
                using var text = new NSAttributedString(" " + title, attributes);
                label.Append(text);
                back.SetAttributedTitle(label, UIControlState.Normal);
                back.SizeToFit();
            }
            internal void Update(NativeNavigationPageContent content, UIView nextSurface, bool active)
            {
                var previousSurface = surface;
                surface = nextSurface;
                var restyle = !ReferenceEquals(previousSurface, nextSurface);
                if (back.Title(UIControlState.Normal) != content.BackTitle)
                {
                    back.SetTitle(content.BackTitle, UIControlState.Normal);
                    back.AccessibilityLabel = content.BackTitle;
                    back.SizeToFit();
                    restyle = true;
                }
                if (surface.Superview != controller.View)
                    controller.View!.AddSubview(surface);
                if (restyle)
                    Restyle();
                controller.Title = content.Title;
                var first = stack.Desired.FirstOrDefault();
                controller.NavigationItem.HidesBackButton = true;
                controller.NavigationItem.LeftBarButtonItem = first.Content is NativeNavigationPageContent root && root.Id == id ? null : item;
                controller.View!.AccessibilityElementsHidden = !active;
                controller.View.UserInteractionEnabled = active;
                if (!active)
                    controller.View.EndEditing(true);
                Layout();
            }
            void Layout()
            {
                if (surface is null || controller.View is not { } view)
                    return;
                var safe = view.SafeAreaInsets;
                // Under a floating bottom bar the page reaches the edge; its scroll views inset themselves.
                var bottom = (double)view.Bounds.Height - (stack.Appearance?.BottomInset > 0 ? 0 : (double)safe.Bottom);
                // A hidden keyboard's guide rests on the safe-area bottom; only a raised keyboard shortens the page.
                var keyboardTop = (double)view.KeyboardLayoutGuide.LayoutFrame.Y;
                if (keyboardTop > safe.Top && keyboardTop < (double)view.Bounds.Height - (double)safe.Bottom - .5)
                    bottom = Math.Min(bottom, keyboardTop);
                var frame = new CoreGraphics.CGRect(safe.Left, safe.Top,
                    Math.Max(0, view.Bounds.Width - safe.Left - safe.Right), Math.Max(0, bottom - safe.Top));
                if (surface.Frame != frame)
                {
                    surface.Frame = frame;
                    stack.renderer.RequestFrame();
                }
            }
            public void Dispose()
            {
                back.RemoveAction(action, UIControlEvent.PrimaryActionTriggered);
                action.Dispose();
                item.Dispose();
                back.Dispose();
                surface?.RemoveFromSuperview();
                surface = null;
                controller.Dispose();
                foreach (var entry in Retired)
                    DisposeEntry(entry);
                Retired = [];
            }
        }
        sealed class PageController : UIViewController
        {
            readonly Action<Action>? guard;
            readonly Action? layout;
            internal PageController(Action<Action> guard, Action layout)
            {
                this.guard = guard;
                this.layout = layout;
            }
            // UIKit may call a controller whose managed owner was already disposed; it then has no work.
            PageController(ObjCRuntime.NativeHandle handle) : base(handle)
            {
            }
            public override void ViewDidLayoutSubviews()
            {
                base.ViewDidLayoutSubviews();
                if (guard is not null && layout is not null)
                    guard(layout);
            }
        }
    }
}
