using Flint.Services;
using Kumiko.Runtime;

namespace Flint;

public sealed partial class FlintApp
{
    public static IEnumerable<Sub<Msg>> Subscriptions(FlintModel model)
    {
        // Each toast owns a one-shot timer; nothing ticks while the app is idle.
        foreach (var toast in model.Toasts)
        {
            var id = toast.Id;
            var delay = toast.Kind == ToastKind.Error ? 7000 : 2600;
            yield return new Sub<Msg>("toast:" + id, send => new Timer(_ => send(new Msg.DismissToast(id)), null, delay, Timeout.Infinite));
        }
        // A quick Control+Tab commits before this fires, so the switcher overlay never flashes.
        if (model.Switcher is { Visible: false } switcher)
        {
            var id = switcher.Id;
            yield return new Sub<Msg>("switcher:" + id, send => new Timer(_ => send(new Msg.ShowSwitcher(id)), null, 160, Timeout.Infinite));
        }
        if (!model.Watching)
            yield break;
        // Like the original, only the active workspace is watched; switching workspaces revalidates its repositories.
        foreach (var path in model.Workspace.Repositories)
        {
            var root = path;
            yield return Sub<Msg>.From<IRepositoryWatcher>("repo:" + root,
                (services, send) => services.Watch(root, metadata => send(new Msg.Refresh(root, StatusOnly: !metadata))));
        }
    }
}
