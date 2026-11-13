# Native iOS ML example

This example calls Apple's built-in Vision text recognizer directly from C#.
It uses real UIKit controls: UIButton actions, UIImageView preview, selectable
UITextView output, and PHPickerViewController for choosing a photo. It requires
neither JavaScript nor a Swift wrapper, third-party ML SDK, downloaded model,
network request, camera access, or broad photo-library permission.

Start with **Recognize text** to scan the locally rendered sample note, or use
**Choose photo** to select one image with Apple's system picker. **Sample note**
restores the fixture. Results show recognized lines and confidence, with request
latency in the status. **Copy text** copies only the recognized lines; it is
disabled until a scan finds text. A blank image clears previous results. Selected images
are kept in memory only. Recognition is currently configured for English.

## Build

Use the pinned .NET 10 SDK and the installed iOS workload/Xcode:

```sh
dotnet build examples/ML.iOS -c Release -r iossimulator-arm64 \
  -p:KumikoCompileMetal=false
```

The application ID is `dev.kumiko.ml`. Install the resulting `ML.iOS.app` onto a
simulator with `xcrun simctl install`, then launch it with `xcrun simctl launch`.
The automated checks below do not open the app or Simulator UI.

Build an unsigned NativeAOT device bundle locally:

```sh
dotnet publish examples/ML.iOS -c Release -r ios-arm64 \
  -p:KumikoiOSNativeAot=true -p:EnableCodeSigning=false \
  -p:BuildIpa=false -p:ArchiveOnBuild=false -p:KumikoCompileMetal=false
```

## Reusable package

Recognition lives in the separate [Kumiko.iOS.ML](../../src/Kumiko.iOS.ML/README.md)
package, which depends only on Apple frameworks. This example uses plain UIKit and
does not reference the `Kumiko.iOS` adapter.

```csharp
using Kumiko.iOS.ML;

var lines = await NativeTextRecognition.RecognizeAsync(cgImage, orientation, cancellationToken);
```

The Vision request runs on a worker thread with its own image reference, so the
main thread keeps handling events during a scan. Results return to UIKit through
the main dispatch queue. The example disables its actions while scanning and
cancels a pending scan when the controller is disposed. UIImage orientation is
translated to ImageIO orientation in the example.

Picked photos are decoded off the main thread by `PhotoLoader`, which uses ImageIO
to decode directly at most 2560 pixels on the long edge with EXIF orientation
applied, instead of decoding a full 12–48 MP camera image. This is not a live camera
processing pipeline. NativeAOT compiles the C# integration; Apple Vision executes
the ML inference. No particular compute device or Neural Engine utilization is claimed.
