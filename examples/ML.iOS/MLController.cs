using System.Collections.Immutable;
using System.Diagnostics;
using CoreFoundation;
using CoreGraphics;
using Kumiko.iOS.ML;
using UIKit;

// UIKit owns this example's image picker, preview, and selectable OCR results.
// The recognition service is reusable independently of this controller.
internal sealed partial class MLController(UIPasteboard? clipboard = null) : UIViewController
{
    readonly UILabel heading = Label("Read text from an image", UIFontTextStyle.Title1);
    readonly UILabel detail = Label("Apple Vision · on device", UIFontTextStyle.Subheadline);
    readonly UIImageView preview = new() { ContentMode = UIViewContentMode.ScaleAspectFit, ClipsToBounds = true };
    readonly UILabel status = Label("Scan the sample note, or choose a photo.", UIFontTextStyle.Footnote);
    readonly UITextView results = new() { Editable = false, Selectable = true, AccessibilityIdentifier = "recognized-text" };
    readonly UIButton recognize = new(UIButtonType.System) { AccessibilityIdentifier = "recognize" };
    readonly UIButton choose = new(UIButtonType.System) { AccessibilityIdentifier = "choose-photo" };
    readonly UIButton sample = new(UIButtonType.System) { AccessibilityIdentifier = "sample-note" };
    readonly UIButton copy = new(UIButtonType.System) { AccessibilityIdentifier = "copy-text" };
    readonly List<(UIButton Button, UIAction Action)> actions = [];
    UIImage? image;
    string recognized = "";
    CancellationTokenSource? scanning;
    bool disposed;

    // The recognition in progress, for checks that dispose the controller mid-scan.
    internal Task? Pending
    {
        get; private set;
    }

    public override void ViewDidLoad()
    {
        base.ViewDidLoad();
        Guard(() =>
        {
            View!.BackgroundColor = UIColor.SystemBackground;
            preview.BackgroundColor = UIColor.SecondarySystemBackground;
            preview.Layer.CornerRadius = 12;
            preview.IsAccessibilityElement = true;
            preview.AccessibilityLabel = "Image to recognize";
            results.BackgroundColor = UIColor.SecondarySystemBackground;
            results.Font = UIFont.GetPreferredFontForTextStyle(UIFontTextStyle.Body);
            results.AdjustsFontForContentSizeCategory = true;
            results.Layer.CornerRadius = 12;
            results.TextContainerInset = new UIEdgeInsets(16, 12, 16, 12);
            results.Text = "Recognized text will appear here.";
            AddAction(recognize, "Recognize text", true, Scan);
            AddAction(choose, "Choose photo", false, ChoosePhoto);
            AddAction(sample, "Sample note", false, LoadSample);
            AddAction(copy, "Copy text", false, CopyText);
            foreach (var child in new UIView[] { heading, detail, preview, status, recognize, choose, sample, copy, results })
                View.AddSubview(child);
            LoadSample();
        });
    }

    static UILabel Label(string text, UIFontTextStyle style) => new()
    {
        Text = text,
        Font = UIFont.GetPreferredFontForTextStyle(style) ?? throw new InvalidOperationException("Missing system font."),
        AdjustsFontForContentSizeCategory = true,
        Lines = 0
    };

    void AddAction(UIButton button, string title, bool primary, Action invoke)
    {
        using var configuration = primary ? UIButtonConfiguration.FilledButtonConfiguration : UIButtonConfiguration.TintedButtonConfiguration;
        configuration.Title = title;
        configuration.CornerStyle = UIButtonConfigurationCornerStyle.Medium;
        button.Configuration = configuration;
        var action = UIAction.Create(_ => Guard(invoke));
        actions.Add((button, action));
        button.AddAction(action, UIControlEvent.PrimaryActionTriggered);
    }

    void LoadSample()
    {
        using var note = SampleNote.Create("FOCUS SESSION", "Work for 45 minutes", "Rest for 15 minutes");
        SetImage(note);
        preview.AccessibilityLabel = "Sample note with a focus session schedule";
    }

    // Retain our own native image so callers and picker results keep their lifetime.
    internal void SetImage(UIImage value)
    {
        using var pixels = value.CGImage ?? throw new ArgumentException("The image has no bitmap.", nameof(value));
        var next = UIImage.FromImage(pixels, value.CurrentScale, value.Orientation);
        preview.Image = next;
        image?.Dispose();
        image = next;
        results.Text = "Recognized text will appear here.";
        SetRecognized("");
        status.Text = "Ready to recognize · English";
        View!.SetNeedsLayout();
    }

