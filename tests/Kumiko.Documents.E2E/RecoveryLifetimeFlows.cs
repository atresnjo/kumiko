using Kumiko.Documents;

static class RecoveryLifetimeFlows
{
    internal static async Task Run(string root, Action<bool, string> check)
    {
        string directory = Path.Combine(root, "recovery-lifetime");
        using var old = new DocumentRecovery(directory, "session");
        using var codec = new HeldCodec();
        var writing = Task.Run(() => old.WriteAsync(Document<string>.Create("note", "Note", "old draft"), codec, default));
        check(codec.Started.Wait(TimeSpan.FromSeconds(5)), "recovery write reaches held codec before disposal");
        old.Dispose();
        bool leased = false;
        try
        {
            using var premature = new DocumentRecovery(directory, "session");
        }
        catch (IOException)
        {
            leased = true;
        }
        var shutdown = old.DisposeAsync().AsTask();
        check(!shutdown.IsCompleted, "asynchronous recovery shutdown waits without releasing a held operation");
        codec.Release.Set();
        bool cancelled = false;
        try
        {
            await writing.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }
        check(leased && cancelled, "disposed recovery retains exclusive lease until held write can no longer commit");
        await shutdown.WaitAsync(TimeSpan.FromSeconds(5));
        using var next = new DocumentRecovery(directory, "session");
        await next.WriteAsync(Document<string>.Create("note", "Note", "new draft"), new Utf8DocumentCodec(), default);
        check((await next.ReadAsync("note", new Utf8DocumentCodec(), default))?.Value == "new draft",
            "replacement recovery owner never observes an old owner's late write");
        using var held = new HeldCodec();
        var pending = Task.Run(() => next.WriteAsync(Document<string>.Create("note", "Note", "resurrected"), held, default));
        check(held.Started.Wait(TimeSpan.FromSeconds(5)), "recovery write reaches held codec before remove");
        next.Remove("note");
        held.Release.Set();
        cancelled = false;
        try
        {
            await pending.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }
        check(cancelled && await next.ReadAsync("note", new Utf8DocumentCodec(), default) is null,
            "remove prevents held recovery writes from resurrecting discarded content");
    }
    sealed class HeldCodec : IDocumentCodec<string>, IDisposable
    {
        internal readonly ManualResetEventSlim Started = new();
        internal readonly ManualResetEventSlim Release = new();
        public ReadOnlyMemory<byte> Encode(string value)
        {
            Started.Set();
            if (!Release.Wait(TimeSpan.FromSeconds(5)))
                throw new TimeoutException("Held recovery codec was not released.");
            return new Utf8DocumentCodec().Encode(value);
        }
        public string Decode(ReadOnlyMemory<byte> bytes) => new Utf8DocumentCodec().Decode(bytes);
        public void Dispose()
        {
            Started.Dispose();
            Release.Dispose();
        }
    }
}
