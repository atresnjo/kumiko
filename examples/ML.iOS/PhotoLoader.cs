using Foundation;
using ImageIO;
using UIKit;

internal static class PhotoLoader
{
    // Vision reads text well at this size; full camera images (12–48 MP) cost
    // hundreds of megabytes to decode and slow recognition without adding accuracy.
    internal const int MaxPixelSize = 2560;

    // Decodes directly at the reduced size with EXIF orientation applied, so the
    // result is upright. ImageIO is thread-safe; call this off the main thread.
    internal static UIImage Decode(NSData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        using var source = CGImageSource.FromData(data) ??
            throw new InvalidOperationException("The selected image format is not supported.");
        var options = new CGImageThumbnailOptions
        {
            CreateThumbnailFromImageAlways = true,
            CreateThumbnailWithTransform = true,
            MaxPixelSize = MaxPixelSize,
            ShouldCacheImmediately = true
        };
        using var pixels = source.CreateThumbnail(0, options) ??
            throw new InvalidOperationException("The selected image could not be decoded.");
        return UIImage.FromImage(pixels);
    }
}
