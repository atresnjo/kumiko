using System.Collections.Immutable;
using CoreGraphics;
using Foundation;
using ImageIO;
using Vision;

namespace Kumiko.iOS.ML;

/// <summary>A recognized line and its confidence and normalized image rectangle.</summary>
public sealed class RecognizedText
{
    internal RecognizedText(string text, float confidence, CGRect bounds)
    {
        Text = text;
        Confidence = confidence;
        Bounds = bounds;
    }

    /// <summary>The most likely transcription of the line.</summary>
    public string Text
    {
        get;
    }

    /// <summary>Vision's confidence in <see cref="Text"/>, from 0 to 1.</summary>
    public float Confidence
    {
        get;
    }

    /// <summary>Vision coordinates: unit image rectangle with its origin at the lower left.</summary>
    public CGRect Bounds
    {
        get;
    }
}

/// <summary>Direct, on-device text recognition through Apple's Vision framework.</summary>
/// <remarks>
/// Intended for still images, not a live camera frame-processing loop. No camera,
/// photo-library, network permission, or external model is required.
/// </remarks>
public static class NativeTextRecognition
{
    /// <summary>Recognizes English text on a worker thread, keeping the main thread free.</summary>
    /// <remarks>
    /// The request holds its own reference to the image, so the caller may dispose
    /// its image immediately. Continuations run on the thread pool; dispatch UI
    /// updates to the main queue. Cancellation stops the Vision request.
    /// </remarks>
    public static Task<ImmutableArray<RecognizedText>> RecognizeAsync(CGImage image,
        CGImagePropertyOrientation orientation = CGImagePropertyOrientation.Up,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        var owned = image.Clone() ?? throw new InvalidOperationException("The image could not be retained.");
        var work = Task.Run(() => Recognize(owned, orientation, cancellationToken), cancellationToken);
        work.ContinueWith(static (_, state) => ((CGImage)state!).Dispose(), owned,
            CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return work;
    }

    /// <summary>Recognizes English text synchronously on the calling thread.</summary>
    /// <remarks>
    /// Vision requests can take hundreds of milliseconds, more on first use while the
    /// model loads. Prefer <see cref="RecognizeAsync"/> from UI code.
    /// </remarks>
    public static ImmutableArray<RecognizedText> Recognize(CGImage image,
        CGImagePropertyOrientation orientation = CGImagePropertyOrientation.Up,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        cancellationToken.ThrowIfCancellationRequested();
        // Worker threads have no ambient pool for Vision's autoreleased objects.
        using var pool = new NSAutoreleasePool();
        using var request = new VNRecognizeTextRequest(null)
        {
            RecognitionLevel = VNRequestTextRecognitionLevel.Accurate,
            RecognitionLanguages = ["en-US"],
            UsesLanguageCorrection = true
        };
        using var options = new NSDictionary();
        using var handler = new VNImageRequestHandler(image, orientation, options);
        using var cancellation = cancellationToken.Register(static state => ((VNRequest)state!).Cancel(), request);
        var succeeded = handler.Perform([request], out var error);
        using (error)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!succeeded || error is not null)
                throw new InvalidOperationException(error?.LocalizedDescription ?? "Apple Vision could not recognize this image.");
        }
        var observations = request.Results ?? [];
        var lines = ImmutableArray.CreateBuilder<RecognizedText>(observations.Length);
        foreach (var observation in observations)
        {
            using (observation)
            {
                foreach (var candidate in observation.TopCandidates(1))
                {
                    using (candidate)
                        lines.Add(new(candidate.String, candidate.Confidence, observation.BoundingBox));
                }
            }
        }
        return lines.DrainToImmutable();
    }
}
