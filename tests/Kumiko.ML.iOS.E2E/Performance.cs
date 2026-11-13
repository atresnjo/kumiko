using CoreGraphics;
using Foundation;
using ImageIO;
using Kumiko.iOS.ML;
using UIKit;

internal static partial class EntryPoint
{
    static void CheckAsync(UIImage source)
    {
        // The only reference to these pixels is released before Vision starts.
        var detached = Detached(source);
        var pending = NativeTextRecognition.RecognizeAsync(detached);
        detached.Dispose();
        PumpUntil(() => pending.IsCompleted);
        Require(pending.Result.Any(line => line.Text.Contains("204")), "Async request owns its image after the caller disposes it");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        using var pixels = source.CGImage!;
        var skipped = NativeTextRecognition.RecognizeAsync(pixels, cancellationToken: cancelled.Token);
        PumpUntil(() => skipped.IsCompleted);
        Require(skipped.IsCanceled, "Cancelled request does not run");
    }

    static void CheckPhotoLoader(UIImage rotated, string output)
    {
        // A camera-sized JPEG whose pixels are stored rotated, with EXIF orientation to stand it upright.
        using var format = new UIGraphicsImageRendererFormat { Scale = 1, Opaque = true };
        var size = new CGSize(rotated.Size.Width * 4, rotated.Size.Height * 4);
        using var renderer = new UIGraphicsImageRenderer(size, format);
        using var large = renderer.CreateImage(_ => rotated.Draw(new CGRect(CGPoint.Empty, size)));
        using var pixels = large.CGImage!;
        using var encoded = new NSMutableData();
        using (var destination = CGImageDestination.Create(encoded, "public.jpeg", 1) ??
            throw new InvalidOperationException("JPEG encoder unavailable."))
        {
            using var orientation = NSNumber.FromInt32((int)CGImagePropertyOrientation.Left);
            using var properties = NSDictionary.FromObjectAndKey(orientation, ImageIO.CGImageProperties.Orientation);
            destination.AddImage(pixels, properties);
            Require(destination.Close(), "Encode large rotated photo");
        }
        using var decoded = PhotoLoader.Decode(encoded);
        using var upright = decoded.CGImage!;
        Require(Math.Max(upright.Width, upright.Height) <= PhotoLoader.MaxPixelSize && Math.Max(upright.Width, upright.Height) > PhotoLoader.MaxPixelSize / 2,
            $"Photo decoded within size limit ({upright.Width}×{upright.Height} from {pixels.Width}×{pixels.Height})");
        Require(upright.Width > upright.Height && decoded.Orientation == UIImageOrientation.Up, "Decoded photo applies EXIF orientation");
        Require(NativeTextRecognition.Recognize(upright).Any(line => line.Text.Contains("204")), "Downsampled photo stays recognizable");
        Save(decoded, Path.Combine(output, "photo-decoded.png"));
    }

    static void CheckDisposeDuringScan(UIPasteboard clipboard)
    {
        var controller = new MLController(clipboard);
        var view = controller.View!;
        view.Frame = new CGRect(0, 0, 393, 852);
        view.LayoutIfNeeded();
        var scan = Find<UIButton>(view, "recognize");
        scan.SendActionForControlEvents(UIControlEvent.PrimaryActionTriggered);
        var pending = controller.Pending ?? throw new InvalidOperationException("Scan did not start.");
        controller.Dispose();
        PumpUntil(() => pending.IsCompleted);
        // Let the queued completion reach the disposed controller.
        NSRunLoop.Current.RunUntil(NSDate.FromTimeIntervalSinceNow(.2));
        Require(pending.IsCanceled, "Disposing mid-scan cancels recognition");
        Require(scan.Handle == IntPtr.Zero, "Mid-scan disposal releases native controls");
    }

    static CGImage Detached(UIImage image)
    {
        using var source = image.CGImage!;
        using var space = CGColorSpace.CreateDeviceRGB();
        using var context = new CGBitmapContext(null, source.Width, source.Height, 8, 0, space, CGImageAlphaInfo.PremultipliedLast);
        context.DrawImage(new CGRect(0, 0, source.Width, source.Height), source);
        return context.ToImage() ?? throw new InvalidOperationException("Bitmap copy failed.");
    }
}
