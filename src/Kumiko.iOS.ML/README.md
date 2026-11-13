# Kumiko.iOS.ML

Experimental on-device text recognition for iOS apps, calling Apple's Vision
framework directly from C#. There is no external model, network request,
JavaScript, or Swift wrapper. The package depends only on Apple frameworks, so
it works with or without the `Kumiko.iOS` UIKit adapter.

```csharp
using Kumiko.iOS.ML;

var lines = await NativeTextRecognition.RecognizeAsync(cgImage, orientation, cancellationToken);
foreach (var line in lines)
    Console.WriteLine($"{line.Text} ({line.Confidence:P0})");
```

`RecognizeAsync` runs the Vision request on a worker thread and holds its own
reference to the image, so the caller may dispose its image immediately.
Its continuation runs on the thread pool; dispatch UI updates to the main queue.
Cancelling the token stops the Vision request. `Recognize` is the synchronous
form for code that is already on a worker thread.

Pass the image's ImageIO orientation. Results contain immutable managed strings,
confidence from 0 to 1, and Vision's normalized lower-left-origin bounding boxes.
No Vision objects escape the request. Recognition is currently English-only and
uses Vision's accurate level with language correction.

Requests take hundreds of milliseconds, more on first use while Vision loads its
model. Very large images cost memory and time without improving text accuracy;
downsample camera photos with ImageIO first (see the example's `PhotoLoader`).
This is not a live camera pipeline. No compute device or Neural Engine use is claimed.

Building or packing requires macOS with Xcode and the .NET iOS workload. See
[examples/ML.iOS](../../examples/ML.iOS/README.md) for a complete UIKit app.
