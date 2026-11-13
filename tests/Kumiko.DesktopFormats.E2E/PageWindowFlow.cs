using Kumiko;
using Kumiko.Web;

static class PageWindowFlow
{
    internal static void Run(Action<bool, string> check)
    {
        if (!OperatingSystem.IsMacOS())
            return;
        using var app = new App(new(Name: "Page window fixture", Hidden: true));
        var errors = new List<Exception>();
        app.UnhandledException += errors.Add;
        app.Services.Bind("Fixture", "echo", static (string text) => "echo:" + text, PageWindowJson.Default.String,
            PageWindowJson.Default.String, "string", "string");
        using var window = app.NewPage(new(Width: 400, Height: 300, Visible: false, HiddenTitleBar: true), new(Assets: [Asset.Text("/index.html", """
            <!doctype html><html><body style="margin:0">
            <header id="bar" style="--app-region: drag; height: 40px"><span id="title">Title</span>
            <button id="close" style="--app-region: no-drag">Close</button></header>
            <script type="module">
            import {Fixture} from '/kumiko.js';
            kumiko.log(await Fixture.echo('ready'));
            </script></body></html>
            """)]));
        var logs = new List<string>();
        window.Page!.Logged += logs.Add;
        Pump(app, () => logs.Contains("echo:ready"));
        check(window.Context is null && logs.Contains("echo:ready"),
            "program-free page window loads embedded assets and calls typed services");
        var before = window.State;
        window.Page.Evaluate("""
            const press = (id) => document.getElementById(id).dispatchEvent(new MouseEvent('mousedown', {button: 0, detail: 1, bubbles: true}));
            press('close');
            press('title');
            kumiko.log('pressed');
            """);
        Pump(app, () => logs.Contains("pressed"));
        check(window.MoveRequests == 1, "drag region requests one window move; a no-drag descendant requests none");
        check(window.MovesStarted == 0 && window.State == before && !window.IsVisible && errors.Count == 0,
            "a move without a held primary button is refused without moving, showing or erroring the window");
    }

    static void Pump(App app, Func<bool> done)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (clock.Elapsed < TimeSpan.FromSeconds(10) && !done())
            app.RunFor(TimeSpan.FromMilliseconds(30));
        if (!done())
            throw new TimeoutException("Page window fixture did not respond.");
    }
}

[System.Text.Json.Serialization.JsonSerializable(typeof(string))]
internal partial class PageWindowJson : System.Text.Json.Serialization.JsonSerializerContext;
