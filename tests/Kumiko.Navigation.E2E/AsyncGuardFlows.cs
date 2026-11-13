using System.Collections.Concurrent;
using System.Text.Json;
using Kumiko.Runtime;
using Kumiko.UI;

static class AsyncGuardFlows
{
    internal static void Run(string output, Action<bool, string> check)
    {
        var permissions = new Permissions();
        using var program = CheckedApp.CreateProgram(new KumikoServices().Add<IRoutePermissions>(permissions));
        using var tester = new Tester(program, new(520, 440, DevMode: true));
        void Wait(Func<bool> condition)
        {
            var until = DateTime.UtcNow.AddSeconds(10);
            while (!condition() && DateTime.UtcNow < until)
            {
                tester.Frame();
                Thread.Sleep(5);
            }
            check(condition(), "async permission result reaches the model through the runtime queue");
        }
        void Ready(long request, int step) => Wait(() => permissions.Contains(request, step));
        void Answer(long request, int step, RouteAdmission admission = RouteAdmission.Allow, string? redirect = null)
        {
            Ready(request, step);
            permissions.Answer(new("main", request, step, admission, redirect));
            Wait(() => program.Model.Guard.Pending is not { } pending || pending.Request != request || pending.Step != step);
        }
        void Go(string label) => tester.Get(Role.Button, label).Click();
        void Shot(string name)
        {
            tester.Screenshot(Path.Combine(output, name + ".png"));
            File.WriteAllText(Path.Combine(output, name + ".txt"), tester.Dump());
            tester.WriteWarnings(Path.Combine(output, name + ".warnings.json"));
            check(tester.Context.Warnings.Count == 0, "async guard UI has no development warnings");
        }

        Go("Open one");
        var first = program.Model.Guard.Sequence;
        Ready(first, 0);
        check(program.Model.Path.Top.Route == "home" && !permissions.Contains(first, 1), "async ancestor admission holds the current page and checks children in order");
        var snapshot = Path.Combine(output, "async-pending.json");
        program.SaveModel(snapshot);
        Shot("async-pending");
        Answer(first, 0);
        Ready(first, 1);
        program.Send(new CheckedMsg.Reply(new("main", first, 0, RouteAdmission.Allow)));
        tester.Settle();
        check(program.Model.Path.Top.Route == "home" && program.Model.Guard.Pending?.Step == 1, "a duplicated earlier-step result cannot admit the child");
        Answer(first, 1);
        check(program.Model.Path.Top.Route == "one" && program.Model.Guard.Pending is null, "only final async admission commits the requested path");
        Shot("async-allowed");
        program.Send(new CheckedMsg.Permission(false));
        tester.Settle();
        var installed = Path.Combine(output, "async-install-without-permission.json");
        program.SaveModel(installed);
        program.Send(new CheckedMsg.Permission(true));

        Go("Edit draft");
        var calls = permissions.Count;
        Go("Open two");
        check(program.Model.Discard is not null && permissions.Count == calls, "unsaved confirmation precedes all async work");
        program.Send(new CheckedMsg.Permission(false));
        Go("Discard");
        check(program.Model.Status == "Sign in" && permissions.Count == calls && program.Model.Path.Top.Note.Length > 0, "confirmation rechecks synchronous guards before discarding or starting work");
        program.Send(new CheckedMsg.Permission(true));
        Go("Open two");
        Go("Discard");
        var cancelled = program.Model.Guard.Sequence;
        Ready(cancelled, 0);
        program.Send(new CheckedMsg.SignOut());
        program.Send(new CheckedMsg.Permission(true));
        Go("Open two");
        var next = program.Model.Guard.Sequence;
        Ready(next, 0);
        permissions.Answer(new("main", cancelled, 0, RouteAdmission.Allow));
        Wait(() => program.PendingCommands == 1);
        check(next > cancelled && program.Model.Guard.Pending?.Request == next && program.Model.Path.Top.Route == "home", "sign-out cancels admission without reusing request IDs");
        program.Send(new CheckedMsg.Reply(new("other", next, 0, RouteAdmission.Allow)));
        tester.Settle();
        check(program.Model.Guard.Pending?.Step == 0, "another stack cannot advance a current check");
        program.Send(new CheckedMsg.Reply(new("main", next, 0, RouteAdmission.Allow), Foreign: true));
        tester.Settle();
        check(program.Model.Guard.Pending?.Step == 0, "a result cannot be applied through another stack's completion seam");
        Answer(next, 0);
        program.Send(new CheckedMsg.Permission(false));
        Answer(next, 1);
        check(program.Model.Path.Top.Route == "home" && program.Model.Status == "Sign in", "a changed prerequisite is rechecked after async approval");

        program.Send(new CheckedMsg.Permission(true));
        Go("Open one");
        var superseded = program.Model.Guard.Sequence;
        Ready(superseded, 0);
        Go("Open two");
        var current = program.Model.Guard.Sequence;
        Ready(current, 0);
        permissions.Answer(new("main", superseded, 0, RouteAdmission.Allow));
        Wait(() => program.PendingCommands == 1);
        check(program.Model.Guard.Pending?.Request == current && program.Model.Path.Top.Route == "home", "new navigation supersedes an older pending path");
        Answer(current, 0, RouteAdmission.Reject);
        check(program.Model.Path.Top.Route == "home" && program.Model.Status == "Rejected", "async rejection leaves the current path intact");

        Go("Open one");
        var edited = program.Model.Guard.Sequence;
        Ready(edited, 0);
        Go("Edit draft");
        permissions.Answer(new("main", edited, 0, RouteAdmission.Allow));
        Wait(() => program.PendingCommands == 0);
        check(program.Model.Guard.Pending is null && program.Model.Path.Top.Note == "Keep this draft", "editing withdraws an in-flight path without losing the newer draft");
        Go("Open one");
        current = program.Model.Guard.Sequence;
        Answer(current, 0, RouteAdmission.Redirect, "home");
        var redirected = program.Model.Guard.Sequence;
        check(redirected > current, "async redirect starts a newly guarded destination");
        Answer(redirected, 0);
        check(program.Model.Path.Top.Route == "home", "async redirect admits only its independently checked path");
        Go("Open one");
        current = program.Model.Guard.Sequence;
        Answer(current, 0, RouteAdmission.Defer);
        check(program.Model.Status == "Sign in" && program.Model.Guard.Pending is null, "async deferral stops the chain without committing");
        Shot("async-deferred");
        Go("Open one");
        current = program.Model.Guard.Sequence;
        Ready(current, 0);
        permissions.Fail(current, 0);
        Wait(() => program.Model.Guard.Pending is null);
        check(program.Model.Status == "Rejected" && program.Model.Path.Top.Route == "home", "a failing permission command rejects without changing the path");
        Go("Open one");
        current = program.Model.Guard.Sequence;
        Ready(current, 0);
        program.Send(new CheckedMsg.Refresh());
        Answer(current, 0);
        check(program.Model.Guard.Pending is null && program.Model.Path.Top.Route == "home" && !permissions.Contains(current, 1), "a refreshed source revision stops a matching but stale admission result");

        var recording = Path.Combine(output, "async-session.jsonl");
        program.Save(recording);
        var silent = new Permissions();
        using var replay = CheckedApp.CreateProgram(new KumikoServices().Add<IRoutePermissions>(silent));
        using var replayTester = new Tester(replay, new(520, 440, DevMode: true));
        replay.Replay(recording);
        replayTester.Settle();
        check(JsonSerializer.Serialize(replay.Model, CheckedAppKumikoJson.Default.Model) == JsonSerializer.Serialize(program.Model, CheckedAppKumikoJson.Default.Model) &&
            replay.PendingCommands == 0 && silent.Count == 0, "replay validates guarded transitions without running permission work");
        replay.Step(1);
        replayTester.Settle();
        check(replay.Model.Guard.Pending is not null && replay.PendingCommands == 0 && silent.Count == 0, "time travel installs a pending check without resuming it");
        check(replay.RestoreModel(snapshot), "a source-generated generic pending check restores under NativeAOT");
        replayTester.Settle();
        check(replay.History.Count == 1 && replay.Model.Guard.Pending?.Request == first && replay.PendingCommands == 0 && silent.Count == 0, "restoring pending navigation starts no work or synthetic messages");
        var bad = Path.Combine(output, "async-invalid.json");
        File.WriteAllText(bad, File.ReadAllText(snapshot).Replace("\"Step\":0", "\"Step\":999", StringComparison.Ordinal));
        var before = replay.Model;
        check(!replay.RestoreModel(bad) && ReferenceEquals(before, replay.Model), "invalid pending path steps fail before changing the installed model");
        check(replay.RestoreModel(installed), "a recorded protected model can be installed without an admission update");
        replayTester.Settle();
        check(replay.Model.Path.Top.Route == "one" && !replay.Model.SignedIn && replay.History.Count == 1 && replay.PendingCommands == 0 && silent.Count == 0,
            "snapshot installation adds no guard enforcement or synthetic navigation");
    }

    sealed class Permissions : IRoutePermissions
    {
        readonly ConcurrentDictionary<(long, int), TaskCompletionSource<RouteGuardReply<string>>> requests = new();
        public int Count => requests.Count;
        public bool Contains(long request, int step) => requests.ContainsKey((request, step));
        public void Answer(RouteGuardReply<string> reply) => requests[(reply.Request, reply.Step)].SetResult(reply);
        public void Fail(long request, int step) => requests[(request, step)].SetException(new IOException("Permission service unavailable"));
        public Task<RouteGuardReply<string>> Check(RouteGuardRequest<string> request, CancellationToken token)
        {
            var completion = new TaskCompletionSource<RouteGuardReply<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!requests.TryAdd((request.Request, request.Step), completion))
                throw new InvalidOperationException("The same async route check started twice.");
            return completion.Task.WaitAsync(token);
        }
    }
}
