using System.Diagnostics;
using System.Text.Json;
using Kumiko;
using Kumiko.Platform;
using Kumiko.Rendering;
using Kumiko.UI;
using O = Kumiko.Platform.ObjC;

internal static partial class ReviewWindows
{
    internal static bool Child(string[] args)
    {
        if (args.FirstOrDefault() != "--review-windows-child")
            return false;
        using var app = new App(new("Window reentrancy fixture", Hidden: true, QuitOnLastWindow: false));
        using var pool = new AutoreleasePool();
        var errors = new List<Exception>();
        app.UnhandledException += errors.Add;
        foreach (string action in new[] { "resize", "bounds", "restore", "configure" })
        {
            using var window = app.NewWindow(new(Width: 320, Height: 200, Visible: false), c => c.Text("Reentrant disposal"));
            int closed = 0, late = 0;
            window.Closed += () => closed++;
            window.Moved += window.Dispose;
            window.Moved += () => late++;
            window.Resized += window.Dispose;
            window.Resized += () => late++;
            var bounds = window.Bounds;
            switch (action)
            {
                case "resize":
                    window.Resize(400, 280);
                    break;
                case "bounds":
                    window.SetBounds(bounds with
                    {
                        X = bounds.X + 30,
                        Y = bounds.Y + 40,
                        Width = 420,
                        Height = 300
                    });
                    break;
                case "restore":
                    window.Restore(new(40, 40, 440, 300));
                    break;
                case "configure":
                    window.SetMinimumSize(new(500, 400));
                    window.Resize(520, 420);
                    break;
            }

            app.RunFor(TimeSpan.FromMilliseconds(20));
            if (closed != 1 || late != 0 || errors.Count != 0 || app.Windows.Contains(window))
                throw new InvalidOperationException($"{action}: closed={closed}, late={late}, errors={string.Join(';', errors)}");
            bool rejected = false;
            try
            {
                window.Resize(640, 480);
            }
            catch (ObjectDisposedException)
            {
                rejected = true;
            }

            if (!rejected)
                throw new InvalidOperationException("Disposed native geometry accepted");
            Console.WriteLine(action + ": closed once; no callbacks after disposal; stale mutation rejected");
        }

        return true;
    }

    internal static void Run(string output, Action<bool, string> check)
    {
        if (!OperatingSystem.IsMacOS())
            return;
        var evidence = new List<string>();
        void Verify(bool condition, string name)
        {
            check(condition, "Window review: " + name);
            evidence.Add(name);
        }

        Directory.CreateDirectory(output);
        ReentrantProcess(output, Verify);
        using var app = new App(new("Window review", Hidden: true, QuitOnLastWindow: false));
        var errors = new List<Exception>();
        app.UnhandledException += errors.Add;
        using var pool = new AutoreleasePool();
        Chrome(app, output, Verify);
        Ownership(app, Verify);
        Transitions(app, Verify);
        Dock(output, Verify);
        Verify(errors.Count == 0, "native callbacks report no managed exceptions");
        using var stream = File.Create(Path.Combine(output, "review-windows.json"));
        using var json = new Utf8JsonWriter(stream, new()
        {
            Indented = true
        });
        json.WriteStartObject();
        json.WriteString("status", "passed");
        json.WriteString("nativeExecution", "hidden window responder/input, geometry, traffic lights, disposal, offscreen Dock view");
        json.WriteString("fixtureOnly", "full-screen/zoom requests and child visibility use private Objective-C endpoints; no compositor transitions or actual Dock display");
        json.WriteStartArray("checks");
        foreach (var item in evidence)
            json.WriteStringValue(item);
        json.WriteEndArray();
        json.WriteEndObject();
    }

