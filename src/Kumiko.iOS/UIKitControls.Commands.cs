using UIKit;

namespace Kumiko.iOS;

internal sealed partial class UIKitControls
{
    Entry CreateCommands(ulong id, NativeCommandKind kind)
    {
        UIView view = kind switch
        {
            NativeCommandKind.Navigation => new UINavigationBar(),
            NativeCommandKind.Toolbar => new UIToolbar(),
            _ => new UIButton(UIButtonType.System) { ShowsMenuAsPrimaryAction = true }
        };
        return new(view, Owner: new CommandsOwner(this, view, id));
    }

    sealed class CommandsOwner(UIKitControls renderer, UIView view, ulong id) : IDisposable
    {
        sealed record CommandEntry(UIAction Action, UIBarButtonItem? Bar);
        readonly Dictionary<string, CommandEntry> commands = new(StringComparer.Ordinal);
        readonly List<UIBarButtonItem> spaces = new();
        readonly UINavigationItem navigation = new();
        UIMenu? menu;
        NativeCommandsContent? previous;
        bool previousEnabled;
        bool stopped;

        internal void Update(NativeCommandsContent content, bool enabled)
        {
            if (previous is { } before && before.Label == content.Label && before.Items.SequenceEqual(content.Items) &&
                previousEnabled == enabled)
                return;
            var obsolete = commands.Keys.Where(key => !content.Items.Any(item => item.Id == key)).ToArray();
            foreach (var item in content.Items)
            {
                if (!commands.TryGetValue(item.Id, out var command))
                {
                    var commandId = item.Id;
                    var action = UIAction.Create(item.Label, null, null, _ => renderer.Guard(() =>
                    {
                        if (!stopped && renderer.current.TryGetValue(id, out var latest) &&
                            latest.Content is NativeCommandsContent snapshot &&
                            snapshot.Items.Any(candidate => candidate.Id == commandId && candidate.Enabled))
                            renderer.QueueValue(id, commandId);
                    }));
                    var bar = content.Kind == NativeCommandKind.Menu ? null : new UIBarButtonItem(action);
                    commands.Add(item.Id, command = new(action, bar));
                }
                using var image = item.Symbol is { } symbol ? UIImage.GetSystemImage(symbol) : null;
                command.Action.Title = item.Label;
                command.Action.Image = image;
                command.Action.Attributes = (enabled && item.Enabled ? (UIMenuElementAttributes)0 : UIMenuElementAttributes.Disabled) |
                    (item.Destructive ? UIMenuElementAttributes.Destructive : (UIMenuElementAttributes)0);
                if (command.Bar is { } barItem)
                {
                    barItem.Title = item.Label;
                    barItem.Image = image;
                    barItem.Enabled = enabled && item.Enabled;
                    barItem.TintColor = item.Destructive ? UIColor.SystemRed : null;
                    barItem.AccessibilityLabel = item.Label;
                    barItem.AccessibilityIdentifier = item.Id;
                }
            }
            if (view is UIButton button)
            {
                using var configuration = UIButtonConfiguration.TintedButtonConfiguration;
                configuration.Title = content.Label;
                button.Configuration = configuration;
                button.AccessibilityLabel = content.Label;
                var nextMenu = UIMenu.Create(content.Items.Select(item => (UIMenuElement)commands[item.Id].Action).ToArray());
                button.Menu = nextMenu;
                menu?.Dispose();
                menu = nextMenu;
            }
            else if (view is UINavigationBar bar)
            {
                navigation.Title = content.Label;
                navigation.LeftBarButtonItems = content.Items.Where(item => item.Leading).Select(item => commands[item.Id].Bar!).ToArray();
                navigation.RightBarButtonItems = content.Items.Where(item => !item.Leading).Select(item => commands[item.Id].Bar!).ToArray();
                bar.SetItems([navigation], false);
            }
            else if (view is UIToolbar toolbar)
            {
                var oldSpaces = spaces.ToArray();
                spaces.Clear();
                var items = new List<UIBarButtonItem>();
                foreach (var item in content.Items)
                {
                    if (items.Count > 0)
                    {
                        var space = new UIBarButtonItem(UIBarButtonSystemItem.FlexibleSpace);
                        spaces.Add(space);
                        items.Add(space);
                    }
                    items.Add(commands[item.Id].Bar!);
                }
                toolbar.SetItems(items.ToArray(), false);
                foreach (var space in oldSpaces)
                    space.Dispose();
                toolbar.AccessibilityLabel = content.Label;
            }
            foreach (var key in obsolete)
            {
                commands[key].Bar?.Dispose();
                commands[key].Action.Dispose();
                commands.Remove(key);
            }
            previous = content;
            previousEnabled = enabled;
        }

        public void Dispose()
        {
            stopped = true;
            if (view is UIButton button)
                button.Menu = null;
            else if (view is UINavigationBar bar)
                bar.SetItems([], false);
            else if (view is UIToolbar toolbar)
                toolbar.SetItems([], false);
            menu?.Dispose();
            navigation.Dispose();
            foreach (var space in spaces)
                space.Dispose();
            foreach (var command in commands.Values)
            {
                command.Bar?.Dispose();
                command.Action.Dispose();
            }
            commands.Clear();
        }
    }
}
