using System.Text.Json;
using System.Text.Json.Serialization;
using CoreGraphics;
using Foundation;
using ImageIO;
using Kumiko.iOS.ML;
using UIKit;

internal static partial class EntryPoint
{
    static int Main(string[] args)
    {
        try
        {
            Run(Environment.GetEnvironmentVariable("KUMIKO_ML_ARTIFACTS") ?? args.Single());
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    static void Run(string output)
    {
        Directory.CreateDirectory(output);
        using var pool = new NSAutoreleasePool();
        // A private named pasteboard keeps the system clipboard untouched.
        var clipboardName = "dev.kumiko.mlchecks." + Guid.NewGuid().ToString("N");
        using var clipboard = UIPasteboard.FromName(clipboardName, true) ?? throw new InvalidOperationException("Missing private pasteboard.");
        try
        {
            Run(output, clipboard);
        }
        finally
        {
            UIPasteboard.Remove(clipboardName);
        }
    }

    static void Run(string output, UIPasteboard clipboard)
    {
        using var controller = new MLController(clipboard);
        var view = controller.View!;
        view.Frame = new CGRect(0, 0, 393, 852);
        view.LayoutIfNeeded();
        Capture(view, Path.Combine(output, "before.png"));
        var scan = Find<UIButton>(view, "recognize");
        var copy = Find<UIButton>(view, "copy-text");
        Require(!copy.Enabled, "Copy disabled before recognition");
        // Time the action itself too: synchronous recognition would block inside it.
        var tap = System.Diagnostics.Stopwatch.StartNew();
        scan.SendActionForControlEvents(UIControlEvent.PrimaryActionTriggered);
        tap.Stop();
        var pumped = PumpUntil(() => scan.Enabled);
        var longestTurn = pumped > tap.Elapsed ? pumped : tap.Elapsed;
        // A cold Vision request on the main thread blocks for seconds; first-use JIT
        // and thread-pool startup on a fresh simulator account for up to ~250 ms.
        Require(longestTurn < TimeSpan.FromMilliseconds(500), $"Main run loop turns during cold scan (longest {longestTurn.TotalMilliseconds:F0} ms; tap {tap.Elapsed.TotalMilliseconds:F0} ms)");
        var results = Find<UITextView>(view, "recognized-text");
        Require((results.Text ?? "").Contains("FOCUS SESSION", StringComparison.OrdinalIgnoreCase), "System button recognizes sample heading");
        Require((results.Text ?? "").Contains("45 minutes", StringComparison.OrdinalIgnoreCase), "System button recognizes sample body");
        var initial = results.Text;
        Require(copy.Enabled, "Copy enabled after recognition");
        copy.SendActionForControlEvents(UIControlEvent.PrimaryActionTriggered);
        var copied = clipboard.String ?? "";
        Require(copied.Contains("FOCUS SESSION", StringComparison.OrdinalIgnoreCase) &&
            copied.Contains("45 minutes", StringComparison.OrdinalIgnoreCase), "Copy writes recognized lines");
        Require(!copied.Contains("confidence"), "Copy omits confidence annotations");
        Capture(view, Path.Combine(output, "recognized.png"));
        scan.SendActionForControlEvents(UIControlEvent.PrimaryActionTriggered);
        PumpUntil(() => scan.Enabled);
        Require(results.Text == initial, "Repeated scan replaces results");
        using var independent = SampleNote.Create("NATIVE VISION", "Room 204", "Meet at 10:30");
        Save(independent, Path.Combine(output, "independent-input.png"));
        var observations = NativeTextRecognition.Recognize(independent.CGImage!);
        Require(observations.Any(line => line.Text.Contains("204")), "Vision reads independent image pixels");
        Require(observations.All(line => line.Confidence is >= 0 and <= 1 &&
            line.Bounds.Width > 0 && line.Bounds.Height > 0 && line.Bounds.X >= 0 && line.Bounds.Y >= 0 &&
            line.Bounds.Right <= 1.001 && line.Bounds.Bottom <= 1.001), "Valid confidence and normalized bounds");
        var records = observations.Select(line => new Observation(line.Text, line.Confidence,
            (double)line.Bounds.X, (double)line.Bounds.Y, (double)line.Bounds.Width, (double)line.Bounds.Height)).ToArray();
        File.WriteAllText(Path.Combine(output, "recognition.json"), JsonSerializer.Serialize(records, ReportJson.Default.ObservationArray));
        using var rotated = Rotate(independent);
        Save(rotated, Path.Combine(output, "rotated-input.png"));
        var rotatedResults = NativeTextRecognition.Recognize(rotated.CGImage!, CGImagePropertyOrientation.Left);
        Require(rotatedResults.Any(line => line.Text.Contains("204")), "Orientation-aware recognition");
        using var blank = SampleNote.Create();
        Save(blank, Path.Combine(output, "blank-input.png"));
        controller.SetImage(blank);
        scan.SendActionForControlEvents(UIControlEvent.PrimaryActionTriggered);
        PumpUntil(() => scan.Enabled);
        Require(results.Text == "No text found. Try a clearer image.", "Blank input clears previous results");
        Require(!copy.Enabled, "Copy disabled after empty result");
        Capture(view, Path.Combine(output, "empty.png"));
        controller.SetImage(independent);
        scan.SendActionForControlEvents(UIControlEvent.PrimaryActionTriggered);
        PumpUntil(() => scan.Enabled);
        Require((results.Text ?? "").Contains("204"), "Recognition recovers after empty results");
        copy.SendActionForControlEvents(UIControlEvent.PrimaryActionTriggered);
        Require((clipboard.String ?? "").Contains("204") && !(clipboard.String ?? "").Contains("FOCUS"), "Copy replaces stale text");
        var nullRejected = false;
        try
        {
            NativeTextRecognition.Recognize(null!);
        }
        catch (ArgumentNullException)
        {
            nullRejected = true;
        }
        Require(nullRejected, "Null image rejected");
        CheckAsync(independent);
        CheckPhotoLoader(rotated, output);
        CheckDisposeDuringScan(clipboard);
        foreach (var (name, size) in new[] { ("narrow", new CGSize(320, 640)), ("ipad", new CGSize(820, 1180)) })
        {
            view.Frame = new CGRect(CGPoint.Empty, size);
            view.SetNeedsLayout();
            view.LayoutIfNeeded();
            Require(scan.Frame.Width >= 44 && scan.Frame.Height >= 44, "Native touch target at " + name);
            Require(copy.Frame.Width >= 44 && copy.Frame.Height >= 44, "Copy touch target at " + name);
            Require(results.Frame.Right <= size.Width && results.Frame.Bottom <= size.Height, "Results fit at " + name);
            Capture(view, Path.Combine(output, name + ".png"));
        }
        controller.Dispose();
        Require(scan.Handle == IntPtr.Zero, "Native controls released with controller");
        File.WriteAllText(Path.Combine(output, "report.json"),
            $$"""
            {"passed":true,"longestMainTurnMs":{{longestTurn.TotalMilliseconds:F0}},"async":true,"cancellation":true,
             "disposeDuringScan":true,"photoDownsample":true,"engine":"Apple Vision","offscreen":true,"realRecognition":true,
             "nativeButton":true,"repeatedScan":true,"blankImage":true,"orientation":true,
             "boundsAndConfidence":true,"copy":true,"nullRejected":true,"resize":true,"disposed":true}
            """);
    }

    static UIImage Rotate(UIImage image)
    {
        using var format = new UIGraphicsImageRendererFormat { Scale = 1, Opaque = true };
        using var renderer = new UIGraphicsImageRenderer(new CGSize(image.Size.Height, image.Size.Width), format);
        return renderer.CreateImage(context =>
        {
            context.CGContext.TranslateCTM(image.Size.Height, 0);
            context.CGContext.RotateCTM((nfloat)(Math.PI / 2));
            image.Draw(CGPoint.Empty);
        });
    }

    static T Find<T>(UIView view, string identifier) where T : UIView
    {
        if (view is T match && view.AccessibilityIdentifier == identifier)
            return match;
        foreach (var child in view.Subviews)
        {
            var found = FindOrNull<T>(child, identifier);
            if (found is not null)
                return found;
        }
        throw new InvalidOperationException("Missing native view: " + identifier);
    }

    static T? FindOrNull<T>(UIView view, string identifier) where T : UIView =>
        view is T match && view.AccessibilityIdentifier == identifier ? match :
            view.Subviews.Select(child => FindOrNull<T>(child, identifier)).FirstOrDefault(child => child is not null);

    // Returns the longest single run-loop turn, which grows if work blocks the main thread.
    static TimeSpan PumpUntil(Func<bool> done)
    {
        var timeout = System.Diagnostics.Stopwatch.StartNew();
        var turn = new System.Diagnostics.Stopwatch();
        var longest = TimeSpan.Zero;
        while (!done() && timeout.Elapsed < TimeSpan.FromSeconds(60))
        {
            turn.Restart();
            NSRunLoop.Current.RunUntil(NSDate.FromTimeIntervalSinceNow(.01));
            longest = turn.Elapsed > longest ? turn.Elapsed : longest;
        }
        Require(done(), "Native event loop completed recognition");
        return longest;
    }

    static void Capture(UIView view, string path)
    {
        view.LayoutIfNeeded();
        using var format = new UIGraphicsImageRendererFormat { Scale = 2, Opaque = true };
        using var renderer = new UIGraphicsImageRenderer(view.Bounds.Size, format);
        using var image = renderer.CreateImage(context => view.Layer.RenderInContext(context.CGContext));
        Save(image, path);
    }

    static void Save(UIImage image, string path)
    {
        using var png = image.AsPNG() ?? throw new InvalidOperationException("PNG encoding failed.");
        File.WriteAllBytes(path, png.ToArray());
    }

    static void Require(bool condition, string description)
    {
        if (!condition)
            throw new InvalidOperationException(description);
        Console.WriteLine("PASS " + description);
    }
}

internal sealed record Observation(string Text, float Confidence, double X, double Y, double Width, double Height);

[JsonSerializable(typeof(Observation[]))]
internal sealed partial class ReportJson : JsonSerializerContext;
