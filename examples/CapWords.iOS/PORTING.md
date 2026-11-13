# What this port exercises

The four studies and their gallery form a normal `IKumiko` application: immutable model/messages, artwork
loaded by a subscription, Yoga layout, native controls and recorded replay.
There is no React Native, JavaScript animation engine or bespoke UIViewController
implementing the collection/detail experience. The controller only hosts Kumiko
inside the safe area.

## Framework support added

- UIKit previously rejected `SharedLayout`. Native controls now carry shared
  identity and logical parent metadata. The renderer samples the old presentation
  geometry before reconciliation and animates the destination subtree with native
  position/scale springs. Images and text use the same mechanism.
- A rapid reversal uses presentation-layer geometry rather than the old target.
  Resizing cancels obsolete geometry. Duplicate live shared tags fail explicitly.
- UIKit now supports `Enter` on native containers. `Transition.Delay` enables
  100/200/300 ms arrivals and also works on desktop transitions. The compositor
  owns UIKit motion, so the host does not add a polling render loop.
- `NativeSymbol` now accepts an explicit tint, allowing the reference's charcoal
  navigation and white offer icons without inheriting the system blue.
- `NativePressable` provides a message-bound native UIButton scope containing
  images, labels and symbols, including semantic naming and disabled state.
  Decorative labels/images do not intercept its touches. Pressed content dims
  without changing shared transforms; card selection and Back request native
  selection haptics. Spring physics scale to each preset's settling duration.
- `Sub.Map` composes child messages without changing subscription identity or
  cancellation. Leaving Alma removes its timer while preserving child state.
- `NativeFontDesign` adds system serif, rounded and monospaced labels with the
  same descriptor used for measurement and rendering. CapWords' date is now serif.
- `NativeCircularCarousel` owns a bounded set of native card views. Core Animation
  samples the reference mass/stiffness/damping along the circle. Its message-bound
  overload adds horizontal UIPanGestureRecognizer tracking, release velocity,
  interruption, cancellation and snapped selection. Gallery previews are inert.
- UIKit Reduce Motion is synchronized with the Kumiko context at render time.
  Hidden/detached hosts apply final geometry without native motion.

## Remaining differences and useful next slices

- **Navigation:** this is model-driven list/detail replacement, not a native
  UINavigationController stack. There is no interactive edge-swipe pop,
  navigation-driven progress, cancellation, or automatic route restoration.
- **Transition composition:** the reference fades its whole destination screen
  over 200 ms. This port shares the three key elements and staggers detail content;
  it does not retain and crossfade a whole departing native screen. Generic UIKit
  `Exit` remains explicitly unsupported.
- **Motion control:** named presets choose timings, with Gentle using 550 ms on
  UIKit. `NativePresentation` now exposes a reusable subtree transform/fade subset
  through `NativePose` and configurable unit-mass `NativeSpring`. It samples the
  shown pose on interruption; it does not expose arbitrary velocity handoff, a
  general gesture-value graph, native keyframe builder or progress binding. The
  circular carousel retains its specialized native gesture/velocity spring. Desktop motion retains
  its existing eased timing rather than reproducing the UIKit spring.
- **Typography:** native system serif now closes the design-selector gap.
  Custom bundled font-family selection, including Alma's exact Libre Baskerville,
  remains unsupported by NativeLabel.
- **Shared clipping:** motion stays inside the destination's native hierarchy.
  A general transition overlay is still needed for elements that must cross
  clipped scroll containers. This two-item reference does not require it.
- **Rendering cost:** dotted paper currently uses native box nodes. A reusable
  tiled background primitive would avoid a view per dot for large canvases.
- **Scope:** this is an iOS port. Kumiko's desktop shared-layout implementation
  remains available, but this example uses the UIKit adapter and is not an
  Android implementation. Camera, sound and billing are placeholders upstream.

The E2E suite covers card selection, Back, resizing, session/pixel replay,
compositor intermediates, shared headers, stagger timing, rapid reversal,
Quick-preset endpoint continuity and visible press/release feedback.
Gallery E2E checks cover both routes, hit testing, retained child state, timer
cancellation, bounded wrap, phone/narrow/tablet and pixel replay. Live checks
cover the mapped timer, circular compositor motion, rotation, pause and return.
Controlled native pan callback input covers direct tracking, velocity snap,
interruption, cancellation and swipe pausing; PNG/JSON artifacts are repeatable.
Physical touch feel, edge-swipe navigation and VoiceOver traversal are not covered.

## Wider verification limit

