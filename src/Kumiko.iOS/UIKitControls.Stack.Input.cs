using Foundation;
using Kumiko.UI;
using UIKit;

namespace Kumiko.iOS;

internal sealed partial class UIKitControls
{
    readonly HashSet<ulong> navigationCommits = new();

    /// <summary>Queues native text unless it is already the newest pending value for that control.</summary>
    void QueueLatestText(ulong id, string text)
    {
        if (!values.TryGetValue(id, out var queue) || queue.Count == 0 || !Equals(queue.Last(), text))
            QueueValue(id, text);
    }

    void CommitCoveredInput(IReadOnlyList<NativeControlNode> next)
    {
        var covered = next.Where(n => n.Content is NativeNavigationPageContent && n.Inert &&
            current.TryGetValue(n.Id, out var old) && !old.Inert).Select(n => n.Id).ToArray();
        if (covered.Length == 0)
            return;
        foreach (var pair in entries.ToArray())
        {
            if (!Active(pair.Key) || !covered.Any(page => DescendsFrom(pair.Key, page)) ||
                !current.TryGetValue(pair.Key, out var node))
                continue;
            if (pair.Value.View is UITextView editor && node.Content is NativeEditorContent text)
            {
                editor.UnmarkText();
                if ((editor.Text ?? "") != text.Value)
                {
                    navigationCommits.Add(pair.Key);
                    QueueLatestText(pair.Key, editor.Text ?? "");
                    RequestFrame();
                }
            }
            if (pair.Value.View is UITextField field && node.Content is NativeTextFieldContent input)
            {
                field.UnmarkText();
                if ((field.Text ?? "") != input.Value)
                {
                    texts[pair.Key] = field.Text ?? "";
                    RequestFrame();
                }
            }
        }
    }

    void RememberNavigationOpener(ulong control, string? row = null)
    {
        foreach (var page in current.Values.Where(n => n.Content is NativeNavigationPageContent && DescendsFrom(control, n.Id)))
            if (entries.TryGetValue(page.Parent, out var stackEntry) && stackEntry.Owner is StackOwner stack)
                stack.RememberOpener(((NativeNavigationPageContent)page.Content).Id, control, row);
    }

    sealed partial class StackOwner
    {
        internal void RememberOpener(int page, ulong control, string? row)
        {
            if (pages.TryGetValue(page, out var owner))
            {
                owner.Opener = control;
                owner.OpenerRow = row;
            }
        }
        NSObject? FocusTarget(UIViewController controller)
        {
            var owner = pages.Values.FirstOrDefault(page => ReferenceEquals(page.Controller, controller));
            if (owner is null || !renderer.entries.TryGetValue(owner.Opener, out var entry))
                return controller.View;
            if (entry.View is UITableView table && owner.OpenerRow is { } row &&
                renderer.current.TryGetValue(owner.Opener, out var node) && node.Content is NativeListContent list)
            {
                var index = Array.FindIndex(list.Items.ToArray(), item => item.Id == row);
                if (index >= 0)
                {
                    using var path = NSIndexPath.FromRowSection(index, 0);
                    return table.CellAt(path) ?? (NSObject)table;
                }
            }
            return entry.View;
        }
    }
}
