---
name: Kumiko Focus for iOS
description: Portrait UIKit reproduction of the supplied focus-session reference.
colors:
  background: "#101615"
  surface: "#191f1f"
  raised: "#252e2b"
  text: "#e4eee7"
  muted: "#a2b0aa"
  accent: "#b9e3cc"
  warm: "#e9bd78"
  border: "#35423d"
typography:
  countdown: { fontFamily: "iOS system font", fontSize: "56pt", fontWeight: 300 }
  action: { fontFamily: "iOS system font", fontSize: "18pt", fontWeight: 400 }
  tab-label: { fontFamily: "iOS system font", fontSize: "11pt", fontWeight: 400 }
rounded:
  compact: "11pt"
  action: "18pt"
  navigation: "24pt"
  drawer: "28pt"
spacing:
  row-gap: "12pt"
  drawer-padding: "20pt"
  page-inset: "22pt"
---
# Design System: Kumiko Focus for iOS

## Overview
The supplied portrait reference is the visual authority. Preserve its charcoal surfaces, mint accents, quiet countdown, compact controls and floating five-tab bar. This documents the built example, not a new identity or framework-wide theme. UIKit draws the UI; SF Symbols supply icons.

## Colors
`NativePalette.FocusDark` owns the exact palette above. Mint marks selection, progress, connection and Start illumination. Amber identifies rest intervals and the duration dot. Neutral layers separate page, surfaces, controls, text and outlines.

## Typography
Native system fonts use `UIFontMetrics` and Dynamic Type. Baseline points: countdown 56 light; section title 28 semibold; drawer title 22 semibold; identity 17 semibold; body 15–17; detail 13; connection 12; timeline/tab/status 11. Status is uppercase; selected tabs and Start are semibold. Action captions fit down to 0.65; countdown fits down to 0.6. Scaling does not establish unlimited large-text layout coverage.

## Layout
Portrait content adds 22pt horizontal and 8pt top padding; navigation spans the host width so UIKit supplies its own floating-bar margins. Header height is 44pt with 16pt following space. Centered Now fills the padded parent up to 420pt using `FillWidth`, `MaxWidth` and `AlignSelf`; dial diameter is `min(264, column width - 20, max(160, available height - 340))`. Flexible space keeps navigation low. A system UITabBar uses its SizeThatFits height (83pt on the iPhone 15 Pro simulator), capped at 600pt wide. UIKit lays out its items; never force the native bar into a shorter custom height.
System sheets use UISheetPresentationController. Duration/connection request an initial 390pt detent; blocking requests 480pt. All offer the native large detent. UIKit owns presented sizing, bottom safe-area insets, corner treatment, grabber and dimming. Content padding is 20pt; checklist content scrolls. Detached previews are bounded by the host. Narrow-phone and tablet bounds are verified; there is no separate landscape composition.

## Elevation & Depth
Use tonal layering, 0.75pt outlines and clipped radial illumination without drop shadows or blur. Page light is mint at 0.07 alpha. Start light is mint at 0.9 alpha, radius 0.62 times width, vertically scaled to 0.38 from below the pill. The modal backdrop is black at 0.35 alpha.

## Shapes
Continuous corners define surfaces. Compact selectors inset their paint 6pt vertically; a 44pt target paints a 32pt-high shape. The mint connection control also insets 6pt horizontally with 9pt corners. Pause uses 14pt corners; UIKit owns the tab selection treatment; Start is a full pill.

## Components
- **Actions:** standalone targets retain at least 44×44pt. Duration is 106pt wide with amber dot/chevron; blocking uses mint shield/summary/chevron. Start grows from a zero flex basis to share a 52pt row with a 48pt-wide Pause and 10pt gap. Both controls fill their allocated containers; margins provide section spacing. Pressed alpha is 0.72; disabled alpha is 0.4. Running disables “Focusing”; pausing offers Resume.
- **Dial/day strip:** dial has a 12pt ring, 60 ticks and 4pt mint progress arc. The 20pt strip contains a 12pt painted track with 6pt corners, sample mint/amber intervals and a flag marker.
- **Navigation:** Now, Blocking, Activity, Health and Settings retain stable IDs. Each is a UITabBarItem with an SF Symbol and label. Selection uses native mint tint and native accessibility.
- **Drawers:** system grabber, title and 44×44pt Close. UIKit owns presentation, dragging between detents, scroll expansion and swipe dismissal. Dismissal dispatches to Kumiko state. Close uses native dismissal transitions. Duration offers 15/25/45/60 minutes as 48pt-high targets in a wrapping grid (140pt basis, 8pt gaps, 420pt width cap), one column on narrow phones and two on wider hosts; selection resets the session and dismisses. Blocking uses a themed multi-select UITableView.

## Do's and Don'ts
- **Do** preserve reference hierarchy, shared tokens, native scaling, SF Symbols and width caps. Disclose simulated Mac connection, history and blocking in Settings/connection copy.
- **Don't** substitute a flattened screenshot/web UI, invent an identity or motion, or imply real app/site blocking. Physical touch trajectories and VoiceOver traversal need separate coverage; detached checks exercise native sheet configuration and delegate state synchronization.
Evidence: `artifacts/ios-focus/focus-{phone,narrow,tablet,duration,blocking,running,replay}.png` and `focus-report.json`; native offscreen behavior and exact pixel replay passed. The earlier visual review preceded the switch to system sheets and tabs; current artifacts show the native controls. Detached captures exclude system status/home-indicator bars.