At the earlier three-study checkpoint, CapWords, gallery, live-compositor and native
swipe flows passed. The solution and signed phone Release NativeAOT build completed
without warnings; public API declarations matched. The earlier Alma build was
launched and confirmed running on iPhone Adnan.
The earlier three-study Slack build was installed, with launch blocked by the
phone lock; see `slack-nativeaot-report.json`. The performance-fix NativeAOT build
is signed and warning-free but not installed because iPhone Adnan is unavailable.
The user selected simulator-only verification. See `performance-fix-nativeaot-report.json`.

An earlier desktop run passed 6,779 checks, but the latest two desktop runs stop
at `KumikoChecks`' existing worker-message/hidden-host wake assertion. This check
does not exercise the added `Sub.Map`; the gallery E2E does exercise mapped
subscriptions. These reruns are recorded as failures, not a clean desktop pass.
See `artifacts/capwords/desktop-e2e.log` and `desktop-first-run.log`.

The broader UIKit suite currently stops at `SheetFlow.HiddenPresentation` while
waiting for hidden-controller dismissal. Its earlier button, component, flexbox
and Focus slices complete; the entire suite must not be reported as passing.
See `artifacts/capwords/native-suite.log`. Full repository formatting also reports
import-order issues outside this port; changed-source whitespace checks pass.

## Slack Catch Up capability slice

`NativeSwipeDeck` accepts stable card IDs and a selected index, and returns
committed selections through app messages. Its `NativeSwipeCard` scopes contain
ordinary Kumiko Yoga layout and native controls, including vertical scroll views;
the renderer reconciles their compositor poses after placing content. Horizontal
pans arbitrate with the native vertical scroller. Native Core Animation owns
return springs, flings, stack scale/position, reverse Undo and resize cancellation.
The app owns read/unread dispositions, progress, busy guards and completion.

`NativeLinearGradient` adds reusable clipped two-color native surfaces.
`NativeFlipCounter` adds direction-aware native 3D counter feedback. Directional
progress arcs use Core Graphics instead of a Skia dependency. These are focused
native components. Opal adds configurable subtree pose springs; a general public
gesture-value graph, arbitrary velocity handoff, reusable path animation and shader surfaces remain future work.
Slack composing/account data are not connected in this study.

Controlled native recognizer input can verify callback and compositor behavior,
but does not establish physical swipe feel, haptic feel or VoiceOver traversal.

## Slack responsiveness measurement

The original clean Release simulator run held 60 callbacks/s while idle and
continuously dragging, but averaged 41.9 during swipe commits, 49.2 during footer
commits and 50.9 during Undo. Its longest UI callback gap was 113 ms.
`artifacts/capwords/performance/slack-frame-pacing.json` preserves that baseline.

Instrumentation isolated repeated native appearance/frame/order updates and
unconditional native text-layout invalidation at model changes. The renderer now
retains unchanged native appearance and logical geometry. UIKit child-layout
notifications no longer rebuild an unchanged host; size, traits, Reduce Motion,
model/input wakes and explicit renders still invalidate it. Native text caches
invalidate on text, font design or font-trait changes. Font construction uses the
active trait collection. Stateful controls still reconcile native edits, enabled
state and modal isolation. The deck omits keyframes for constant animation channels.

The updated workload is recorded in `artifacts/capwords/performance-fixed`. Its JSON and raw CSV measure idle,
dragging, cancel/return, swipe/footer commits and Undo without screenshot work
inside the measurement window. The original instant begin/end gesture burst is
preserved for before/after comparison. A paced run in `artifacts/capwords/performance-paced`
also approaches the threshold over 250 ms of display-link-driven pan updates;
its results are a different workload, not a replacement baseline.
Repeatable text-layout evidence also exercises
content edits, font design, Dynamic Type and host-driven resizing.

The final original-workload run measures 59.9 callbacks/s during continuous drag,
56.7 during swipe commits, 59.2 during footer commits and 59.3 during Undo.
Its maximum frame interval is 41.7 ms, compared with the baseline's 113 ms;
callback arrival gaps over 50 ms fall from 33 to zero. This is substantially
improved responsiveness, not a perfect 60 FPS result: occasional missed callbacks
remain, with swipe-commit p95 at 25.0 ms. The other phases have p95 of 16.7 ms.
See `artifacts/capwords/performance-comparison.json`. All seven focused behavior
and compositor reports pass after these changes; 17 still PNGs match the previous
captures byte for byte. The broader UIKit suite still stops at its previously
observed `SheetFlow.HiddenPresentation` failure.

