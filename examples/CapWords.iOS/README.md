# Motion Studies · Kumiko

The gallery keeps a `NavigationHistory<GalleryScreen>` in its model. Opening a
study from the gallery pushes a visit; a study's top-left arrow moves Back to the
gallery's retained visit, leaving the study as a Forward entry until another
study is opened. History requests apply only when they are current and come from
the gallery's host. Each study maps its screen messages into the gallery model;
the home screen receives only the CapWords artwork it previews. A study run on
its own keeps a one-visit history in its own model. Opal's timer controls are a
keyed child view shared with the Reduced Motion fixture.

One native C# / UIKit app containing four ports from the user's licensed local
React Native checkout, `~/projects/personal/rn-makeitanimated`.

- [CapWords](https://makeitanimated.dev/animations/capwords-shared-transition-animation):
  shared illustrations and headers between a word collection and its details.
- [Alma](https://makeitanimated.dev/animations/alma-nutrients-circular-carousel-animation):
  eight nutrient cards traveling along a spring-driven circular path.

- [Slack Catch Up](https://makeitanimated.dev/animations/slack-catch-up-cards-swipe-animation):
  drag a five-card stack right to mark Read or left to Keep Unread, with
  top/bottom-dependent tilt, progress rings, release momentum and Undo.
- [Opal Set Timer Slider](https://makeitanimated.dev/animations/opal-set-timer-slider-animation):
  compact timer controls expand into a discrete slider with spring fill and
  anchored elastic overdrag on the source demo's intentionally open black canvas.

The app opens a gallery with an illustrated card for each example. Each study's
top-left arrow returns to the gallery; child state survives switching. Swipe
Alma's cards horizontally to follow the arc and snap with release velocity.
Swiping pauses its 2.5-second auto-advance; the top-right control resumes it.
Previous/Next also let you inspect each card.

Tap either illustration to open its detail. Back reverses the shared image,
date and word-count transition. Detail content arrives at 100/200/300 ms.
Camera, offers, pronunciation and locked examples show explanatory demo sheets;
the original implements those actions as simulated presses.

Licensed PNG artwork is embedded from the original local checkout at build time.
Set the `CapWordsAssetRoot` MSBuild property to select another licensed
checkout. No original TypeScript or artwork is copied into the repository.
The example and its dedicated E2E project are opt-in and outside the default
solutions so standard builds do not require private reference assets.

Artifacts in `artifacts/capwords`: PNG/TXT list/detail captures at phone, narrow
and tablet sizes, session.jsonl, byte-identical live/replay PNGs, report.json,
spring-in-flight.png, pressed/released PNGs, quick-report.json and live-report.json. Repeat the command to reproduce them.
Gallery and Alma phone/narrow/tablet captures, gallery session replay,
`gallery-report.json`, `gallery-live-report.json`, and `swipe-report.json` cover
switching, child timers, circular motion, native pan input, release, interruption
and cancellation. Gesture input is delivered to the native recognizer callback
with controlled coordinates; no system input or whole-screen capture is used.
These checks do not certify physical-device gesture feel.
See [PORTING.md](PORTING.md) for the framework findings and remaining gaps.

## Historical phone builds

Earlier phone installation used Release NativeAOT, `ios-arm64`, a local
Apple Development identity and a profile that includes the device. It is a
signed development installation, with no App Store upload. The commands below
record that earlier workflow; the Opal extension is simulator-only.

```sh
dotnet publish examples/CapWords.iOS -c Release -r ios-arm64 \
  -p:KumikoiOSNativeAot=true -p:KumikoCompileMetal=false \
  -p:CodesignKey="$KUMIKO_SIGNING_IDENTITY" \
  -p:CodesignProvision="$KUMIKO_PROVISIONING_PROFILE" \
  -p:BuildIpa=false -p:ArchiveOnBuild=false
xcrun devicectl device install app --device "iPhone Adnan" \
  examples/CapWords.iOS/bin/Release/net10.0-ios/ios-arm64/CapWords.iOS.app
xcrun devicectl device process launch --device "iPhone Adnan" \
  --terminate-existing --environment-variables '{"KUMIKO_STUDY":"alma"}' \
  dev.kumiko.capwords
```

`artifacts/capwords/performance-fix-nativeaot-report.json` records the earlier signed
three-study build: 5.61 MB bundle, binary hash, no managed assemblies and no compiler
warnings. It is not installed: iPhone Adnan was unavailable, and the user chose
simulator-only verification. `slack-nativeaot-report.json` preserves the earlier
installation. Touch feel, haptics and VoiceOver need hands-on verification.

The current four-study `artifacts/opal/nativeaot-report.json` records local unsigned
Release `ios-arm64` NativeAOT validation: passed, zero warnings, no managed DLLs,
5,733,619-byte bundle, not installed on a phone. This is separate from the older
signed build/installation evidence. The supplied latest solution build also
passed with zero warnings; neither build was rerun for this documentation update.

## Slack Catch Up

The plum gradient, dark synthetic channel cards, native vertical message scrolling
and Read/Keep Unread controls follow the source. A horizontal drag that exceeds a
quarter of the screen width commits one card; shorter and cancelled drags spring
back. Undo reverses dismissal, including during motion and after completion.
The remaining count flips in the direction of progress. Done returns to the
gallery. Messages are synthetic placeholders, and composing is not connected.

`slack-report.json` and `slack-live-report.json` record routing, bounded cards,
read/unread status, duplicate guards, Undo, completion, retained state, scrolling,
anchor-dependent rotation, momentum, spring interruption, counter flip, resize
and pixel replay. `slack-*.png` covers phone, narrow, tablet, drag and completion.
Set `KUMIKO_STUDY=slack` when launching to enter Slack directly.

## Earlier Slack simulator frame pacing

The 60 Hz Release simulator workload warms the app, measures idle, continuous two-axis dragging,
cancel/return, swipe commits, footer commits and Undo, and writes raw CSV plus
`artifacts/capwords/performance-fixed/slack-frame-pacing.json`. Screenshot capture runs
after measurement.

The original baseline measured 41.9 callbacks/s during swipe commits, 49.2 during
footer commits and 50.9 during Undo, with a 113 ms maximum callback gap. It remains
in `artifacts/capwords/performance`. The optimized renderer retains unchanged
native appearance/geometry and text metrics; the deck omits constant animation
channels. Updated results are recorded separately in `performance-fixed`.
The default workload intentionally injects an entire swipe in one callback.
`--paced --output artifacts/capwords/performance-paced` additionally measures a
250 ms display-link-paced approach to release; compare it separately.
The final original-workload run measures 56.7 callbacks/s during swipe commits,
59.2 during footer commits and 59.3 during Undo, with a 41.7 ms maximum interval.
No callback arrival gap exceeds 50 ms, versus 33 in the baseline. Occasional
missed callbacks remain; this is an improvement, not a perfect 60 FPS result.
See `artifacts/capwords/performance-comparison.json`.

CADisplayLink measures UI callback/frame pacing, not actual GPU presentation FPS.
Animation Hitches is unsupported on this simulator; the Time Profiler attempt
timed out. Physical-device performance remains unmeasured.

## Opal Set Timer Slider

Press the duration to expand or collapse; activation occurs on press-in and
release does not toggle again. Three native +/- taps with gaps ≤300ms also
expand; slower taps reset the count. Select 5m..60m in five-minute steps, then
2..24h. The 36 source markers retain an unselectable index 0. A 46pt spring shift
and 180ms presentation sequence reveal the slider in the final 36ms. Native
immediate contact drives spring fill and opposite-edge stretch; release/cancel
settles the deformation, and cancellation keeps the last touched duration.
Selection and expansion survive gallery switching and replay.

The empty black middle and bottom controls are intentional source composition.
SF Symbols replace source font icons. The existing static ambient Start Timer
glow approximates the composition; separate breathing/shimmer is not ported.
Start Timer, Block and the five icon controls explain their scope in a native
drawer: blocking, running sessions, schedules, screen-time data and accounts are
not connected. No private TSX or assets were copied; new captures and reference
media remain ignored local artifacts.

Existing `artifacts/opal/verified` contains 11 passing reports: `report.json`,
`live-report.json`, `quick-report.json`, `gallery-report.json`,
`gallery-live-report.json`, `swipe-report.json`, `slack-report.json`,
`slack-live-report.json`, `opal-report.json`, `opal-live-report.json` and
`opal-reduced-motion-report.json`. `artifacts/opal/preservation.json` records
17 previous-study PNGs byte-identical to the original checkpoint. The finish
review in `artifacts/opal/finish-review.md` found all ten captures valid and no
material UI defects; its FIX disposition asks only for documentation persistence.
These are existing results, not checks rerun here. Controlled native recognizer
callbacks verify behavior, not physical touch, haptics or VoiceOver traversal.
Broader-suite limitations remain: UIKit `SheetFlow.HiddenPresentation`, desktop
hidden-host worker wake and unrelated full-formatting import-order errors.
The latest full-format attempt also reports RS0016/RS0041 diagnostics in unchanged
files; separate Release builds and the public-API check pass. See `artifacts/opal/full-format.log`.

## Opal simulator frame pacing

The native Release 60Hz workload writes JSON/raw CSV under `artifacts/opal/performance`.
It measures compact idle, interrupted presentation toggles, continuous duration
sweeps, alternating edge overdrag, release/cancel and idle after interactions.
Screenshots run after measurement. The existing `opal-frame-pacing.json` records
approximately 60 callbacks/s across all six phases, 16.67ms p95/max intervals,
zero missed 60Hz slots and no callback arrival gaps over 50ms. CADisplayLink
measures simulator callback pacing, not GPU presentation FPS or device performance.
