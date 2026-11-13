namespace Kumiko.Runtime;

/// <summary>Flows through asynchronous work so effects cannot inspect live UI state.</summary>
internal static class KumikoExecution
{
    static readonly AsyncLocal<string?> phase = new();

    internal static IDisposable Enter(string name)
    {
        var previous = phase.Value;
        phase.Value = name;
        return new Scope(previous);
    }

    internal static void CheckUI()
    {
        if (phase.Value is { } current)
            throw new KumikoViolationException($"Kumiko {current} cannot read or change UI state; return a message instead.");
    }

    sealed class Scope(string? previous) : IDisposable
    {
        public void Dispose() => phase.Value = previous;
    }
}

internal sealed class KumikoViolationException(string message) : InvalidOperationException(message);
