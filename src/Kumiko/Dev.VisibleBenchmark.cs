using System.Diagnostics;
using System.Globalization;
using Kumiko.Platform;
using Kumiko.Rendering;
using O = Kumiko.Platform.ObjC;

namespace Kumiko;

public static partial class Dev
{
    // Opt-in instrumentation only. No readback or PNG work occurs until after MEASURED.
    public static void StartVisibleBenchmark(
        App app,
        Window window,
        string[] args,
        Func<long> frames,
        Func<long>? samples = null,
        Action<int>? changePage = null,
        Func<int>? currentPage = null,
        Func<int>? processCount = null
    )
    {
        if (Option(args, "--visible-benchmark") is not { } output)
            return;
        if (!OperatingSystem.IsMacOS())
            throw new PlatformNotSupportedException("Visible comparison requires macOS.");
        double duration = double.Parse(Option(args, "--duration") ?? "60", CultureInfo.InvariantCulture);
        if (duration < 10 || duration > 3600)
            throw new ArgumentOutOfRangeException(nameof(args));
        var clock = Stopwatch.StartNew();
        int previousPage = -1;
        void Status()
        {
            using var pool = new AutoreleasePool();
            var native = O.O(O.O(O.Class("NSApplication"), "sharedApplication"), "keyWindow");
            bool visible = window.IsVisible, uncovered = native != 0 && (O.N(native, "occlusionState") & 2) != 0;
            var c = window.Context!;
            Console.WriteLine(FormattableString.Invariant($"STATUS {clock.Elapsed.TotalSeconds:F6} {frames()} {samples?.Invoke() ?? 0} {c.Size.Width:F0} {c.Size.Height:F0} {c.Scale:F2} {(visible ? 1 : 0)} {(uncovered ? 1 : 0)} {GC.GetTotalMemory(false)} {currentPage?.Invoke() ?? 0} {processCount?.Invoke() ?? 0}"));
            if (changePage is not null)
            {
                int page = (int)(clock.Elapsed.TotalSeconds / 2) % 9;
                if (page != previousPage)
                {
                    previousPage = page;
                    changePage(page);
                    window.Invalidate();
                }
            }
        }

        app.Dispatch(Status);
        app.Schedule(TimeSpan.FromSeconds(1), Status, TimeSpan.FromSeconds(1));
        if (Option(args, "--fixed-redraw") is not null)
            app.Schedule(TimeSpan.FromSeconds(2), window.Invalidate, TimeSpan.FromSeconds(2));
        app.Schedule(TimeSpan.FromSeconds(duration), () =>
        {
            Console.WriteLine("MEASURED");
            _ = Task.Run(() =>
            {
                if (Console.ReadLine() != "CAPTURE")
                {
                    app.Dispatch(app.Quit);
                    return;
                }

                app.Dispatch(() =>
                {
                    try
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
                        if (changePage is not null)
                        {
                            for (int page = 0; page < 9; page++)
                            {
                                changePage(page);
                                Png.Write(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output))!, $"page-{page}.png"), window.Capture());
                            }

                            changePage(0);
                        }

                        Png.Write(output, window.Capture());
                        Console.WriteLine("CAPTURED " + output);
                    }
                    finally
                    {
                        app.Quit();
                    }
                });
            });
        });
    }
}
