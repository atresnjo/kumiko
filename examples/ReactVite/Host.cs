using Kumiko;
using Kumiko.Runtime;
using Kumiko.Web;

namespace ReactVite;

/// <summary>Owns the native window, the Kumiko program and the embedded React page.</summary>
sealed class Host : IDisposable
{
    public const string WebViewId = "react";
    public const float Width = 900, Height = 640;
    public required Program<CounterModel, CounterMsg> Program
    {
        get; init;
    }
    public required Window Window
    {
        get; init;
    }
    public required Page Page
    {
        get; init;
    }

    public static void Run(string? devServer)
    {
        using var app = new App(new("React + Vite in Kumiko"));
        using var host = Start(app, devServer, visible: true);
        app.Run();
    }

    /// <summary>A null dev server serves the embedded Vite bundle from memory at kumiko://localhost/.</summary>
    public static Host Start(App app, string? devServer, bool visible)
    {
        var program = CounterApp.CreateProgram();
        var feed = new CounterFeed();
        HostServices.Register(app.Services, program, feed, devServer is null ? "bundled" : "dev-server");
        program.ModelChanged += () => feed.Publish(program.Model.Count);
        var options = new WindowOptions("React + Vite in Kumiko", Width, Height, MinWidth: 480, MinHeight: 360, Visible: visible);
        var window = app.NewWindow(options, program);
        // The browser is owned here, outside View/Update. ShellScreen only places it by ID.
        var page = window.NewWebView(WebViewId, devServer is null ? new(Assets: WebBundle.Load()) : new(Url: devServer));
        return new()
        {
            Program = program,
            Window = window,
            Page = page
        };
    }

    public void Dispose()
    {
        Window.Dispose();
        Program.Dispose();
    }
}
