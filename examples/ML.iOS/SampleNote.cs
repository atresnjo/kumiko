using CoreGraphics;
using Foundation;
using UIKit;

internal static class SampleNote
{
    // Generated locally from pixels; the recognizer never receives these strings.
    internal static UIImage Create(params string[] lines)
    {
        using var format = new UIGraphicsImageRendererFormat { Scale = 1, Opaque = true };
        using var renderer = new UIGraphicsImageRenderer(new CGSize(1000, 640), format);
        return renderer.CreateImage(context =>
        {
            UIColor.White.SetFill();
            context.FillRect(new CGRect(0, 0, 1000, 640));
            using var font = UIFont.SystemFontOfSize(58, UIFontWeight.Medium);
            var attributes = new UIStringAttributes { Font = font, ForegroundColor = UIColor.Black };
            using var dictionary = attributes.Dictionary;
            for (var i = 0; i < lines.Length; i++)
            {
                using var text = new NSString(lines[i]);
                text.DrawString(new CGPoint(70, 90 + i * 120), attributes);
            }
        });
    }
}
