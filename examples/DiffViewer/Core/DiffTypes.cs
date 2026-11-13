using System.Collections.Immutable;

namespace DiffViewer.Core;

public enum LineKind : byte
{
    Context,
    Added,
    Removed
}

/// <summary>One line of a hunk. Old/New are zero-based indices into the old/new line arrays, or -1.</summary>
public readonly record struct DiffLine(LineKind Kind, int Old, int New);

/// <summary>Zero-based starts; counts follow unified diff conventions (a zero count starts after the line before).</summary>
public sealed record Hunk(int OldStart, int OldCount, int NewStart, int NewCount, ImmutableArray<DiffLine> Lines);

/// <summary>A changed range of UTF-16 units within one line.</summary>
public readonly record struct Emphasis(int Start, int Length);
