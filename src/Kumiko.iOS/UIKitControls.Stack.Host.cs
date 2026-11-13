using Kumiko.Runtime;
using Kumiko.UI;

namespace Kumiko.iOS;

internal sealed partial class UIKitControls : INavigationStackRenderer
{
    INavigationStackScope INavigationStackRenderer.BeginStack<TMessage>(Ui<TMessage> ui, string stack, long revision,
        Func<BackRequest, TMessage> onBack, NodeKey key) => NativeComponents.NativeStack(ui, stack, revision,
            back => onBack(new(back.Stack, back.Revision, back.From, back.To)), key);
}