CADisplayLink reports UI callback/frame pacing, not render-server/GPU presentation
FPS. Animation Hitches is unsupported on this simulator; the earlier Time Profiler
attempt timed out. Physical-device performance remains unmeasured. The scoped
design/behavior SHIP verdict is separate from performance measurements.

## Opal Set Timer Slider capability slice

`OpalModel` and messages retain duration, expansion, rapid-tap timing and notices;
gallery routing retains the child state and recorded replay. The fixed native
controls implement this interaction; the app controller only hosts Kumiko.
Timed `NativePressable` receives a native monotonic timestamp. Duration toggles
on press-in and suppresses duplicate release activation. Three +/- taps whose
successive gaps are at most 300ms expand the strip; a slower gap resets the count.

`NativePresentation` / `NativePose` / `NativeSpring` animate ordinary UIKit
subtrees independently of Yoga frames, retarget from presentation transforms
and opacity, and preserve transformed native hit testing. Opal shifts the value
46pt left with a 1400/90 spring; other presentation poses use 1200/90. Retained
spring duration is 650ms. Fades span 180ms; the slider waits 144ms, then fades
over the final 36ms. Reduce Motion and resize settle obsolete geometry.

`NativeRubberSlider` is discrete and contacts immediately through a zero-duration
native long-press recognizer. Its 36 source markers represent 0..60 minutes by
five, then 2..24 hours; index 0 is unselectable (35 selectable values, 5m..24h).
Selection changes persist through messages; cancellation keeps the last touched
selection. Fill uses stiffness/damping 1600/140. Separate layers own active scale
(1.03) and edge stretch: a 625pt overdrag domain reaches 2.5× horizontal scale,
compresses vertically to 0.9 and anchors opposite the dragged edge. Edge return
uses 900/60; active-scale release uses 100/6. Recontact samples the shown pose.
Native accessibility increment/decrement adjusts the bounded selection.

UIKit-origin actions now accept native scrolling and transformed geometry while
retaining identity, enabled, hidden and modal checks. Public agent actions keep
desktop semantic visibility checks. Desktop semantic scroll-offset synchronization
remains a gap; the native action fix does not close it. Pose continuity is built,
but arbitrary velocity handoff and a general gesture graph remain absent.

SF Symbols replace source font icons. Start Timer uses the existing static native
ambient glow; separate breathing/shimmer is not ported. Blocking, running sessions,
accounts and tab destinations disclose scope in a native drawer. No private TSX
or assets were copied; reference media and new captures are ignored artifacts.

## Current Opal evidence

Existing `artifacts/opal/verified` contains 11 passing JSON reports: `report`,
`live-report`, `quick-report`, `gallery-report`, `gallery-live-report`,
`swipe-report`, `slack-report`, `slack-live-report`, `opal-report`,
`opal-live-report` and `opal-reduced-motion-report`. They cover all four studies,
including native Opal contact, rapid/slow taps, bounds, press-in/release deduplication,
reversal, both edge anchors, release/cancel, interruption, resize, accessible
adjustment, reachable controls, retained state, replay and Reduce Motion.
Input uses controlled native recognizer callbacks, not physical touch.
`artifacts/opal/preservation.json` records 17 previous-study PNGs byte-identical
to the original checkpoint.

`artifacts/opal/finish-review.md` records FIX for documentation persistence only:
all ten captures valid, no material UI defects. The black middle, bottom controls
and transient overdrag occlusion are intentional. Earlier reviews remain historical.
The supplied latest solution build passed with zero warnings. The current
`artifacts/opal/nativeaot-report.json` records a passing unsigned Release
`ios-arm64` NativeAOT build: zero warnings, no managed DLLs, 5,733,619-byte bundle.
This is local validation only; it is not installed on a phone. None of these
checks were rerun by this documentation pass. The broader UIKit hidden-sheet,
desktop hidden-host worker-wake and unrelated formatting limitations above remain.
The current full-format attempt reports IMPORTS, RS0016 and RS0041 diagnostics,
all in unchanged files; serialized builds and the public-API check pass.
See `artifacts/opal/full-format.log`; this is not a clean repository-format verdict.

`artifacts/opal/performance/opal-frame-pacing.json` and raw CSV record it. The existing native
Release 60Hz CADisplayLink run records approximately 60 callbacks/s in all six
phases: compact idle, presentation toggle, continuous drag, edge stretch, release
return and idle after interactions. Each phase has 16.67ms p95/max intervals,
zero missed 60Hz slots and zero callback arrival gaps over 50ms. Screenshots run
after measurement. This measures simulator callback pacing, not GPU presentation
FPS. Physical touch, haptics, VoiceOver traversal and device performance remain unverified.
