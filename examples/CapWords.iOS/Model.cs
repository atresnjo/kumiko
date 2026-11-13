using System.Collections.Immutable;
using Kumiko.Runtime;

internal sealed record CapWordsModel(string? SelectedId = null, string? Notice = null,
    ImmutableArray<Word> Words = default, string? Error = null)
{
    /// <summary>The study's one-screen history when it runs on its own; the gallery keeps its own.</summary>
    public NavigationHistory<string> Navigation { get; init; } = NavigationHistory<string>.Start("study");
}
internal sealed record Word(string Id, string Name, string Phonetic, ImmutableArray<byte> Image);
