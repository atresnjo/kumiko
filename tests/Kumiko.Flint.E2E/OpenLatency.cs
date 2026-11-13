using Flint.Services;

/// <summary>
/// Opening a repository Flint has not loaded before shows its tab only after every Git command it needs. Each command
/// that waits for another adds a full Git round trip, so a first open must start them all at once. Results, empty
/// repositories and reloads of an unchanged repository behave as before.
/// </summary>
static class OpenLatency
{
    public static void Run(Repositories fixture, IProcessRunner runner, Action<bool, string> check)
    {
        var state = Path.Combine(fixture.Root, "open-latency");
        var slow = new SlowRunner(runner, TimeSpan.FromMilliseconds(150));
        var git = new GitService(slow, state);
        var first = git.Load(Path.Combine(fixture.Repo, "src"), 4000, default).GetAwaiter().GetResult();
        check(slow.Calls.Count > 0 && slow.Calls.All(c => c.FinishedBefore == 0),
            $"A first open starts every Git command without waiting for another ({Describe(slow)})");
        var reference = new GitService(runner, Path.Combine(state, "reference"));
        var sequential = reference.Load(fixture.Repo, 4000, default).GetAwaiter().GetResult();
        check(first.Path == sequential.Path && first.Head == sequential.Head && History(first).SequenceEqual(History(sequential))
            && first.Refs.SequenceEqual(sequential.Refs) && first.Staged.SequenceEqual(sequential.Staged) && first.Unstaged.SequenceEqual(sequential.Unstaged)
            && first.HasMore == sequential.HasMore, "A concurrent first open loads the same snapshot as a sequential one");

        slow.Reset();
        git.Load(fixture.Repo, 4000, default).GetAwaiter().GetResult();
        check(!slow.Calls.Any(c => c.Command == "log"), "Reloading an unchanged repository reuses its history instead of running git log");

        // Selecting a commit, including the one an open selects, needs its header, files and patch.
        foreach (var commit in new[] { first.Commits[0], first.Commits[^1], first.Commits.First(c => c.Parents.Length > 1) })
        {
            slow.Reset();
            var (detail, patch) = git.Commit(fixture.Repo, commit, default).GetAwaiter().GetResult();
            check(slow.Calls.Count == 3 && slow.Calls.All(c => c.FinishedBefore == 0),
                $"A known commit's header, files and patch load at once ({Describe(slow)})");
            var expected = reference.Detail(fixture.Repo, commit.Sha, default).GetAwaiter().GetResult();
            var expectedPatch = reference.Patch(fixture.Repo, expected, null, false, default).GetAwaiter().GetResult();
            check(detail.Sha == expected.Sha && detail.Parents.SequenceEqual(expected.Parents) && detail.Subject == expected.Subject
                && detail.Body == expected.Body && detail.Files.SequenceEqual(expected.Files) && patch == expectedPatch,
                $"Loading a commit at once matches loading it in sequence ({commit.Subject})");
        }

        slow.Reset();
        var empty = git.Load(fixture.Empty, 4000, default).GetAwaiter().GetResult();
        check(empty.Head.Unborn && empty.Commits.IsEmpty, "A repository without commits still opens");
    }

    // Commit records hold arrays, which records compare by reference; compare their contents instead.
    static IEnumerable<string> History(Flint.Git.Snapshot snapshot) =>
        snapshot.Commits.Select(c => $"{c.Sha} {string.Join(',', c.Parents)} {c.Author} {c.Time} {c.Subject}");

    static string Describe(SlowRunner slow) => string.Join(", ", slow.Calls.Select(c => $"{c.Command} after {c.FinishedBefore}"));

    sealed class SlowRunner(IProcessRunner inner, TimeSpan delay) : IProcessRunner
    {
        int finished;
        public List<(string Command, int FinishedBefore)> Calls { get; } = [];
        public void Reset()
        {
            Calls.Clear();
            Volatile.Write(ref finished, 0);
        }
        public async Task<string> Run(string executable, string directory, IEnumerable<string> arguments, CancellationToken token,
            IReadOnlyDictionary<string, string>? environment = null, bool allowDifference = false)
        {
            var list = arguments.ToList();
            lock (Calls)
                Calls.Add((list[0], Volatile.Read(ref finished)));
            try
            {
                await Task.Delay(delay, token).ConfigureAwait(false);
                return await inner.Run(executable, directory, list, token, environment, allowDifference).ConfigureAwait(false);
            }
            finally
            {
                Interlocked.Increment(ref finished);
            }
        }
    }
}
