namespace Kumiko.Runtime;

public sealed partial class Program<TModel, TMessage>
{
    Action? hostWake;

    // Hosts schedule, rather than execute, a frame here: this callback may run on a worker.
    IDisposable IProgram.ConnectHost(Action wake)
    {
        CheckThread();
        ArgumentNullException.ThrowIfNull(wake);
        if (window is not null || hostWake is not null || nativeApp is not null)
            throw new InvalidOperationException("An Kumiko program can be attached to only one host.");
        if (desiredWindows.Length > 1)
            throw new InvalidOperationException("This host has one surface. Multi-window models require a desktop window host.");
        Volatile.Write(ref hostWake, wake);
        return new HostConnection(this, wake);
    }

    sealed class HostConnection(Program<TModel, TMessage> owner, Action wake) : IDisposable
    {
        int released;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref released, 1) == 0)
                Interlocked.CompareExchange(ref owner.hostWake, null, wake);
        }
    }
}
