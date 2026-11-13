# Kumiko Focus for iOS

A native UIKit focus-session example based on the supplied `image0` reference:
charcoal surfaces, mint accents, a large timer dial, a day activity strip,
compact selectors, an illuminated Start action and floating bottom navigation.

Build with the pinned SDK from the repository root:

```sh
dotnet build examples/Focus.iOS -c Release -r iossimulator-arm64
```

The example uses reusable components from `Kumiko.iOS`:

- `NativeSurface` and `NativeLabel` provide explicit native appearance tokens.
- `NativeAction` supports SF Symbols, compact selectors, tonal/icon buttons and
  an illuminated primary action. Compact visuals retain a 44-point hit target.
- `NativeTabBar` wraps `UITabBar` and `UITabBarItem` with 2–5 stable destinations.
  UIKit owns selection, layout, feedback and accessibility. The bar uses UIKit’s
  measured height and sits outside the page’s horizontal padding.
- `NativeTimerDial` renders ticks, a countdown, status text and a progress arc.
- `NativeTimeline` renders validated activity intervals and a current marker.
- `NativeCheckList` is a themed, scrolling UITableView with multiple selections.
- `NativeBottomDrawer` uses `UISheetPresentationController` with native detents,
  a grabber, dragging, scroll expansion, swipe dismissal and Close. Content belongs
  to the returned disposable scope. UIKit controls the presented sheet dimensions.

Layout uses the shared C# flex engine. The session fills its parent up to 420pt,
with `AlignSelf` centering it. Start uses `Basis(0).Grow().Shrink()` beside a fixed
Pause target; controls fill their allocated container instead of calculating
screen widths. Margins replace empty spacer nodes, and timeline labels share a
baseline. Duration choices use `Wrap`, independent row/column gaps and a 140pt
basis: one column on a narrow phone and two on a wider phone or tablet.

All components share `NativePalette`. The example uses `FocusDark`; callers may
supply their own immutable palette. State stays in the Kumiko model, and UIKit
callbacks dispatch through the existing native host. UIKit owns rasterization
and hit handling; there is no flattened screenshot or embedded web view.

Each tab is its own screen selected by a `NavigationHistory<FocusTab>` in the
model. A tab switch replaces the current visit, so tabs never leave a Back entry;
selecting the current tab keeps its visit. The background, header, tab bar and
drawer live in `FocusShell`, a navigation shell that stays mounted across visits;
each screen renders only its tab's content in the shell's outlet. The UITabBar,
its delegate and items therefore stay the same native objects across tab
switches, keeping VoiceOver focus and UIKit's selection animation. Tab selections and history requests
are ignored while a drawer is open, and stale or foreign requests change nothing.

Start, Pause, Resume, duration choices, tabs and blocking selection work locally.
A one-second subscription exists only while a session is running. Mac connection,
activity history and blocking targets are demo data; the app does not control or
block other apps, websites or devices. Settings explains these limits.

This portrait example runs full screen. The scene controller places the host in the safe area. Native system fonts scale
with Dynamic Type, and SF Symbols provide icons. UIKit supplies sheet presentation and dismissal transitions and honors Reduce
Motion. Physical touch trajectories and VoiceOver focus require separate coverage.
Detached tests verify the system controller configuration and native delegate paths.

The headless check retains `focus-*.png`, `focus-session.jsonl` and
`focus-report.json`. It verifies tab identity, duration changes, a live timer tick,
pause cancellation, multi-selection, modal isolation, narrow/tablet bounds and
exact native image replay. Duration choices retain their UIKit buttons during
reflow. `focus-*-frames.json` records the native action and duration-grid bounds. Captures are detached UIKit content layers; system
status and home-indicator bars are not recreated inside the app.
