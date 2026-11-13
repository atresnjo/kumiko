using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Kumiko;
using Kumiko.Platform;
using Kumiko.Runtime;
using Kumiko.UI;
using Kumiko.Web;

static partial class ReviewLifecycle
{
    internal static bool Child(string[] args)
    {
        if (Environment.GetEnvironmentVariable("KUMIKO_REVIEW_OPEN_CHILD") == "1")
        {
            using var launched = new App(new("Launch operand fixture", Hidden: true, QuitOnLastWindow: false));
            int count = 0;
            launched.OpenFile += path =>
            {
                if (path == args[0])
                    count++;
            };
            using var timer = launched.Schedule(TimeSpan.FromMilliseconds(200), launched.Quit);
            launched.Run();
            Console.WriteLine("DELIVERIES=" + count);
            var native = ObjC.O(ObjC.Class("NSApplication"), "sharedApplication");
            ObjC.V(ObjC.O(native, "delegate"), "application:openFiles:", native,
                ObjC.O(ObjC.Class("NSArray"), "arrayWithObject:", ObjC.TempString(args[0])));
            Console.WriteLine("AFTER_REOPEN=" + count);
            return true;
        }
        if (args.FirstOrDefault() != "--review-lock")
            return false;
        using var app = new App(new("Crash lock fixture", Hidden: true, QuitOnLastWindow: false));
        Console.WriteLine(app.RequestSingleInstanceLock(args[1]) ? "FIRST" : "SECOND");
        Console.Out.Flush();
        if (args.ElementAtOrDefault(2) == "hold")
            app.Run();
        return true;
    }

    internal static void Run(string output, Action<bool, string> check)
    {
        var transcript = new List<string>();
        void Verify(bool passed, string text)
        {
            check(passed, text);
            transcript.Add(text);
        }
        string identity = "kumiko-crash-review-" + Guid.NewGuid().ToString("N");
        ProcessStartInfo Start(string mode)
        {
            var start = new ProcessStartInfo(Environment.ProcessPath!)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            if (Path.GetFileNameWithoutExtension(Environment.ProcessPath!) == "dotnet")
                start.ArgumentList.Add(Path.GetFullPath(Environment.GetCommandLineArgs()[0]));
            foreach (var arg in new[] { "--review-lock", identity, mode })
                start.ArgumentList.Add(arg);
            return start;
        }
        using (var owner = Process.Start(Start("hold"))!)
        {
            try
            {
                Verify(owner.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult() == "FIRST", "crash fixture owns isolated instance lock");
            }
            finally
            {
                if (!owner.HasExited)
                    owner.Kill();
                owner.WaitForExit();
            }
        }
        using (var replacement = Process.Start(Start("exit"))!)
        {
            try
            {
                Verify(replacement.WaitForExit(10000), "replacement instance completes after killed owner");
                Verify(replacement.ExitCode == 0 && replacement.StandardOutput.ReadToEnd().Contains("FIRST"), "stale native pipe is recovered after owner crash: " + replacement.StandardError.ReadToEnd());
            }
            finally
            {
                if (!replacement.HasExited)
                    replacement.Kill();
            }
        }
        if (OperatingSystem.IsMacOS())
        {
            string operand = Path.Combine(output, "review-launch.txt");
            File.WriteAllText(operand, "isolated launch operand");
            var launch = Start("exit");
            launch.ArgumentList.Clear();
            if (Path.GetFileNameWithoutExtension(Environment.ProcessPath!) == "dotnet")
                launch.ArgumentList.Add(Path.GetFullPath(Environment.GetCommandLineArgs()[0]));
            launch.Environment["KUMIKO_REVIEW_OPEN_CHILD"] = "1";
            launch.ArgumentList.Add(operand);
            using (var child = Process.Start(launch)!)
            {
                try
                {
                    Verify(child.WaitForExit(10000), "hidden launch operand child exits within deadline");
                    string result = child.StandardOutput.ReadToEnd() + child.StandardError.ReadToEnd();
                    File.WriteAllText(Path.Combine(output, "review-launch-delivery.txt"), result);
                    Verify(child.ExitCode == 0 && result.Contains("DELIVERIES=1") && result.Contains("AFTER_REOPEN=2"), "AppKit launch operand is delivered exactly once: " + result);
                }
                finally
                {
                    if (!child.HasExited)
                        child.Kill();
                }
            }
            Edges(output, Verify);
            ModelQuit(false, output, Verify);
            ModelQuit(true, output, Verify);
            using var app = new App(new("Nonclosable quit", Hidden: true, QuitOnLastWindow: false));
            using var window = app.NewWindow(new(Visible: false)
            {
                Closable = false
            }, _ => { });
            bool? approved = null;
            ((MacApp)app.Backend).TerminationReply = value => approved = value;
            var native = ObjC.O(ObjC.Class("NSApplication"), "sharedApplication");
            var termination = (nuint)ObjC.O(ObjC.O(native, "delegate"), "applicationShouldTerminate:", native);
            Verify(termination == 2 && approved is null, "AppKit termination defers its reply until quit processing");
            app.RunFor(TimeSpan.FromMilliseconds(40));
            Verify(approved == true && app.Windows.Count == 0, "nonclosable chrome does not prevent normal application quit");
        }
        File.WriteAllLines(Path.Combine(output, "review-lifecycle.txt"), transcript);
        using var file = File.Create(Path.Combine(output, "review-lifecycle.json"));
        using var json = new Utf8JsonWriter(file, new()
        {
            Indented = true
        });
        json.WriteStartArray();
        foreach (var line in transcript)
            json.WriteStringValue(line);
        json.WriteEndArray();
    }

