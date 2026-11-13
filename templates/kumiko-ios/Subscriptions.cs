using Kumiko.iOS;
using Kumiko.Runtime;

namespace KumikoiOSTemplate;

internal sealed partial class KumikoiOSTemplateApp
{
    public static IEnumerable<Sub<Msg>> Subscriptions(Model model) =>
        [NativeLifecycle.Phases<Msg>(static phase => new Msg.PhaseChanged(phase))];
}
