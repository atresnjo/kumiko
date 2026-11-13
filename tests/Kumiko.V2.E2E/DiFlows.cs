using System.Collections.Concurrent;
using Kumiko.Runtime;
using Kumiko.UI;
using Microsoft.Extensions.DependencyInjection;

static class DiFlows
{
    public static void Run(string output, Action<bool, string> check)
    {
        var trace = new DiTrace();
        var collection = new ServiceCollection();
        collection.AddSingleton(trace);
        collection.AddScoped<DiResource>();
        collection.AddScoped<IStore, DiStore>();
        collection.AddScoped<IOpaqueStore, OpaqueStore>();
        using var provider = collection.BuildServiceProvider(new ServiceProviderOptions() { ValidateScopes = true, ValidateOnBuild = true });
        using (var empty = new ServiceCollection().BuildServiceProvider())
        {
            try
            {
                using var missing = StorageApp.CreateProgram(empty);
                throw new Exception("Missing DI service accepted");
            }
            catch (InvalidOperationException error)
            {
                check(error.Message.Contains(nameof(IStore), StringComparison.Ordinal), "DI startup reports missing registration without resolving services");
            }
        }
        using var program = StorageApp.CreateProgram(provider);
        check(trace.Events.IsEmpty, "DI factory validation does not construct scoped dependencies");
        using var tester = new Tester(program, new(360, 200));
        tester.Frame();
        program.Send(new StorageMsg.Opaque());
        tester.Frame();
        if (!SpinWait.SpinUntil(() => program.PendingCommands == 0, TimeSpan.FromSeconds(10)))
            throw new TimeoutException();
        tester.Settle();
        check(tester.HasText("Saved: opaque"), "generic helper resolves a DI dependency absent from generated discovery");
        program.Send(new StorageMsg.Edit("cancelled"));
        tester.Frame();
        Await(trace.Started.Task);
        program.Send(new StorageMsg.Watch(false));
        tester.Frame();
        check(!trace.Events.Any(e => e == "disposed:1"), "subscription scope survives asynchronous handle cleanup");
        trace.StopWatch.SetResult();
        program.Send(new StorageMsg.Edit("latest"));
        tester.Frame();
        Await(trace.Latest.Task);
        Await(trace.Cleaning.Task);
        check(!trace.Events.Contains("disposed:2"), "debounce replacement retains the old scope through cancellation cleanup");
        program.Dispose();
        var stopped = program.DisposeAsync().AsTask();
        Await(trace.Cleaning.Task);
        check(!stopped.IsCompleted && !trace.Events.Any(e => e == "disposed:2"), "shutdown waits for command cancellation cleanup before disposing its scope");
        trace.FinishCommand.SetResult();
        Await(stopped);
        var events = trace.Events.ToArray();
        check(Array.IndexOf(events, "watch-stopped") < Array.IndexOf(events, "disposed:1")
            && Array.IndexOf(events, "command-cleaned") < Array.IndexOf(events, "disposed:2")
            && events.Count(e => e.StartsWith("disposed:", StringComparison.Ordinal)) == 3,
            "independent command and subscription scopes dispose once after actual work");
        var session = Path.Combine(output, "services-di-session.jsonl");
        // Save on a live program, then replay into DI with no scope construction.
        using (var source = StorageApp.CreateProgram(provider))
            source.Save(session);
        var before = trace.Events.Count;
        using (var replay = StorageApp.CreateProgram(provider))
        {
            replay.Replay(session);
            using var replayTester = new Tester(replay, new(360, 200));
            replayTester.Settle();
            replayTester.Screenshot(Path.Combine(output, "services-di-replay.png"));
        }
        check(trace.Events.Count == before, "DI replay creates no service scopes or effects");
        trace.ThrowWatch = true;
        using (var failing = StorageApp.CreateProgram(provider))
        {
            try
            {
                using var failureTester = new Tester(failing, new(360, 200));
                failureTester.Frame();
                throw new Exception("Subscription startup failure accepted");
            }
            catch (InvalidOperationException error) when (error.Message == "watch-start-failed")
            {
                Await(failing.DisposeAsync().AsTask());
            }
        }
        check(trace.Events.Last().StartsWith("disposed:", StringComparison.Ordinal), "failed subscription startup releases its DI scope");
        File.WriteAllLines(Path.Combine(output, "services-di.txt"), trace.Events);
    }

    static void Await(Task task) => task.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
}

sealed class DiTrace
{
    public ConcurrentQueue<string> Events { get; } = new();
    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Latest { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Cleaning { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource FinishCommand { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource StopWatch { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public int Created;
    public bool ThrowWatch;
}

sealed class DiResource(DiTrace trace) : IAsyncDisposable
{
    readonly int id = Interlocked.Increment(ref trace.Created);
    public async ValueTask DisposeAsync()
    {
        await Task.Yield();
        trace.Events.Enqueue("disposed:" + id);
    }
}

sealed class DiStore(DiTrace trace, DiResource resource) : IStore
{
    public async Task<string> Save(string text, CancellationToken token)
    {
        if (text == "latest")
        {
            trace.Events.Enqueue("latest");
            trace.Latest.SetResult();
            return text;
        }
        trace.Events.Enqueue("command-started");
        trace.Started.SetResult();
        try
        {
            await Task.Delay(Timeout.Infinite, token);
            return text;
        }
        finally
        {
            trace.Cleaning.SetResult();
            await trace.FinishCommand.Task;
            GC.KeepAlive(resource);
            trace.Events.Enqueue("command-cleaned");
        }
    }
    public Task<string> Load(CancellationToken token) => Task.FromResult("DI");
    public IDisposable Watch(Action<string> receive)
    {
        if (trace.ThrowWatch)
            throw new InvalidOperationException("watch-start-failed");
        trace.Events.Enqueue("watch-started");
        return new Handle(trace);
    }
    sealed class Handle(DiTrace trace) : IDisposable, IAsyncDisposable
    {
        public void Dispose() => trace.Events.Enqueue("watch-stop-requested");
        public async ValueTask DisposeAsync()
        {
            Dispose();
            await trace.StopWatch.Task;
            trace.Events.Enqueue("watch-stopped");
        }
    }
}

// The generator intentionally cannot infer T inside this helper. DI still
// resolves it at execution, including constructor dependencies.
static class DiEffects
{
    public static Cmd<StorageMsg> Load<T>() where T : class, IOpaqueStore => Cmd<StorageMsg>.Run<T>("opaque",
        async (store, token) => new StorageMsg.Saved(await store.Load(token)), static error => new StorageMsg.Saved(error.Message));
}
interface IOpaqueStore
{
    Task<string> Load(CancellationToken token);
}
sealed class OpaqueStore(DiTrace trace) : IOpaqueStore
{
    public Task<string> Load(CancellationToken token)
    {
        trace.Events.Enqueue("opaque");
        return Task.FromResult("opaque");
    }
}
