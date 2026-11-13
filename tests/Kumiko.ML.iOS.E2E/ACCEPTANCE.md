# Native iOS text recognition

Failure scenarios recorded before implementation:

- Scanning the sample returns canned text rather than recognizing image pixels.
- The system button does not invoke Vision or the recognized text never reaches
  the native results view; scanning twice appends duplicate results.
- A blank image leaves stale recognized text or displays an error instead of an
  empty result. Invalid arguments fail without entering native code.
- Ignoring the supplied image orientation makes rotated input unreadable.
- Result confidence or normalized bounding rectangles are missing or invalid.
- Small iPhone and iPad layouts clip actions or results outside the safe area.
- A native callback lets an exception escape, or a picker delegate/action is
  collected before use or retained after its controller is disposed.
- Copy text is enabled before a scan or after an empty result, copies stale text
  or confidence annotations, or writes to the system clipboard during checks.
- Recognition runs on the main thread, so UIKit cannot process events while a
  scan is in progress; the asynchronous API completes on a released image after
  its caller disposes it.
- A cancelled request still runs, or disposing the controller during a scan
  updates released views or lets the result escape a native callback.
- Picked photos are decoded at full camera resolution before recognition, or the
  downsampled image loses its EXIF orientation or becomes unreadable.
- The separate Kumiko.iOS.ML package depends on the UIKit adapter, or its pack
  omits the library, README, or documentation.
- The NativeAOT registrar or trimming removes the recognition or UI code, or
  publishing emits compiler, trimming, or AOT warnings.

The check runs in its own headless simulator and spawns the E2E executable without UIApplicationMain,
showing a window, opening Simulator, using the system clipboard, or system input.
Drive real in-process UIButton actions on the example controller. Vision must
recognize the sample and a second independent image, clear results for blank
input, and recognize rotated input with its orientation supplied. Copy text
writes only the recognized lines to a private named pasteboard owned by the check.
The scan action and every main run-loop turn stay short during a cold scan. A
pre-cancelled request fails with cancellation, and disposing the controller
mid-scan is harmless. A large rotated photo decodes within the size limit,
upright, and remains recognizable. The packed Kumiko.iOS.ML has no dependencies.
Retain input PNGs, before/recognized/empty/narrow/iPad PNGs, recognition.json,
package.json, and report.json.
The JSON records observations from the actual Apple Vision request, not expected
fixture strings. PNGs render detached native views, never the display.

`--nativeaot` repeats the same flow under NativeAOT. Also publish the example
for ios-arm64 without signing. These checks do not cover physical-device speed,
Neural Engine utilization, real photo-picker gestures, camera access, or VoiceOver.
