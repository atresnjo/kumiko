using CoreGraphics;
using Foundation;
using Kumiko.UI;
using UIKit;

namespace Kumiko.iOS;

internal sealed partial class UIKitControls
{
    long navigationGeneration;
    internal NativeNavigationAppearance? PendingNavigationAppearance;
    internal NativeNavigationAppearance? TakeNavigationAppearance()
    {
        var appearance = PendingNavigationAppearance;
        PendingNavigationAppearance = null;
        return appearance;
    }
    bool suppressNavigationMotion;
    internal long NavigationGeneration
    {
        get => navigationGeneration;
        set
        {
            if (navigationGeneration == value)
                return;
            navigationGeneration = value;
            suppressNavigationMotion = true;
            foreach (var stack in entries.Where(pair => pair.Value.Owner is StackOwner).Select(pair => pair.Key).ToArray())
            {
                var owned = entries.Keys.Where(id => DescendsFrom(id, stack)).Reverse().ToArray();
                // Cancel controller ownership before disposing descendants or accepting another callback.
                Remove(stack);
                foreach (var id in owned)
                    if (id != stack)
                        Remove(id);
            }
            values.Clear();
            navigationCommits.Clear();
            texts.Clear();
            selections.Clear();
        }
    }
    internal Size NavigationSize(ulong id, Size fallback)
    {
        var size = entries.TryGetValue(id, out var entry) && entry.Owner is StackOwner stack
            ? stack.PageSize(fallback) : fallback with
            {
                Height = Math.Max(1, fallback.Height - 44)
            };
        // UIKit reports its safe viewport after layout; a shrinking host must
        // already bound this frame's tree while that feedback is pending.
        return new(Math.Min(size.Width, fallback.Width), Math.Min(size.Height, fallback.Height));
    }

    Entry CreateNavigation(NativeControlNode node) => node.Content switch
    {
        NativeNavigationContent content => StackEntry(node.Id, content),
        NativeNavigationPageContent => new(new UIView { BackgroundColor = UIColor.SystemBackground, ClipsToBounds = true }),
        _ => throw new InvalidOperationException("Not navigation content")
    };
    Entry StackEntry(ulong id, NativeNavigationContent content)
    {
        var owner = new StackOwner(this, id, content);
        return new(owner.Controller.View!, Owner: owner);
    }

    void RetireNavigationPages(HashSet<ulong> live)
    {
        foreach (var pair in entries.Where(pair => !live.Contains(pair.Key) &&
            current.TryGetValue(pair.Key, out var node) && node.Content is NativeNavigationPageContent).ToArray())
        {
            var page = current[pair.Key];
            if (!entries.TryGetValue(page.Parent, out var stackEntry) || stackEntry.Owner is not StackOwner stack)
                continue;
            var ids = entries.Keys.Where(id => DescendsFrom(id, pair.Key)).Reverse().ToArray();
            var retired = ids.Select(id => entries[id]).ToArray();
            foreach (var id in ids)
            {
                entries.Remove(id);
                current.Remove(id);
                values.Remove(id);
                navigationCommits.Remove(id);
                texts.Remove(id);
            }
            stack.RetirePage(((NativeNavigationPageContent)page.Content).Id, retired);
        }
    }

    void FinishNavigation()
    {
        foreach (var entry in entries.Values.ToArray())
            if (entry.Owner is StackOwner owner)
                owner.Reconcile();
    }

    void SettleNavigationInput()
    {
        root.EndEditing(true);
        CommitNavigationText();
        // A frame consumes pending edits and rebuilds guards before any native pop is authorized.
        // Each settle pass consumes one queued value per control; drain a backlog before deciding.
        for (var frame = 0; frame < 8 && root is NativeView view; frame++)
        {
            view.Render();
            if (!PendingText())
                break;
        }
    }

    void CommitNavigationText()
    {
        foreach (var pair in entries.ToArray())
        {
            if (!Active(pair.Key) || !current.TryGetValue(pair.Key, out var node))
                continue;
            if (pair.Value.View is UITextView editor && node.Content is NativeEditorContent text)
            {
                editor.UnmarkText();
                if ((editor.Text ?? "") != text.Value)
                    QueueLatestText(pair.Key, editor.Text ?? "");
            }
            if (pair.Value.View is UITextField field && node.Content is NativeTextFieldContent input)
            {
                field.UnmarkText();
                if ((field.Text ?? "") != input.Value)
                    texts[pair.Key] = field.Text ?? "";
            }
        }
    }

    bool PendingText() => texts.Keys.Any(Active) ||
        values.Keys.Any(id => Active(id) && entries.TryGetValue(id, out var entry) && entry.View is UITextView or UITextField);

