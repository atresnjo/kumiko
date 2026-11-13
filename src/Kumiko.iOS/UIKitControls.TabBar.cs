using CoreGraphics;
using UIKit;

namespace Kumiko.iOS;

internal sealed partial class UIKitControls
{
    readonly UITabBar measureTabBar = new(CGRect.Empty);

    internal float MeasureTabBar(float width) =>
        (float)measureTabBar.SizeThatFits(new CGSize(width, float.MaxValue)).Height;

    Entry CreateTabBar(Kumiko.UI.NativeControlNode node)
    {
        var bar = new UITabBar { ItemPositioning = UITabBarItemPositioning.Fill };
        var owner = new TabBarOwner(this, node.Id);
        bar.Delegate = owner;
        return new(bar, Owner: owner);
    }

    sealed class TabBarOwner(UIKitControls renderer, ulong id) : UITabBarDelegate
    {
        readonly Dictionary<string, UITabBarItem> items = new(StringComparer.Ordinal);
        NativeTabBarContent? previous;
        bool stopped;

        internal void Update(UITabBar bar, NativeTabBarContent content)
        {
            if (previous is null || !previous.Items.SequenceEqual(content.Items))
            {
                var obsolete = items.Keys.Where(key => !content.Items.Any(item => item.Id == key)).ToArray();
                foreach (var item in content.Items)
                {
                    if (!items.TryGetValue(item.Id, out var native))
                    {
                        native = new UITabBarItem();
                        items.Add(item.Id, native);
                    }
                    native.Title = item.Label;
                    native.AccessibilityIdentifier = item.Id;
                    native.BadgeValue = item.Badge;
                    using var image = UIImage.GetSystemImage(item.Symbol)
                        ?? throw new NotSupportedException($"Unknown SF Symbol '{item.Symbol}'.");
                    native.Image = image;
                }
                bar.SetItems(content.Items.Select(item => items[item.Id]).ToArray(), false);
                foreach (var key in obsolete)
                {
                    items[key].Dispose();
                    items.Remove(key);
                }
            }
            bar.SelectedItem = items[content.SelectedId];
            if (previous is null || previous.Palette != content.Palette || previous.Tint != content.Tint)
                Style(bar, content);
            previous = content;
        }

        static void Style(UITabBar bar, NativeTabBarContent content)
        {
            if (content.Palette is not { } p)
            {
                // The system appearance keeps UIKit's material; only the selected item takes the app's tint.
                using var system = new UITabBarAppearance();
                system.ConfigureWithDefaultBackground();
                bar.StandardAppearance = system;
                bar.ScrollEdgeAppearance = system;
                using var tint = content.Tint is { } color ? NativePaint.Color(color) : null;
                bar.TintColor = tint!;
                bar.UnselectedItemTintColor = null!;
                return;
            }
            using var appearance = new UITabBarAppearance();
            appearance.ConfigureWithOpaqueBackground();
            using var background = NativePaint.Color(p.Surface);
            using var border = NativePaint.Color(p.Border);
            using var accent = NativePaint.Color(p.Accent);
            using var muted = NativePaint.Color(p.Muted);
            appearance.BackgroundColor = background;
            appearance.ShadowColor = border;
            bar.StandardAppearance = appearance;
            bar.ScrollEdgeAppearance = appearance;
            bar.TintColor = accent;
            bar.UnselectedItemTintColor = muted;
        }

        public override void ItemSelected(UITabBar tabBar, UITabBarItem item) => renderer.Guard(() =>
        {
            if (stopped)
                return;
            var selected = items.FirstOrDefault(pair => pair.Value == item).Key;
            if (selected is not null)
                renderer.Select(id, selected);
        });

        protected override void Dispose(bool disposing)
        {
            if (disposing && !stopped)
            {
                stopped = true;
                foreach (var item in items.Values)
                    item.Dispose();
                items.Clear();
            }
            base.Dispose(disposing);
        }
    }
}
