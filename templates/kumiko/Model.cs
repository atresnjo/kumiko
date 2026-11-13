using Kumiko.Runtime;

namespace KumikoTemplate;

internal sealed record Model(int Count = 0, string Name = "", bool Updates = true)
{
    public NavigationHistory<string> Navigation { get; init; } = NavigationHistory<string>.Start("home");
}
