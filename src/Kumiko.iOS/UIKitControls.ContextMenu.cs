using CoreGraphics;
using Kumiko.UI;
using UIKit;

namespace Kumiko.iOS;

internal sealed partial class UIKitControls
{
    Entry? CreatePresentation(NativeControlNode node) => node.Content switch
    {
        NativeAlertContent alert => CreateAlert(node, alert),
        NativeContextMenuContent => CreateContextMenu(node.Id),
        _ => null
    };

    static void UpdatePresentation(Entry entry, NativeControlNode node)
    {
        switch (node.Content)
        {
            case NativeAlertContent alert:
                ((AlertOwner)entry.Owner!).Update(alert);
                break;
            case NativeContextMenuContent menu:
                ((ContextMenuOwner)entry.Owner!).Update(menu);
                break;
        }
    }

    Entry CreateContextMenu(ulong id)
    {
        var view = new UIView();
        return new(view, Owner: new ContextMenuOwner(this, view, id));
    }

    // UIKit holds its interaction delegate weakly; the entry owns this delegate until removal.
    sealed class ContextMenuOwner : UIContextMenuInteractionDelegate
    {
        readonly UIKitControls renderer;
        readonly UIView view;
        readonly ulong id;
        readonly UIContextMenuInteraction interaction;
        readonly Dictionary<string, UIAction> actions = new(StringComparer.Ordinal);
        NativeContextMenuContent? previous;
        UIMenu? menu;
        bool stopped;

        internal ContextMenuOwner(UIKitControls renderer, UIView view, ulong id)
        {
            this.renderer = renderer;
            this.view = view;
            this.id = id;
            interaction = new UIContextMenuInteraction(this);
            view.AddInteraction(interaction);
        }

        internal void Update(NativeContextMenuContent content)
        {
            if (previous is { } before && before.Label == content.Label && before.Title == content.Title &&
                before.Disabled == content.Disabled && before.Items.SequenceEqual(content.Items))
                return;
            var obsolete = actions.Keys.Where(key => !content.Items.Any(item => item.Id == key)).ToArray();
            foreach (var item in content.Items)
            {
                if (!actions.TryGetValue(item.Id, out var action))
                {
                    var commandId = item.Id;
                    action = UIAction.Create(item.Label, null, null, _ => renderer.Guard(() =>
                    {
                        if (!stopped && renderer.current.TryGetValue(id, out var latest) &&
                            latest.Content is NativeContextMenuContent { Disabled: false } snapshot &&
                            snapshot.Items.Any(candidate => candidate.Id == commandId && candidate.Enabled))
                            renderer.QueueValue(id, commandId);
                    }));
                    actions.Add(item.Id, action);
                }
                using var image = item.Symbol is { } symbol ? UIImage.GetSystemImage(symbol) : null;
                action.Title = item.Label;
                action.Image = image;
                action.Attributes = (item.Enabled ? 0 : UIMenuElementAttributes.Disabled) |
                    (item.Destructive ? UIMenuElementAttributes.Destructive : 0);
            }
            var next = UIMenu.Create(content.Title ?? "", content.Items.Select(item => (UIMenuElement)actions[item.Id]).ToArray());
            menu?.Dispose();
            menu = next;
            foreach (var key in obsolete)
            {
                actions[key].Dispose();
                actions.Remove(key);
            }
            view.AccessibilityLabel = content.Label;
            previous = content;
        }

        public override UIContextMenuConfiguration? GetConfigurationForMenu(UIContextMenuInteraction interaction, CGPoint location)
        {
            UIContextMenuConfiguration? configuration = null;
            renderer.Guard(() =>
            {
                if (!stopped && previous is { Disabled: false } && renderer.Active(id))
                    configuration = UIContextMenuConfiguration.Create(null, null, _ =>
                    {
                        UIMenu? shown = null;
                        renderer.Guard(() => shown = stopped ? null : menu);
                        return shown;
                    });
            });
            return configuration;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !stopped)
            {
                stopped = true;
                view.RemoveInteraction(interaction);
                interaction.Dispose();
                menu?.Dispose();
                menu = null;
                foreach (var action in actions.Values)
                    action.Dispose();
                actions.Clear();
            }
            base.Dispose(disposing);
        }
    }
}