    static void ModelQuit(bool veto, string output, Action<bool, string> check)
    {
        using var app = new App(new("Model quit fixture", Hidden: true, QuitOnLastWindow: false));
        var events = new List<string>();
        app.BeforeQuit += _ => events.Add("before");
        app.WillQuit += _ => events.Add("will");
        using var program = new Program<QuitModel, int>(new(Veto: veto), static (model, id) =>
            (model.Veto ? model : model with
            {
                First = id == 1 ? 0 : model.First,
                Second = id == 2 ? 0 : model.Second
            }, Cmd<int>.None),
            static (_, _, _) => { }, ReviewLifecycleJson.Default.QuitModel, ReviewLifecycleJson.Default.Int32);
        program.WithWindows(static model => new[] { model.First, model.Second }.Where(id => id != 0).Select(id => new WindowSpec<int, int>(id, "Quit " + id, id)),
            static (_, id) => NavigationRoot<int>.Create(NavigationHistory<int>.Start(id), "quit", _ => new QuitScreen().Map<int>(static message => message), _ => 0), ReviewLifecycleJson.Default.Int32);
        app.NewWindows(program, new(Visible: false));
        bool? reply = null;
        app.RequestNativeQuit(value => reply = value);
        var timer = Stopwatch.StartNew();
        while (reply is null && timer.Elapsed < TimeSpan.FromSeconds(5))
            app.RunFor(TimeSpan.FromMilliseconds(20));
        check(reply == !veto && app.Windows.Count == (veto ? 2 : 0), "one native quit resolves all model windows or preserves model veto");
        check(events.SequenceEqual(veto ? new[] { "before" } : new[] { "before", "will" }), "deferred model quit emits lifecycle events once");
        program.Save(Path.Combine(output, veto ? "review-quit-veto.jsonl" : "review-quit-clean.jsonl"));
    }

    sealed class QuitScreen() : Screen<QuitModel, int>(new())
    {
        protected override void Render() => C.Text("Quit fixture");
    }
}

internal sealed record QuitModel(int First = 1, int Second = 2, bool Veto = false, bool Replace = false);
[JsonSerializable(typeof(QuitModel))]
[JsonSerializable(typeof(int))]
internal partial class ReviewLifecycleJson : JsonSerializerContext;
