using System.Collections.Concurrent;
using System.Text.Json.Serialization;
using Kumiko.Runtime;
using Kumiko.UI;

/// <summary>Debounced, keyed commands: a newer command with the same key cancels the pending or running one.</summary>
static class DebounceChecks
{
    public static void Run(Action<bool, string> check)
    {
        var started = new ConcurrentQueue<string>();
        var cancelled = new ConcurrentQueue<string>();
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        (DebounceModel, Cmd<DebounceMessage>) Update(DebounceModel m, DebounceMessage msg) => msg switch
        {
            DebounceMessage.Search s => (m with
            {
                Query = s.Query
            }, Cmd<DebounceMessage>.Debounce(s.Key, s.Delay, async token =>
            {
                started.Enqueue(s.Query);
                try
                {
                    if (s.Hold)
                        await gate.Task.WaitAsync(token);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    // Observe cancellation in the work itself. Callback disposal while
                    // WaitAsync unwinds can suppress a separate token registration in AOT.
                    cancelled.Enqueue(s.Query);
                    throw;
                }
                return new DebounceMessage.Found(s.Query);
            }, error => new DebounceMessage.Found("error: " + error.Message))),
            DebounceMessage.Found f => (m with { Answers = [.. m.Answers, f.Query] }, Cmd<DebounceMessage>.None),
            _ => throw new ArgumentOutOfRangeException(nameof(msg))
        };
        using var program = new Program<DebounceModel, DebounceMessage>(new("", []), Update, (c, m, send) =>
        {
            using var root = c.Div(key: "root").SizeFull();
            c.Text(m.Query, key: "query");
        }, DebounceJson.Default.DebounceModel, DebounceJson.Default.DebounceMessage);
        using var tester = new Tester(program, new(Width: 200, Height: 100));
        void Idle()
        {
            tester.Frame();
            if (!SpinWait.SpinUntil(() => program.PendingCommands == 0, TimeSpan.FromSeconds(5)))
                throw new TimeoutException("Debounced commands did not finish.");
            tester.Settle();
        }

        var pause = TimeSpan.FromMilliseconds(150);
        foreach (var query in new[] { "g", "gl", "gla" })
        {
            program.Send(new DebounceMessage.Search("search", query, pause));
            tester.Frame();
        }
        Idle();
        check(started.SequenceEqual(["gla"]) && program.Model.Answers.SequenceEqual(["gla"]),
            "a burst of debounced commands runs only the last one after the pause");

        started.Clear();
        program.Send(new DebounceMessage.Search("search", "slow", TimeSpan.Zero, Hold: true));
        tester.Frame();
        SpinWait.SpinUntil(() => !started.IsEmpty, TimeSpan.FromSeconds(5));
        program.Send(new DebounceMessage.Search("search", "fast", TimeSpan.Zero));
        Idle();
        gate.SetResult(true);
        tester.Settle();
        check(cancelled.SequenceEqual(["slow"]) && program.Model.Answers.SequenceEqual(["gla", "fast"]),
            "a newer command with the same key cancels the running one, which never answers");

        started.Clear();
        program.Send(new DebounceMessage.Search("owners", "maya", pause));
        program.Send(new DebounceMessage.Search("search", "atlas", pause));
        Idle();
        check(started.Order().SequenceEqual(["atlas", "maya"]) && program.Model.Answers.Length == 4,
            "commands with different keys do not cancel each other");
        check(program.History.Where(step => step.Message is DebounceMessage.Search).All(step => step.Commands.SequenceEqual(["search"]) || step.Commands.SequenceEqual(["owners"])),
            "debounced commands are recorded by key");
    }
}

internal sealed record DebounceModel(string Query, System.Collections.Immutable.ImmutableArray<string> Answers);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(DebounceMessage.Search), "search")]
[JsonDerivedType(typeof(DebounceMessage.Found), "found")]
internal abstract record DebounceMessage
{
    public sealed record Search(string Key, string Query, TimeSpan Delay, bool Hold = false) : DebounceMessage;
    public sealed record Found(string Query) : DebounceMessage;
}

[JsonSerializable(typeof(DebounceModel))]
[JsonSerializable(typeof(DebounceMessage))]
internal partial class DebounceJson : JsonSerializerContext;
