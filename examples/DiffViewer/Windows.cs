using DiffViewer.Sources;
using Kumiko;
using Kumiko.Runtime;

namespace DiffViewer;

/// <summary>
/// The interactive app's windows, one program each. Diff runs once per user: a later launch (a T3 action, a
/// terminal) hands its arguments to the running app, which focuses and reloads the window already showing what
/// they ask for, or opens a new window, so no open diff is replaced.
/// </summary>
internal sealed class DiffWindows(App app, Func<DiffRequest, Model> initial, WindowOptions options, bool devMode, bool debug)
{
    readonly List<(Program<Model, Msg> Program, Window Window)> open = [];

    public Program<Model, Msg> Open(DiffRequest request)
    {
        var fullScreen = new WindowFullScreen(app);
        var program = DiffApp.CreateProgram(initial(request), new KumikoServices().Add<IFullScreen>(fullScreen));
        // A long-running viewer keeps only the current model; reloads would otherwise retain every document.
        program.RetainHistory = false;
        program.DevMode = devMode;
        program.DebugEnabled = debug;
        // Outside a repository (opened from Finder or the Dock, whose folder is /), start in the project palette.
        if (!Arguments.InRepository(request))
            program.Send(new Msg.OpenQuick());
        var window = app.NewWindow(options with { DevMode = devMode }, program);
        fullScreen.Window = window;
        open.Add((program, window));
        // The program stops its watcher and loads once its window has gone.
        window.Closed += () =>
        {
            open.RemoveAll(w => w.Window == window);
            app.Dispatch(program.Dispose);
        };
        return program;
    }

    /// <summary>A later launch: its request goes to the window showing it, else to a new window; Diff comes to the front.</summary>
    public void Launch(SecondInstanceEvent launch)
    {
        DiffRequest request;
        try
        {
            request = Arguments.Parse(launch.Arguments, launch.WorkingDirectory);
        }
        catch (ArgumentException)
        {
            // Arguments Diff cannot read (such as --compare with one path) only bring it to the front.
            app.Activate();
            return;
        }
        var shown = open.FirstOrDefault(w => Shows(w.Program.Model, request));
        if (shown.Window is { } window)
        {
            shown.Program.Send(new Msg.Reload());
            window.Focus();
        }
        else
            Open(request);
        app.Activate();
    }

    /// <summary>
    /// True when a window's model shows what a launch asks for: the same source of the same checkout (by the path it
    /// was given or its root), or the same --pr of it. A window that has switched elsewhere since does not.
    /// </summary>
    internal static bool Shows(Model model, DiffRequest request)
    {
        var current = model.Request;
        if (request.Kind != current.Kind || request.Host != current.Host)
            return false;
        var same = new Place(request.Path, request.Host).Id == model.ProjectId || request.Path == current.Path;
        return request.Kind switch
        {
            SourceKind.PullRequest => same && request.PullQuery is not null && request.PullQuery == current.PullQuery,
            SourceKind.Paths => request.Path == current.Path && request.Target == current.Target,
            SourceKind.Demo => true,
            _ => same && request.Base == current.Base && request.Target == current.Target
        };
    }
}
