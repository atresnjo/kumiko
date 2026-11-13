using Foundation;
using UIKit;

namespace Kumiko.iOS;

internal sealed partial class UIKitControls
{
    sealed class ListSource(UIKitControls owner, ulong id) : UITableViewSource
    {
        NativeListContent? content;

        internal void Update(UITableView table, NativeListContent next)
        {
            if (content == next)
                return;
            var offset = table.ContentOffset;
            content = next;
            if (next.Palette is { } palette)
            {
                using var background = NativePaint.Color(palette.Background);
                using var accent = NativePaint.Color(palette.Accent);
                table.BackgroundColor = background;
                table.TintColor = accent;
            }
            table.ReloadData();
            table.LayoutIfNeeded();
            // Selection and label updates must not reset a user's position.
            var max = Math.Max(0, table.ContentSize.Height - table.Bounds.Height);
            table.SetContentOffset(new(offset.X, Math.Clamp(offset.Y, 0, max)), false);
        }

        public override nint RowsInSection(UITableView tableView, nint section) => content?.Items.Length ?? 0;

        public override UITableViewCell GetCell(UITableView tableView, NSIndexPath indexPath)
        {
            UITableViewCell? cell = null;
            owner.Guard(() => cell = BuildCell(tableView, indexPath));
            return cell ?? new UITableViewCell();
        }

        UITableViewCell BuildCell(UITableView tableView, NSIndexPath indexPath)
        {
            var item = content!.Items[indexPath.Row];
            var cell = tableView.DequeueReusableCell("row") ?? new UITableViewCell(UITableViewCellStyle.Subtitle, "row");
            using var configuration = UIListContentConfiguration.SubtitleCellConfiguration;
            configuration.Text = item.Title;
            configuration.SecondaryText = item.Detail;
            configuration.TextProperties.Color = item.Enabled ? UIColor.Label : UIColor.TertiaryLabel;
            if (content.Palette is { } palette)
            {
                using var foreground = NativePaint.Color(item.Enabled ? palette.Text : palette.Text.Alpha(.4f));
                using var secondary = NativePaint.Color(palette.Muted);
                using var background = NativePaint.Color(palette.Surface);
                using var accent = NativePaint.Color(palette.Accent);
                configuration.TextProperties.Color = foreground;
                configuration.SecondaryTextProperties.Color = secondary;
                cell.BackgroundColor = background;
                cell.TintColor = accent;
            }
            var selected = content.Checks?.Contains(item.Id) ?? content.SelectedId == item.Id;
            cell.ContentConfiguration = configuration;
            cell.Accessory = selected ? UITableViewCellAccessory.Checkmark : UITableViewCellAccessory.None;
            cell.SelectionStyle = item.Enabled ? UITableViewCellSelectionStyle.Default : UITableViewCellSelectionStyle.None;
            cell.AccessibilityIdentifier = item.Id;
            cell.AccessibilityLabel = item.Detail is null ? item.Title : $"{item.Title}, {item.Detail}";
            cell.AccessibilityTraits = UIAccessibilityTrait.Button |
                (selected ? UIAccessibilityTrait.Selected : 0) |
                (item.Enabled ? 0 : UIAccessibilityTrait.NotEnabled);
            return cell;
        }

        public override NSIndexPath? WillSelectRow(UITableView tableView, NSIndexPath indexPath) =>
            content is { } list && indexPath.Row >= 0 && indexPath.Row < list.Items.Length &&
            list.Items[indexPath.Row].Enabled ? indexPath : null;

        public override void RowSelected(UITableView tableView, NSIndexPath indexPath)
        {
            // Guard native callbacks, including stale delegate calls after removal.
            owner.Guard(() =>
            {
                if (content is { } list && indexPath.Row >= 0 && indexPath.Row < list.Items.Length)
                    owner.Select(id, list.Items[indexPath.Row].Id);
            });
        }
    }
}
