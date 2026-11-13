namespace Kumiko.Runtime;

public sealed partial class Program<TModel, TMessage>
{
    bool retainHistory = true;

    /// <summary>
    /// Retains past models for time travel and session export. Defaults to true.
    /// Set false for long-running applications that only need the current model.
    /// Recording, replay and enabling the debugger turn retention back on.
    /// </summary>
    public bool RetainHistory
    {
        get => retainHistory;
        set
        {
            CheckThread();
            if (!value && (recorder is not null || debugEnabled))
                throw new InvalidOperationException("Stop recording and disable the debugger before discarding history.");
            retainHistory = value;
            if (!value)
                RetireHistory();
        }
    }

    void RetireHistory()
    {
        var current = history[Position];
        history.Clear();
        history.Add(new(0, -1, current.Frame, current.Time, null, default, current.Model, []));
        Position = 0;
        ResetDebugHistory();
        detailText = "";
    }
}