    void Scan()
    {
        if (!recognize.Enabled || image is null)
            return;
        using var pixels = image.CGImage ?? throw new InvalidOperationException("The image has no bitmap.");
        SetBusy(true);
        status.Text = "Recognizing text…";
        scanning = new CancellationTokenSource();
        var clock = Stopwatch.StartNew();
        // Vision runs on a worker thread and keeps its own image reference; UIKit stays responsive.
        var work = NativeTextRecognition.RecognizeAsync(pixels, Orientation(image.Orientation), scanning.Token);
        Pending = work;
        work.ContinueWith(done => DispatchQueue.MainQueue.DispatchAsync(() => Guard(() => Finish(done, clock))),
            CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    void Finish(Task<ImmutableArray<RecognizedText>> done, Stopwatch clock)
    {
        try
        {
            var lines = done.GetAwaiter().GetResult();
            results.Text = lines.IsEmpty ? "No text found. Try a clearer image." :
                string.Join("\n\n", lines.Select(line => $"{line.Text}\n{line.Confidence:P0} confidence"));
            SetRecognized(string.Join("\n", lines.Select(line => line.Text)));
            status.Text = $"{lines.Length} lines · {clock.Elapsed.TotalMilliseconds:F0} ms · on device";
        }
        finally
        {
            scanning?.Dispose();
            scanning = null;
            SetBusy(false);
            View!.SetNeedsLayout();
        }
    }

    static ImageIO.CGImagePropertyOrientation Orientation(UIImageOrientation value) => value switch
    {
        UIImageOrientation.Up => ImageIO.CGImagePropertyOrientation.Up,
        UIImageOrientation.Down => ImageIO.CGImagePropertyOrientation.Down,
        UIImageOrientation.Left => ImageIO.CGImagePropertyOrientation.Left,
        UIImageOrientation.Right => ImageIO.CGImagePropertyOrientation.Right,
        UIImageOrientation.UpMirrored => ImageIO.CGImagePropertyOrientation.UpMirrored,
        UIImageOrientation.DownMirrored => ImageIO.CGImagePropertyOrientation.DownMirrored,
        UIImageOrientation.LeftMirrored => ImageIO.CGImagePropertyOrientation.LeftMirrored,
        UIImageOrientation.RightMirrored => ImageIO.CGImagePropertyOrientation.RightMirrored,
        _ => ImageIO.CGImagePropertyOrientation.Up
    };

    // Copy only the recognized lines, without the confidence annotations.
    void CopyText()
    {
        if (recognized.Length == 0)
            return;
        (clipboard ?? UIPasteboard.General).String = recognized;
        status.Text = "Copied recognized text";
        View!.SetNeedsLayout();
    }

    void SetRecognized(string text)
    {
        recognized = text;
        copy.Enabled = text.Length > 0;
    }

    void SetBusy(bool busy)
    {
        recognize.Enabled = !busy;
        choose.Enabled = !busy;
        sample.Enabled = !busy;
        copy.Enabled = !busy && recognized.Length > 0;
    }

    public override void ViewDidLayoutSubviews()
    {
        base.ViewDidLayoutSubviews();
        Guard(() =>
        {
            var safe = View!.SafeAreaLayoutGuide.LayoutFrame;
            var width = Math.Min(640, safe.Width - 40);
            var x = safe.X + (safe.Width - width) / 2;
            nfloat y = safe.Y + 16;
            void Place(UIView child, nfloat height)
            {
                child.Frame = new CGRect(x, y, width, height);
                y += height + 12;
            }
            Place(heading, heading.SizeThatFits(new CGSize(width, nfloat.MaxValue)).Height);
            Place(detail, detail.SizeThatFits(new CGSize(width, nfloat.MaxValue)).Height);
            Place(preview, (nfloat)Math.Min(250, safe.Height * .25));
            Place(recognize, 50);
            var half = (width - 12) / 2;
            choose.Frame = new CGRect(x, y, half, 44);
            sample.Frame = new CGRect(x + half + 12, y, half, 44);
            y += 56;
            Place(status, status.SizeThatFits(new CGSize(width, nfloat.MaxValue)).Height);
            Place(copy, 44);
            results.Frame = new CGRect(x, y, width, Math.Max(44, safe.Bottom - y - 16));
        });
    }

    void Guard(Action callback)
    {
        if (disposed)
            return;
        try
        {
            callback();
        }
        catch (Exception error)
        {
            SetBusy(false);
            status.Text = "Could not read this image. Try another photo.";
            Console.Error.WriteLine(error);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !disposed)
        {
            disposed = true;
            // The pending completion sees `disposed` and skips the released views.
            scanning?.Cancel();
            scanning = null;
            DisposePicker();
            foreach (var binding in actions)
            {
                binding.Button.RemoveAction(binding.Action, UIControlEvent.PrimaryActionTriggered);
                binding.Action.Dispose();
            }
            actions.Clear();
            preview.Image = null;
            image?.Dispose();
            foreach (var child in new UIView[] { heading, detail, preview, status, recognize, choose, sample, copy, results })
            {
                child.RemoveFromSuperview();
                child.Dispose();
            }
        }
        base.Dispose(disposing);
    }
}