    static void ReentrantProcess(string output, Action<bool, string> check)
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!)
        {
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };
        if (Path.GetFileNameWithoutExtension(Environment.ProcessPath!) == "dotnet")
            start.ArgumentList.Add(Path.GetFullPath(Environment.GetCommandLineArgs()[0]));
        start.ArgumentList.Add("--review-windows-child");
        start.Environment["NSZombieEnabled"] = "YES";
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(30_000))
        {
            process.Kill();
            throw new TimeoutException("Hidden window disposal subprocess");
        }

        string transcript = stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult();
        File.WriteAllText(Path.Combine(output, "review-windows-disposal.txt"), transcript);
        check(process.ExitCode == 0 && !transcript.Contains("deallocated instance", StringComparison.Ordinal),
            "resize/bounds/restore/configure survive synchronous subscriber disposal in an isolated NSZombie subprocess");
    }

    static unsafe void NativeInput(Window window, string text)
    {
        window.Draw();
        window.Input(i => i.Key = Key.Tab);
        var responder = O.O(window.NativeHandle, "firstResponder");
        var send = System.Runtime.InteropServices.NativeLibrary.GetExport(System.Runtime.InteropServices.NativeLibrary.Load("/usr/lib/libobjc.A.dylib"), "objc_msgSend");
        ((delegate* unmanaged<nint, nint, nint, NativeRange, void>)send)(responder, O.Sel("insertText:replacementRange:"), O.TempString(text), new NativeRange(nuint.MaxValue, 0));
    }

    static unsafe void Chrome(App app, string output, Action<bool, string> check)
    {
        var framelessText = new TextBuffer("");
        using var frameless = app.NewWindow(new(Width: 320, Height: 220, Visible: false)
        {
            Frameless = true,
            Resizable = true
        }, c => c.TextField(framelessText, new(Width: 280, Name: "Frameless editor"), key: "editor"));
        check(O.B(frameless.NativeHandle, "canBecomeKeyWindow") != 0 && O.B(frameless.NativeHandle, "canBecomeMainWindow") != 0,
            "frameless windows are eligible for key/main status without activation");
        check((O.N(frameless.NativeHandle, "styleMask") & 8) != 0, "frameless resizable windows retain the native resize mask");
        using var fixedWindow = app.NewWindow(new(Visible: false, Resizable: false)
        {
            Frameless = true
        }, c => c.Text("Fixed"));
        check((O.N(fixedWindow.NativeHandle, "styleMask") & 8) == 0, "fixed frameless windows do not gain resize capability");
        var materialText = new TextBuffer("");
        using var material = app.NewWindow(new(Width: 340, Height: 240, Visible: false, HiddenTitleBar: true)
        {
            Material = WindowMaterial.Sidebar,
            TrafficLightPosition = new(18, 8)
        }, c => c.TextField(materialText, new(Width: 280, Name: "Material editor"), key: "editor"));
        var wrapper = O.O(material.NativeHandle, "contentView");
        var content = O.O(O.O(wrapper, "subviews"), "objectAtIndex:", (nuint)0);
        check(O.O(material.NativeHandle, "firstResponder") == content && O.B(content, "acceptsFirstResponder") != 0,
            "material content remains the native first responder after wrapping");
        NativeInput(material, "material input");
        NativeInput(frameless, "frameless input");
        check(materialText.Text == "material input" && framelessText.Text == "frameless input",
            "native first-responder text insertion reaches the material and frameless editors without activation");
        check(!material.IsVisible && !frameless.IsVisible, "responder queries and private input leave native windows unmapped");
        material.Resize(480, 320);
        material.SetTitle("Resized material title");
        app.RunFor(TimeSpan.FromMilliseconds(40));
        for (nuint i = 0; i < 3; i++)
        {
            var frame = O.R(O.O(material.NativeHandle, "standardWindowButton:", i), "frame");
            check(Math.Abs(frame.Origin.X - (18 + i * 20)) < .1 && Math.Abs(frame.Origin.Y - 8) < .1,
                $"traffic light {i} keeps its configured position after native resize and title layout");
        }

        bool unsupported = false;
        try
        {
            material.Blur();
        }
        catch (PlatformNotSupportedException)
        {
            unsupported = true;
        }

        check(unsupported, "macOS Blur explicitly rejects the unsupported focus operation");
        Png.Write(Path.Combine(output, "review-windows-material.png"), material.Capture());
        using var nonclosable = app.NewWindow(new(Visible: false)
        {
            Closable = false
        }, c => c.Text("Programmatic close"));
        check(!nonclosable.CanNativeClose(), "nonclosable chrome rejects native close requests");
        int closed = 0;
        nonclosable.Closed += () => closed++;
        nonclosable.Close();
        check(closed == 1 && !app.Windows.Contains(nonclosable), "nonclosable chrome does not prevent programmatic Close");
    }
}
