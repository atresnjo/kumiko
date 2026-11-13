using System.Globalization;
using Kumiko.Runtime;
using Kumiko.UI;

namespace Kumiko;

public static partial class Dev
{
    public static void Run<TApp>(string title, float width, float height, string[] args, WindowOptions? options = null)
        where TApp : IKumiko
    {
        using var program = TApp.Create();
        Run(title, width, height, program, args, options);
    }

    public static void Run(string title, float width, float height, IProgram program, string[] args, WindowOptions? options = null)
    {
        ApplyCulture(args);
        if (args.Contains("--kumiko-dev", StringComparer.Ordinal))
            program.DevMode = true;
        if (args.Contains("--kumiko-debug", StringComparer.Ordinal))
            program.DebugEnabled = true;
        var windowSelector = Option(args, "--kumiko-window");
        var replay = Option(args, "--kumiko-replay");
        var step = Option(args, "--kumiko-step");
        if (replay is not null)
            program.Replay(replay);
        if ((replay is not null || step is not null) && Option(args, "--kumiko-snapshot") is null && Option(args, "--kumiko-tree") is null)
            args = [.. args, "--kumiko-snapshot=artifacts/kumiko-session.png"];
        if (step is not null)
        {
            var snapshot = Option(args, "--kumiko-snapshot") ?? Option(args, "--kumiko-tree")!;
            if (Option(args, "--kumiko-tree") is null)
                args = [.. args, "--kumiko-tree=" + Path.ChangeExtension(snapshot, ".txt")];
            if (!args.Contains("--kumiko-dev", StringComparer.Ordinal))
                args = [.. args, "--kumiko-dev"];
        }
        if (Option(args, "--kumiko-record") is { } record)
            program.Record(record);
        if (args.Contains("--kumiko-agent", StringComparer.Ordinal))
        {
            using var tester = new Tester(program, new(width, height, DevMode: program.DevMode));
            tester.Context.ReduceMotion = true;
            tester.Settle();
            AgentSession.Run(tester, program, Console.In, Console.Out, windowSelector);
            return;
        }
        if (Option(args, "--kumiko-snapshot") is not null || Option(args, "--kumiko-tree") is not null || Option(args, "--kumiko-audit") is not null)
        {
            if (step is not null)
                program.Step(int.Parse(step, CultureInfo.InvariantCulture));
            var windows = program.WindowList();
            var selectedWindow = windowSelector is null ? windows.FirstOrDefault() : windows.SingleOrDefault(w => w.Key == windowSelector || w.Title == windowSelector);
            if (selectedWindow is not null)
            {
                width = selectedWindow.Width;
                height = selectedWindow.Height;
            }
            Run(title, width, height, context =>
            {
                if (options?.Icons is { } icons)
                    context.ApplicationIcons = icons;
                if (windowSelector is null)
                    program.View(context);
                else
                {
                    var matches = program.WindowList().Where(w => w.Key == windowSelector || w.Title == windowSelector).ToArray();
                    if (matches.Length != 1)
                        throw new InvalidOperationException($"Expected one open window for {windowSelector}; found {matches.Length}.");
                    program.ViewWindow(context, matches[0].Key);
                }
            }, args);
            return;
        }
        using var app = new App(new(title));
        var theme = args.Contains("--dark", StringComparer.Ordinal) ? Theme.DarkTheme : null;
        var configured = options ?? new(Title: title, Width: width, Height: height, Theme: theme);
        configured = configured with
        {
            DevMode = configured.DevMode || program.DevMode
        };
        if (program.UsesWindows)
            app.NewWindows(program, configured);
        else
            app.NewWindow(configured, program);
        if (HotReload.Watching && replay is null)
        {
            var state = HotReload.StatePath(title);
            if (HotReload.Persist(program, app, state))
                Console.WriteLine($"Kumiko hot reload: restored the model from {state}");
            else if (File.Exists(state))
                Console.WriteLine("Kumiko hot reload: the model schema changed; starting from the initial model");
        }
        app.Run();
    }
}
