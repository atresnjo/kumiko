using Kumiko.Documents;

static class ConcurrentFlows
{
    internal static async Task Run(string root, Action<bool, string> check)
    {
        var path = Path.Combine(root, "writers.txt");
        await File.WriteAllTextAsync(path, "original");
        using var one = new FileDocumentStore();
        using var two = new FileDocumentStore();
        var codec = new Utf8DocumentCodec();
        var opened = Document<string>.Open("writers", await one.ReadAsync(path, codec, default));
        var first = opened.Edit("first").BeginSave().Request;
        var second = opened.Edit("second").BeginSave().Request;
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<bool> Write(IDocumentStore store, DocumentRequest<string> request)
        {
            await start.Task;
            try
            {
                await store.SaveAsync(request, codec, default);
                return true;
            }
            catch (DocumentConflictException)
            {
                return false;
            }
        }
        var writes = new[] { Task.Run(() => Write(one, first)), Task.Run(() => Write(two, second)) };
        start.TrySetResult();
        var results = await Task.WhenAll(writes).WaitAsync(TimeSpan.FromSeconds(5));
        string content = await File.ReadAllTextAsync(path);
        check(results.Count(r => r) == 1 && content == (results[0] ? "first" : "second"),
            "simultaneously released writers across store owners commit exactly one complete revision");
        var current = Document<string>.Open("external", await one.ReadAsync(path, codec, default));
        using var held = new HeldCodec();
        var saving = Task.Run(() => one.SaveAsync(current.Edit("local draft").BeginSave().Request, held, default));
        check(held.Started.Wait(TimeSpan.FromSeconds(5)), "filesystem save is held while encoding the captured revision");
        await File.WriteAllTextAsync(path, "external edit during save");
        held.Release.Set();
        try
        {
            await saving.WaitAsync(TimeSpan.FromSeconds(5));
            check(false, "racing external edit conflicts");
        }
        catch (DocumentConflictException)
        {
            check(await File.ReadAllTextAsync(path) == "external edit during save",
                "external edit made during asynchronous save survives with a conflict result");
        }
    }
    sealed class HeldCodec : IDocumentCodec<string>, IDisposable
    {
        internal readonly ManualResetEventSlim Started = new();
        internal readonly ManualResetEventSlim Release = new();
        public ReadOnlyMemory<byte> Encode(string value)
        {
            Started.Set();
            if (!Release.Wait(TimeSpan.FromSeconds(5)))
                throw new TimeoutException("Held codec was never released.");
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
