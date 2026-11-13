using Kumiko.Runtime;

internal sealed record NativeButtonModel(int Count = 0, bool Enabled = true, bool Visible = true)
{
    /// <summary>The single counter screen's history; it has no other destination.</summary>
    public NavigationHistory<string> Navigation { get; init; } = NavigationHistory<string>.Start("counter");
}