    sealed partial class StackOwner : IDisposable
    {
        readonly UIKitControls renderer;
        readonly ulong id;
        readonly NavigationListener listener;
        readonly PopListener gesture;
        readonly UIGestureRecognizer[] recognizers;
        readonly NSObject? systemDelegate;
        readonly Dictionary<int, PageOwner> pages = new();
        NativeNavigationContent content;
        bool stopped, applying, transitioning, suppressNextAnimation, styled;
        NativeBackRequest? gestureRequest;
        // The path last handed to UIKit, and the model revision it represents.
        int[] appliedIds = [];
        UIViewController[] appliedControllers = [];
        long appliedRevision;
        internal UINavigationController Controller
        {
            get;
        }

        internal StackOwner(UIKitControls renderer, ulong id, NativeNavigationContent content)
        {
            this.renderer = renderer;
            this.id = id;
            this.content = content;
            Controller = new UINavigationController();
            Controller.NavigationBar.Translucent = false;
            Controller.LoadViewIfNeeded();
            listener = new(this);
            Controller.Delegate = listener;
            // The binding's typed Delegate getter returns null for UIKit's private delegate; wrap the
            // native object so its arbitration (begin, touch and failure relationships) still applies.
            var edge = Controller.InteractivePopGestureRecognizer;
            systemDelegate = edge?.WeakDelegate;
            var original = systemDelegate is null ? null : ObjCRuntime.Runtime.GetINativeObject<IUIGestureRecognizerDelegate>(systemDelegate.Handle, false);
            gesture = new(this, original);
            UIGestureRecognizer?[] candidates = [edge, OperatingSystem.IsIOSVersionAtLeast(26) ? Controller.InteractiveContentPopGestureRecognizer : null];
            recognizers = [.. candidates.OfType<UIGestureRecognizer>().Distinct()];
            // Every system pop recognizer must settle input and consult guards before it begins.
            foreach (var recognizer in recognizers)
                recognizer.Delegate = gesture;
        }
        internal Size PageSize(Size fallback)
        {
            var active = pages.Values.FirstOrDefault(page => ReferenceEquals(page.Controller, Controller.TopViewController));
            if (active?.Viewport is { } viewport)
                return viewport;
            var top = Controller.TopViewController?.View;
            if (top is not null && top.Bounds.Width > 0 && top.Bounds.Height > 0)
                return new((float)top.Bounds.Width, Math.Max(1, (float)(top.Bounds.Height - top.SafeAreaInsets.Top - top.SafeAreaInsets.Bottom)));
            return fallback with
            {
                Height = Math.Max(1, fallback.Height - 44)
            };
        }
        NativeControlNode[] Desired => renderer.placedOrder.Where(renderer.current.ContainsKey).Select(key => renderer.current[key])
            .Where(n => n.Parent == id && n.Content is NativeNavigationPageContent).ToArray();
        internal void Reconcile()
        {
            if (stopped || !renderer.current.TryGetValue(id, out var stackNode) || stackNode.Content is not NativeNavigationContent next)
                return;
            var reset = content.Generation != next.Generation;
            var restyle = reset || content.Appearance != next.Appearance || !styled;
            content = next;
            if (restyle)
                Style(next.Appearance);
            Overlay(next.Appearance?.BottomInset ?? 0);
            if (reset)
            {
                gestureRequest = null;
                transitioning = false;
            }
            // A cancelled interactive pop need not deliver didShow. Without a transition in flight the
            // native stack is settled, so stop deferring rather than freezing reconciliation.
            if (!Settled())
                return;
            var desired = Desired;
            if (desired.Length == 0)
                throw new InvalidOperationException("Navigation requires a root.");
            if (desired.Select(n => ((NativeNavigationPageContent)n.Content).Id).Distinct().Count() != desired.Length)
                throw new InvalidOperationException("Duplicate native page identity.");
            foreach (var node in desired)
            {
                var page = (NativeNavigationPageContent)node.Content;
                if (!pages.TryGetValue(page.Id, out var owner))
                    pages.Add(page.Id, owner = new(this, page.Id));
                owner.Update(page, renderer.entries[node.Id].View, node == desired[^1]);
            }
            var controllers = desired.Select(n => pages[((NativeNavigationPageContent)n.Content).Id].Controller).ToArray();
            var previous = Controller.ViewControllers ?? [];
            var changed = !previous.SequenceEqual(controllers);
            if (changed)
            {
                var animate = !reset && !suppressNextAnimation && previous.Length > 0 && renderer.Root.Window is { Hidden: false } && !UIAccessibility.IsReduceMotionEnabled;
                applying = true;
                try
                {
                    Controller.SetViewControllers(controllers, animate);
                }
                finally
                {
                    applying = false;
                }
                if (!animate)
                    transitioning = false;
            }
            appliedIds = [.. desired.Select(n => ((NativeNavigationPageContent)n.Content).Id)];
            appliedControllers = controllers;
            appliedRevision = content.Revision;
            // Do not dispose controllers still owned by a native transition.
            if (!transitioning)
                foreach (var key in pages.Keys.Where(key => !desired.Any(n => ((NativeNavigationPageContent)n.Content).Id == key)).ToArray())
                {
                    pages[key].Dispose();
                    pages.Remove(key);
                }
            suppressNextAnimation = false;
            Controller.View!.LayoutIfNeeded();
        }
        internal NativeNavigationAppearance? Appearance => content.Appearance;
        /// <summary>
        /// A floating bottom bar joins the safe area, as in a tab bar controller: UIKit scroll views then inset their
        /// content and indicators by it, while page surfaces still reach the bottom edge.
        /// </summary>
        void Overlay(float height)
        {
            var extra = Math.Max(0, height - (double)renderer.Root.SafeAreaInsets.Bottom);
            var insets = new UIEdgeInsets(0, 0, (nfloat)extra, 0);
            if (Controller.AdditionalSafeAreaInsets != insets)
                Controller.AdditionalSafeAreaInsets = insets;
        }
        void Style(NativeNavigationAppearance? appearance)
        {
            styled = true;
            var bar = Controller.NavigationBar;
            using var standard = new UINavigationBarAppearance();
            if (appearance is null)
            {
                standard.ConfigureWithDefaultBackground();
                bar.TintColor = null!; // nil restores the inherited system tint.
            }
            else
            {
                standard.ConfigureWithOpaqueBackground();
                using var background = NativePaint.Color(appearance.Background);
                using var title = NativePaint.Color(appearance.Title);
                using var separator = NativePaint.Color(appearance.Separator);
                using var tint = NativePaint.Color(appearance.Tint);
                using var font = NativePaint.Font(appearance.TitleSize, NativePaint.Weight(appearance.TitleWeight));
                standard.BackgroundColor = background;
                standard.ShadowColor = separator;
                standard.TitleTextAttributes = new UIStringAttributes { ForegroundColor = title, Font = font };
                bar.TintColor = tint;
            }
            bar.StandardAppearance = standard;
            bar.ScrollEdgeAppearance = standard;
            bar.CompactAppearance = standard;
            foreach (var page in pages.Values)
                page.Restyle();
        }
        internal void RetirePage(int pageId, Entry[] retired)
        {
            if (pages.TryGetValue(pageId, out var page))
                page.Retired = retired;
            else
                foreach (var entry in retired)
                    DisposeEntry(entry);
        }
        bool InTransition() => Controller.GetTransitionCoordinator() is not null ||
            Controller.TopViewController?.GetTransitionCoordinator() is not null;
        /// <summary>A request from the path UIKit shows, versioned by the model revision that path represents.</summary>
        NativeBackRequest? Request(int target)
        {
            if (appliedIds.Length < 2 || !appliedIds.Take(appliedIds.Length - 1).Contains(target))
                return null;
            return new(content.Stack, appliedRevision, appliedIds[^1], target);
        }
        internal void Back(int target)
        {
            if (stopped || !Settled() || content.Generation != renderer.NavigationGeneration)
                return;
            var before = Desired.Select(n => ((NativeNavigationPageContent)n.Content).Id).ToArray();
            var generation = content.Generation;
            renderer.SettleNavigationInput();
            if (generation != content.Generation || !before.SequenceEqual(Desired.Select(n => ((NativeNavigationPageContent)n.Content).Id)))
                return;
            if (!stopped && Request(target) is { } back)
                QueueBack(back);
        }
        void QueueBack(NativeBackRequest back)
        {
            if (!renderer.values.TryGetValue(id, out var pending) || !pending.Contains(back))
                renderer.QueueValue(id, back);
        }
        public void Dispose()
        {
            if (stopped)
                return;
            stopped = true;
            gestureRequest = null;
            Controller.WeakDelegate = null;
            foreach (var recognizer in recognizers)
                recognizer.WeakDelegate = systemDelegate;
            Controller.SetViewControllers([], false);
            foreach (var page in pages.Values)
                page.Dispose();
            pages.Clear();
            Controller.WillMoveToParentViewController(null);
            Controller.View!.RemoveFromSuperview();
            Controller.RemoveFromParentViewController();
            listener.Dispose();
            gesture.Dispose();
            Controller.Dispose();
        }
    }
}
