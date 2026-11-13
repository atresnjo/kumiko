using System.Collections.Immutable;
using DiskMap.Core;
using Kumiko.Runtime;

namespace DiskMap;

internal sealed partial class DiskMapApp
{
    /// <summary>
    /// A scan runs while the model names a folder to scan, keyed by its generation: a newer scan replaces the key,
    /// which cancels the older one. Nothing runs, and nothing wakes the window, once it finishes.
    /// </summary>
    public static IEnumerable<Sub<Msg>> Subscriptions(Model model)
    {
        if (model.Scanning is not { } names)
            yield break;
        var generation = model.Generation;
        var path = Path.Combine(model.Root, Path.Combine([.. names]));
        yield return new Sub<Msg>($"scan:{generation}", send => new ScanJob(path, generation, names, send));
    }
}

/// <summary>Runs a <see cref="Scanner"/> and reports its progress about ten times a second until it finishes.</summary>
internal sealed class ScanJob : IDisposable
{
    readonly Scanner scanner;
    readonly Timer timer;
    readonly object gate = new();
    readonly long generation;
    readonly Dispatch<Msg> send;
    bool stopped;

    public ScanJob(string path, long generation, ImmutableArray<string> names, Dispatch<Msg> send)
    {
        this.generation = generation;
        this.send = send;
        scanner = new Scanner(path);
        timer = new Timer(_ => Report(), null, 100, 100);
        scanner.Result.ContinueWith(result =>
        {
            Msg message = result.IsCompletedSuccessfully ? new Msg.Scanned(generation, names, result.Result, scanner.Progress.Elapsed)
                : new Msg.ScanFailed(generation, result.Exception?.InnerException?.Message ?? "The scan stopped.");
            Finish(result.IsCanceled ? null : message);
        }, TaskScheduler.Default);
        scanner.Start();
    }

    void Report()
    {
        lock (gate)
            if (!stopped)
                send(new Msg.Progress(generation, scanner.Progress));
    }

    void Finish(Msg? message)
    {
        lock (gate)
        {
            if (stopped)
                return;
            stopped = true;
            timer.Dispose();
            if (message is not null)
                send(message);
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (!stopped)
                timer.Dispose();
            stopped = true;
        }
        scanner.Dispose();
    }
}
