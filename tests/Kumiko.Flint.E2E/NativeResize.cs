using System.Globalization;
using Kumiko;
using Kumiko.Rendering;

static class NativeResize
{
    internal static void Run(Harness h, string output)
    {
        if (!OperatingSystem.IsMacOS())
            return;
        using var app = new App(new(Name: "Flint hidden resize verification", Hidden: true));
        var errors = new List<Exception>();
        app.UnhandledException += errors.Add;
        var frames = 0;
        using var window = app.NewWindow(new(Title: "Flint hidden resize", Width: 1440, Height: 900, Visible: false), context =>
        {
            frames++;
            h.Program.View(context);
        });
        app.RunFor(TimeSpan.FromMilliseconds(30));
        var trace = new List<string> { "width,height,viewport_width,viewport_height,rendered_width,rendered_height,frames_before,frames_after,frames_after_dispatch" };
        var mismatches = new List<string>();
        var redundant = 0;
        foreach (var (width, height) in new[] { (1000f, 700f), (900f, 560f), (1001f, 701f), (1440f, 900f) })
        {
            var before = frames;
            window.Invalidate();
            window.Resize(width, height);
            var context = window.Context!;
            var root = context.Nodes.ToArray().Single(n => n.Key == "flint").Rect;
            if (root.Width != context.Size.Width || root.Height != context.Size.Height)
                mismatches.Add($"{width}x{height}: rendered {root.Width}x{root.Height}, viewport {context.Size}");
            var after = frames;
            app.RunFor(TimeSpan.FromMilliseconds(20));
            redundant += frames - after;
            trace.Add(string.Create(CultureInfo.InvariantCulture, $"{width},{height},{context.Size.Width},{context.Size.Height},{root.Width},{root.Height},{before},{after},{frames}"));
        }
        File.WriteAllLines(Path.Combine(output, "native-resize.csv"), trace);
        File.WriteAllLines(Path.Combine(output, "native-resize-differences.txt"), mismatches);
        Png.Write(Path.Combine(output, "native-resize.png"), window.Capture());
        h.Check(!window.IsVisible && errors.Count == 0, "Native resize verification stays hidden and reports no callback errors");
        h.Check(mismatches.Count == 0, "Native resize renders Flint at the new bounds before returning to AppKit");
        h.Check(redundant == 0, "Completed native resize consumes queued invalidations without redundant frames");
    }
}
