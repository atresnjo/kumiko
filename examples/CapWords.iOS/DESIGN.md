---
name: Motion Studies for iOS
description: Four native C# studies — CapWords shared transitions, Alma circular motion, Slack Catch Up swipes and Opal timer controls.
colors:
  paper: "#f8f8f8"
  detail: "#f4f4f3"
  ink: "#333333"
  muted: "#6b7280"
  count: "#4b5563"
  dot: "#d0d0d0"
  orange: "#fb923c"
  warm: "#c5a282"
  white: "#ffffff"
  aperture-yellow: "#facc15"
  aperture-pink: "#f9a8d4"
  aperture-blue: "#93c5fd"
  aperture-green: "#86efac"
  aperture-purple: "#d8b4fe"
  gallery-paper: "#f1f1eb"
  gallery-ink: "#252b24"
  gallery-muted: "#687064"
  gallery-spark: "#78874e"
  gallery-word-surface: "#fffdf7"
  gallery-word-preview: "#f7f3e9"
  gallery-alma-surface: "#304b2a"
  gallery-alma-preview: "#dce6c5"
  gallery-alma-copy: "#f2f5e8"
  alma-paper: "#f7f5ed"
  alma-forest: "#3c5627"
  alma-ink: "#25291f"
  alma-card-ink: "#1b2118"
  gallery-slack-surface: "#30102f"
  gallery-slack-preview: "#542051"
  gallery-slack-copy: "#f4e8f3"
  slack-plum: "#401147"
  slack-background: "#20031f"
  slack-card: "#171717"
  slack-raised: "#262626"
  slack-border: "#303030"
  slack-text: "#e5e5e5"
  slack-muted: "#a3a3a3"
  slack-read: "#34d399"
  slack-unread: "#38bdf8"
  slack-action: "#064e3b"
  slack-action-border: "#065f46"
  slack-channel-text: "#d4d4d4"
  slack-placeholder-icon: "#737373"
  opal-black: "#000000"
  opal-control: "#252525"
  opal-border: "#404040"
  opal-muted: "#8a8a8a"
  opal-mint: "#99f6e4"
  opal-action: "#0e6659"
  gallery-opal-surface: "#102a24"
  gallery-opal-copy: "#e1f9f0"
typography:
  date: { fontFamily: "iOS system serif", fontSize: "30pt", fontWeight: 500 }
  count: { fontFamily: "iOS system font", fontSize: "16pt", fontWeight: 400 }
  detail: { fontFamily: "iOS system font", fontSize: "18pt", fontWeight: 400 }
  offer: { fontFamily: "iOS system font", fontSize: "12pt", fontWeight: 600 }
  action: { fontFamily: "iOS system font", fontSize: "16pt", fontWeight: 600 }
  notice: { fontFamily: "iOS system font", fontSize: "17pt", fontWeight: 400 }
  gallery-title: { fontFamily: "iOS system serif", fontSize: "36pt", fontWeight: 600 }
  gallery-title-narrow: { fontFamily: "iOS system serif", fontSize: "32pt", fontWeight: 600 }
  gallery-subtitle: { fontFamily: "iOS system font", fontSize: "15pt", fontWeight: 400 }
  gallery-card-title: { fontFamily: "iOS system serif", fontSize: "25pt", fontWeight: 600 }
  gallery-card-caption: { fontFamily: "iOS system font", fontSize: "13pt", fontWeight: 400 }
  gallery-credit: { fontFamily: "iOS system font", fontSize: "12pt", fontWeight: 400 }
  alma-wordmark: { fontFamily: "iOS system serif", fontSize: "25pt", fontWeight: 600 }
  alma-heading: { fontFamily: "iOS system serif", fontSize: "24pt", fontWeight: 400 }
  alma-card-title: { fontFamily: "iOS system font", fontSize: "24pt", fontWeight: 500 }
  alma-preview-title: { fontFamily: "iOS system font", fontSize: "15pt", fontWeight: 500 }
  alma-position: { fontFamily: "iOS system font", fontSize: "13pt", fontWeight: 500 }
  alma-action: { fontFamily: "iOS system font", fontSize: "18pt", fontWeight: 500 }
  slack-counter: { fontFamily: "iOS system font", fontSize: "17pt", fontWeight: 700 }
  slack-channel: { fontFamily: "iOS system font", fontSize: "16pt", fontWeight: 600 }
  slack-action: { fontFamily: "iOS system font", fontSize: "16pt", fontWeight: 600 }
  slack-completion: { fontFamily: "iOS system font", fontSize: "30pt", fontWeight: 700 }
  opal-wordmark: { fontFamily: "iOS system font", fontSize: "21pt", fontWeight: 500 }
  opal-duration: { fontFamily: "iOS system font", fontSize: "16pt", fontWeight: 500 }
rounded:
  dot: "1pt"
  offer: "22pt"
  examples: "28pt"
  aperture: "32pt"
  capture: "40pt"
  gallery-card: "26pt"
  alma-card: "36pt"
  alma-preview: "22pt"
  alma-continue: "19pt"
  slack-card: "24pt"
  slack-action: "16pt"
  opal-control: "16pt"
spacing:
  content-gap: "8pt"
  page-vertical: "12pt"
  navigation-inset: "16pt"
  heading-inset: "28pt"
  capture-bottom: "32pt"
  detail-top: "48pt"
  gallery-side: "20pt"
  gallery-gap: "18pt"
  alma-side: "24pt"
  slack-side: "20pt"
components:
  icon-control:
    width: "44pt"
    height: "44pt"
    textColor: "{colors.ink}"
  offer:
    backgroundColor: "{colors.warm}"
    textColor: "{colors.white}"
    typography: "{typography.offer}"
    rounded: "{rounded.offer}"
    height: "44pt"
    padding: "0pt 12pt"
  examples:
    backgroundColor: "{colors.white}"
    textColor: "{colors.orange}"
    typography: "{typography.action}"
    rounded: "{rounded.examples}"
    width: "236pt"
    height: "56pt"
  capture:
    backgroundColor: "{colors.white}"
    rounded: "{rounded.capture}"
    width: "80pt"
    height: "80pt"
  gallery-word-card:
    backgroundColor: "{colors.gallery-word-surface}"
    textColor: "{colors.gallery-ink}"
    rounded: "{rounded.gallery-card}"
    height: "252pt"
  gallery-alma-card:
    backgroundColor: "{colors.gallery-alma-surface}"
    textColor: "{colors.gallery-alma-copy}"
    rounded: "{rounded.gallery-card}"
    height: "252pt"
  alma-carousel-card:
    backgroundColor: "{colors.white}"
    textColor: "{colors.alma-card-ink}"
    typography: "{typography.alma-card-title}"
    rounded: "{rounded.alma-card}"
  alma-continue:
    backgroundColor: "{colors.alma-forest}"
    textColor: "{colors.white}"
    typography: "{typography.alma-action}"
    rounded: "{rounded.alma-continue}"
    height: "56pt"
  gallery-slack-card:
    backgroundColor: "{colors.gallery-slack-surface}"
    textColor: "{colors.gallery-slack-copy}"
    rounded: "{rounded.gallery-card}"
    height: "252pt"
  slack-channel-card:
    backgroundColor: "{colors.slack-card}"
    textColor: "{colors.slack-text}"
    rounded: "{rounded.slack-card}"
  slack-read-action:
    backgroundColor: "{colors.slack-action}"
    textColor: "{colors.slack-text}"
    typography: "{typography.slack-action}"
    rounded: "{rounded.slack-action}"
    height: "52pt"
  slack-unread-action:
    backgroundColor: "{colors.slack-card}"
    textColor: "{colors.slack-text}"
    typography: "{typography.slack-action}"
    rounded: "{rounded.slack-action}"
    height: "52pt"
  gallery-opal-card: { backgroundColor: "{colors.gallery-opal-surface}", textColor: "{colors.gallery-opal-copy}", rounded: "{rounded.gallery-card}", height: "252pt" }
  opal-duration: { backgroundColor: "{colors.white}", textColor: "{colors.opal-black}", typography: "{typography.opal-duration}", rounded: "{rounded.opal-control}", height: "40pt" }
---
# Design System: Motion Studies for iOS

## Overview

**Creative North Star: "Motion Studies on warm paper"**

One native example app presents four recognizable motion artifacts. The gallery's
warm paper, serif heading and generous preview cards frame the work; inside,
CapWords keeps its dotted paper and two word stickers, while Alma uses cream,
forest controls and white emoji cards moving around a circle. Slack Catch Up adds
a plum gradient and charcoal channel stack with emerald/sky swipe feedback. Opal
adds a black timer canvas, elastic track with white fill and mint/green action. The
supplied Experience direction, PRODUCT.md and built C# sources are authoritative.

Preserve the styling the user said looks good. This record expands the existing
CapWords system to include the built gallery, Alma, Slack and Opal; each retains its
study identity. Licensed Computer Mouse and Ice Cream Cone artwork remains an
external build input through `CapWordsAssetRoot`. Slack preserves the local reference
`/Users/backo/projects/personal/rn-makeitanimated/src/apps/(s-t)/slack`. Private reference source and
original artwork were not opened or copied by this documentation pass.

**Key Characteristics:**

- Warm paper and serif headings frame four artifact-led gallery cards.
- CapWords preserves transparent stickers, dotted paper and shared date/count identity.
- Alma combines cream, forest, native emoji and translucent geometric decoration.
- Circular position, rotation and scale express selection; dragging follows the finger.
- Slack layers scrollable channel cards, directional feedback and reversible native motion.
- Opal preserves the empty black middle and bottom timer hierarchy with anchored elastic contact.
- Native controls remain reachable; child state survives returning to the gallery.
- Centered content uses a continuous width cap across phone, narrow and tablet hosts.

### Review evidence and ship verdict

**Historical disposition: SHIP for the scoped gallery, CapWords, Alma and Slack motion studies.**
The earlier Slack finish review `slack-finish-review-1` reports eight valid captures,
no material findings and preservation of TYPE/MATERIAL. The gallery is intentionally
scrolled in some captures to expose the third card; this is not a framing defect.
The earlier finish review `alma-gallery-finish-2` reports eight valid captures and
no material findings. Preserve the accepted styling. This is a scoped visual and
behavior verdict, not a claim that all repository suites or device checks pass.

The earlier eight gallery/Alma review artifacts, relative to this example directory, are:

- `.impeccable/review/gallery-phone.png`
- `.impeccable/review/gallery-narrow.png`
- `.impeccable/review/gallery-tablet.png`
- `.impeccable/review/alma-phone.png`
- `.impeccable/review/alma-narrow.png`
- `.impeccable/review/alma-tablet.png`
- `.impeccable/review/alma-finger-drag.png`
- `.impeccable/review/alma-swipe-settled.png`

Earlier CapWords captures remain `phone.png`, `phone-detail.png`, `narrow.png`,
`tablet.png` and `tablet-detail.png` in that review directory. These are native
content captures, not evidence of whole-device chrome or hardware interaction.

Repository-root evidence, read without rerunning builds or application tests:

- `artifacts/capwords/report.json`: passing detached UIKit flow, both words,
  phone/narrow/tablet, Back pixel parity and model/pixel replay.
- `artifacts/capwords/live-report.json`: passing shared image/header compositor
  motion, Gentle duration, arrival staggering, intermediate presentation, rapid
  Back, resize cancellation and second word.
- `artifacts/capwords/quick-report.json`: passing Quick-preset endpoint probe;
  error 0.0431pt at 162.77ms within its 200ms duration.
- `artifacts/capwords/detached.log`, `pressed.png` and `released.png`: visible
  composed-button feedback and exact restoration after release.
- `artifacts/capwords/gallery-report.json`: both routes, card hit testing,
  bounded wrapping, timer removal on exit, retained selection, three host sizes
  and repeatable pixel replay.
- `artifacts/capwords/gallery-live-report.json`: mapped child timer wake,
  circular compositor, rotation, pause and return to the gallery.
- `artifacts/capwords/swipe-report.json`: direct tracking, velocity snap,
  presentation interruption, cancellation and completed-swipe autoplay pause.
  The final rerun also passed every header/footer native hit-testing assertion
  in `tests/Kumiko.CapWords.iOS.E2E/CarouselSwipeFlow.cs`: Back, pause/play,
  Previous, Next and Continue. Those assertions have no separate JSON field.

The Slack reports `artifacts/capwords/slack-report.json` and `slack-live-report.json`
pass: five bounded cards, gallery hit testing, read/duplicate guard, Undo/completion,
retained state, three host sizes/pixel replay; native pan/scroll arbitration,
two-axis tracking, anchor tilt, stack progress, cancellation, spring interruption,
right Read/left Unread, vertical momentum, counter flip, in-flight Undo, short return
and resize. These tests use a controlled UIPanGestureRecognizer subclass invoking
the exported native handler, not hardware input. Earlier CapWords/Alma reports pass.

The eight Slack finish captures are under `.impeccable/review/slack/`:
`slack-phone.png`, `slack-narrow.png`, `slack-tablet.png`, `slack-complete.png`,
`slack-drag-right.png`, `gallery-phone.png`, `gallery-slack-phone.png` and
`gallery-tablet.png`. Validity and finish disposition come from the supplied review.

Earlier supplied verification: solution build zero warnings/errors (a concurrent
intermediate-artifact race passed on serialized retry); public API check `--ios`
and changed-source whitespace checks pass. The simulator gallery was running at that checkpoint.
This documentation pass reads existing evidence and does not rerun those checks.

The historical signed Release NativeAOT Slack build has zero warnings, a 5,514,316-byte
app bundle and was installed on iPhone Adnan. Launch was blocked by the locked device,
awaiting the user's unlock; Slack was not confirmed running on the phone at that checkpoint.
Earlier successful Alma launch/process evidence predates the Slack update. Existing
aggregate `verification.json`, `phone-nativeaot-report.json` and launch files must
not override that newer handoff status.

The prior desktop E2E rerun failed at the existing `KumikoChecks` assertion
"worker messages wake a hidden native host through App.Dispatch"; investigation
is pending. The earlier 6,779-check pass is historical, not the current rerun
result. The broader UIKit suite separately stops at the unrelated
`SheetFlow.HiddenPresentation` hidden-controller dismissal timeout. Full repository
formatting previously reported unrelated import-order issues. Neither broader suite
was rerun for this iOS-only slice. Do not report a current all-suite pass.

Physical touch, tactile haptics, VoiceOver traversal, dark appearance and enlarged
Dynamic Type remain unverified. No native design detector was used. PRODUCT.md's
legacy-schema refresh is outside this documentation scope.

Current Opal evidence: `artifacts/opal/finish-review.md` records **FIX for documentation persistence only**: ten valid captures under `.impeccable/review/opal/`, no material UI defects.
`artifacts/opal/verified` has 11 passing all-study reports (listed in PORTING.md);
`artifacts/opal/preservation.json` records 17 previous-study PNGs byte-identical to the checkpoint.
Controlled native recognizer callbacks verify contact/compositor behavior, not physical touch.
The supplied latest solution build passed with zero warnings; `artifacts/opal/nativeaot-report.json`
records unsigned Release ios-arm64 NativeAOT, zero warnings, no managed DLLs, 5,733,619 bytes,
not installed on a phone. These are existing results, not checks rerun by this documentation pass.
The final simulator target is `3BF2ABBE-0D3B-44B8-A033-E53751FDE410`, with Opal as its launch route.
Frame-pacing scope/results and reproduction are in README.md and PORTING.md; broader-suite limits above remain.

## Colors

Frontmatter owns the implemented color values. CapWords colors come from
`Content.cs`, `View.cs` and `View.Background.cs`; gallery and Alma colors come
from `Gallery.View.cs`, `Alma.View.cs` and the native carousel card implementation.
Slack tokens come from `Slack.View.cs`, `Slack.Chat.cs` and the native swipe surfaces.
Opal tokens come from `Opal.View.cs`, `Gallery.View.cs` and the native rubber slider.

### Primary

- **Locked-example Orange** (`orange`): CapWords lock and View Examples label.
- **Alma Forest** (`alma-forest`): Alma wordmark, symbols, position and Continue.
- **Read Emerald / Keep Unread Sky** (`slack-read`, `slack-unread`): right/left
  feedback respectively, with progress ring and threshold-filled icon disc.

### Secondary

- **Warm Offer Tan** (`warm`): CapWords offer pill with white text and symbols.
- **Aperture Pastels** (`aperture-*`): five segments inside the camera control.
- **Gallery Olive Spark** (`gallery-spark`): the quiet sparkle beside the heading.
- **Gallery Forest / Leaf / Pale Copy** (`gallery-alma-*`): Alma's dark gallery
  footer, green preview and light title/caption/arrow.
- **Alma Motifs:** translucent peach/orange and green square, circle and capsule
  shapes behind the selected white card; motif-specific RGBA stays in the sidecar.
- **Slack Plum** (`slack-plum`, `slack-background`): top-to-bottom study gradient.
- **Gallery Plum / Preview / Pale Copy** (`gallery-slack-*`): Slack footer, gradient
  start and title/caption/arrow; preview ends at `slack-background`.
- **Slack Action Green** (`slack-action`, `slack-action-border`): Read and Done.
- **Opal Mint / Green** (`opal-mint`, `opal-action`, `gallery-opal-*`): ambient action and gallery footer/copy.

### Neutral

- **Light / Detail Paper** (`paper`, `detail`): CapWords dotted collection/plain detail.
- **Charcoal / Quiet Gray / Count Slate** (`ink`, `muted`, `count`): CapWords hierarchy.
- **Grid Gray** (`dot`): regular paper dots.
- **Sticker White** (`white`): controls, text on filled actions and Alma card faces.
- **Gallery Paper / Ink / Muted** (`gallery-paper`, `gallery-ink`, `gallery-muted`):
  warm canvas, heading and supporting copy.
- **Word Card / Preview Paper** (`gallery-word-surface`, `gallery-word-preview`):
  separate caption and illustration fields in the CapWords gallery card.
- **Alma Cream / Ink / Card Ink** (`alma-paper`, `alma-ink`, `alma-card-ink`):
  canvas, serif heading and nutrient labels.
- **Slack Charcoal / Raised / Border** (`slack-card`, `slack-raised`, `slack-border`):
  channel face, skeleton avatars/bars and quiet outlines. Light/muted/channel copy
  and composer symbols use their corresponding `slack-*` text tokens.
- **Opal Black / Charcoal / Gray** (`opal-black`, `opal-control`, `opal-border`, `opal-muted`): canvas, controls, ticks and quiet icons; white duration/fill.

**The Explicit Tint Rule.** Set SF Symbol tints at their call sites: charcoal for
CapWords navigation and pronunciation, white for its offer, orange for its lock,
forest for Alma controls, and the matching gallery footer ink for opening arrows.

## Typography

**Display Font:** iOS system serif through `NativeFontDesign.Serif`.
**Body Font:** iOS system font, scaled through native metrics.

The CapWords date is now serif, as are the gallery heading, gallery card titles,
Alma wordmark and Alma headline. The gallery title uses its narrow role below
360pt host width. Native font descriptors serve both measurement and rendering;
the old missing-design-selector claim is obsolete.

CapWords retains its date/count/detail/offer/action hierarchy. Alma's two-line
heading is regular serif; card labels are medium system sans, up to two lines,
with native fit-to-width down to 70%. Emoji uses native glyph rendering at 40pt,
or 28pt in previews. Position and action labels remain quiet sans companions.

**Accepted gap:** custom bundled font-family selection remains unsupported.
Alma uses system serif as a substitute for the reference's Libre Baskerville;
do not claim the exact family is bundled or rendered. Sticker lettering remains
embedded artwork. Native metrics do not establish enlarged Dynamic Type coverage. Slack uses system
sans: bold remaining count/completion and semibold channel/footer labels. Skeleton
rows are intentional synthetic content, not an unresolved loading state.

## Layout

All dimensions are UIKit logical points. The controller hosts content within its
safe-area frame. Gallery, CapWords, Alma, Slack and Opal content use a centered 600pt maximum
width, a continuous cap rather than a tablet breakpoint. Alma's orbit spans the
host canvas separately from its capped heading and controls.

The gallery is a vertical scroll surface with padding top/right/bottom/left
24/20/28/20pt and an 18pt gap. Each full-width study card is 252pt tall with a
156pt preview and a flexible footer; footer padding is 14/20/16/20pt. The preview
shows word illustrations, inert Alma orbit, Slack stack or Opal timer strip. Gallery
copy and opening arrows sit below each artifact; the footer credit closes the page.

CapWords keeps two half-width square cells, stickers at 35% of capped width,
a 56pt collection navigation row, and the date/count below it. Detail uses a
76pt navigation row and artwork at 50% of content width, at most 300pt, below
the detail-top space. The collection grid spans the host. Flexible space holds
capture and examples near the bottom; examples has 16pt bottom spacing. Name
has 4pt following space, with the content gap before the phonetic row.

Alma content has 12pt vertical and 24pt horizontal padding. Its header is 48pt
high; the heading begins 26pt below it. Flexible space separates the headline
from Previous / position / Next and the 56pt Continue button. The control row
has a 16pt gap and 16pt bottom margin; position reserves 90pt width.

Full Alma cards are `min(360pt, 60% of orbit width)` wide; preview cards are
`min(200pt, 52% of orbit width)`. Height is 70% of card width. Radius is half the
orbit height; the selected card is centered halfway down the orbit. Neighboring
cards follow the circle, rotate and shrink, with distant cards hidden. Narrow
and tablet captures preserve this composition; no alternate landscape layout
or exhaustive enlarged-text layout is established.

Standalone controls retain at least 44×44pt targets. Only the orbit's central
26–81% vertical band intercepts input; header/footer controls keep native hit
testing above and below it. Gallery preview carousels are inert.

Slack uses padding 12/20/8/20pt, a 12pt gap, 44pt header, flexible deck with
12pt bottom margin and 52pt footer (20pt gap between actions). Channel headers
are 48pt, messages scroll vertically, and the inert composer is 70pt. Each of
20 skeleton rows has 20pt padding, 16pt gap and a 48pt avatar. The preview deck
starts at 32/20pt, width `min(host width - 104pt, 320pt)`, height 190pt; its gradient
clips overflowing children internally, preserving the 156pt gallery preview.

## Elevation & Depth

CapWords adds no drop shadows or blur. Its baked white sticker edge sits directly
on paper; white pills and the plain detail surface provide separation. Gallery
cards use tonal preview/footer fields and clipping, without added shadows.
Alma's white card faces have a restrained black shadow (opacity 0.06, radius
14pt, offset 0/8pt); translucent motifs sit behind, with selection controlling
their opacity and scale. Native bottom drawers keep their system treatment.

Slack depth comes from charcoal/raised tones and native stack parallax, with no
added card shadow: active scale 1, two visible undercards at 0.95 with vertical
offsets 7% and 1% of deck width. Other cards remain bounded and transparent.

## Shapes

CapWords offer/examples controls remain full pills and capture remains circular.
The clipped aperture is 64pt across with a 40pt white center and five overlapping
segments. Dots are 2×2pt at 20pt intervals. Word stickers have transparent
silhouettes without an added card border, fill or shadow.

Gallery cards clip to broad rounded corners. Alma cards use continuous rounded
corners, smaller in previews; Continue has its own softer rectangle. The square,
circle and rotated capsule form a playful geometric halo behind selected cards.
Keep these native shapes and emoji, rather than replacing the accepted imagery.

Slack channel cards and composer use 24pt corners; footer actions use 16pt.
Avatars and skeleton bars use rounded native geometry. Directional gradients
and white progress rings overlay the clipped card rather than replacing its UI.

## Components

### Buttons

Composed `NativePressable` scopes are native UIButtons with semantic labels.
Pressed alpha is 0.65, enabled rest alpha 1, disabled alpha 0.4; feedback dims
contained artwork, text and symbols without scaling shared transforms. No web
hover/focus treatment is established. CapWords uses gift/chevron (16/14pt),
lock (20pt) and navigation/pronunciation symbols (24pt). Alma header/footer icon
targets are 44pt; Continue is a forest filled action opening a demo-scope notice.

### Cards / Containers

Gallery cards are single native pressables named Open CapWords, Open Alma,
Open Slack Catch Up and Open Opal,
with composed preview, serif title, one-line caption and opening arrow. Each
opens its child study in this app. CapWords' two transparent stickers remain
individually pressable, with image identity shared between collection/detail.
Selection and detail Back request optional native selection haptics visibly.

### Navigation

Gallery Back from any study returns to the four-card home. All child models
are retained; returning to Alma restores its selection/playback state. CapWords
detail Back returns to its collection. Navigation is model-driven replacement,
without a native navigation stack, interactive edge-swipe pop or automatic route
restoration. Alma's finger swipe changes nutrients, not navigation routes.

### CapWords shared transition and detail arrivals

Image, serif date and count wrappers use `SharedLayout` with Gentle (550ms)
native position/scale springs. Preset coefficients scale with advertised duration
while preserving damping ratio. A rapid reversal samples presentation geometry;
resize cancels obsolete motion. Final layout owns hit targets. Name,
pronunciation and examples enter from Y=50pt and scale=0.8 with 100/200/300ms
delays, using eased transform/opacity animation separate from the shared spring.

**Accepted gaps:** no whole-destination 200ms crossfade, retained departing screen
or general UIKit `Exit`. Public `NativePresentation` with `NativePose`/`NativeSpring`
now provides subtree transform springs and fades; arbitrary velocity handoff, general gesture graphs,
keyframe/progress APIs and a cross-clipping overlay remain absent. Desktop eased
motion is not evidence of UIKit spring parity.

### Alma circular carousel

Eight native card views hold the eight nutrients; wrapping does not grow the
view set. Initial selection is Support energy (2 / 8). Position, rotation, scale,
opacity and decoration focus follow one circular pose. The centered card is full
scale, neighbors shrink to 75%; angular spacing is `2π / 9.5`. The native
compositor samples the spring (mass 5.5, stiffness 300, damping 54) over 1.9s.
There is no polling render loop.

Autoplay advances every 2500ms only while Alma is the active screen, playing and
without its notice. Pause/play and Previous/Next remain native alternatives.
Previous/Next wrap selection without independently changing playback. Opening
the notice suspends the timer; exiting Alma removes it while retaining child
state. Returning with playing enabled reinstalls it. Inert gallery previews do
not schedule playback or intercept the gallery card's input.

Horizontal pan begins only when horizontal velocity exceeds vertical velocity.
The drag starts from the actual presentation position, clears competing native
animations and follows translation directly around the circle, including scale,
rotation and focus. Release projects velocity into a snapped selection and
carries velocity into the native spring. Re-grabbing interrupts at the shown
position. Cancellation returns to committed selection without a selection
message; a completed swipe sends selection and pauses autoplay, including a
short fast flick. Optional selection haptics fire on a changed selection in a
visible host. Cards, gesture recognizer and native animations have explicit
teardown. Reduce Motion and hidden/detached hosts apply final poses immediately.

The orbit exposes the selected title and position to accessibility, with hidden
nonselected card elements. These semantics and alternate buttons are implemented;
VoiceOver traversal and physical touch feel are not verified.

### Slack Catch Up compositional swipe deck

`NativeSwipeDeck` / `NativeSwipeCard` hold ordinary Kumiko UI and native vertical
scroll content, preserving real child controls within each compositional surface.
Five synthetic channels start with the last (`channel-5`, shown as `#channel-5`)
on top. The application owns Read/Unread dispositions, busy/revision guards and
retained child state; Ui messages persist business state while the GPU/native
compositor owns transient poses. No polling render loop is added.

Horizontal velocity must exceed vertical to begin; the recognizer permits the
native UIScrollView alongside it. Pan follows both axes. The commit threshold is
one quarter of window width (deck width fallback); tilt reaches 4 degrees there,
with sign reversed for a touch starting in the bottom half. Right commits Read,
left Keep Unread; short/cancelled swipes return. Release carries vertical velocity.
A directional gradient and progress ring fill toward threshold as undercards
shift/scale. Threshold crossing requests light native haptics; feel is unverified.

Footer dismiss uses 500ms ease-out, gesture fling 300ms. Short return is critically
damped (stiffness 900, damping 60, mass 1), sampled across 650ms. Undo uses
1000/90, mass 1, across 650ms and can reverse an in-flight dismissal from the actual
presentation pose. The remaining count flips in direction-aware 3D with opacity,
1000/90 springs and 0.8s settling. Reduce Motion, hidden hosts and resize settle
native poses immediately. Specialized deck motion does not establish general APIs.

Undo remains available after all five cards are completed; Done returns to the
gallery. Reopening retains Slack state. The completion copy reads “Boom. You're up
to date.” with an emoji and green Done action. Slack account/composer integration
is disconnected, matching the upstream placeholder. Gallery title “Slack”, caption
“A little swipe. A clear head.” and opening arrow use pale plum copy; the native
preview is inert, with skeleton cards and no gesture interception.

### Opal Set Timer Slider

Preserve the black safe-area canvas, empty middle, restrained system sans, bottom
44pt timer row, 58pt ambient green Start Timer and five icon controls. Content caps
at 600pt; value width is 22% of capped width, slider up to 71%. White duration and
charcoal alternating-tick track use 16pt corners; the gallery keeps its black preview,
deep green footer, pale mint copy and “A little time. Room to focus.” caption.
Duration toggles on press-in without duplicate release; three +/- taps ≤300ms apart
expand, slow gaps reset. Value shifts left 46pt; presentation fades span 180ms with
the slider revealed in the final 36ms. There are 36 source markers, index 0 excluded:
5m..60m by five, then 2..24h. Immediate contact drives 1600/140 spring fill; separate
1.03 active-scale and opposite-edge stretch layers reach 2.5×/0.9 over a 625pt domain,
returning with 900/60 edge springs. Interruption samples shown poses; resize/Reduce Motion settle.
SF Symbols replace source font icons; static ambient glow approximates Start Timer,
with separate breathing/shimmer unported. Native drawers disclose blocking/session/account/tab scope.

### Placeholder notices

Camera, offer, pronunciation, More and locked examples disclose demo scope in a
native bottom drawer; no camera, audio or billing service is connected. Alma
Continue explains that account setup and nutrition tracking are not connected.
Loading/missing-artwork errors use native text. CapWords dots still use individual
native box nodes; a reusable tiled background remains a rendering-cost gap.

## Do's and Don'ts

### Do:

- **Do** preserve the approved warm gallery, CapWords, Alma cream/forest, Slack plum and Opal black/mint styling.
- **Do** let each gallery card show its study's actual artifact before its caption.
- **Do** retain the 550ms shared spring and 100/200/300ms CapWords arrival delays.
- **Do** keep system serif headings/date and document the custom-family limitation accurately.
- **Do** keep Alma dragging direct, release velocity intact and interruption at the shown pose.
- **Do** pause autoplay after a completed swipe and remove Alma's timer on exit.
- **Do** retain child state, eight bounded Alma cards, native alternate controls and 44pt targets.
- **Do** preserve Slack TYPE/MATERIAL, directional colors, scroll content and reversible motion.
- **Do** preserve composed-button feedback, explicit symbol tints and optional haptics.
- **Do** keep licensed artwork external to the repository and reference it at build time.
- **Do** distinguish scoped simulator/offscreen evidence from physical and broader-suite verification.

### Don't:

- **Don't** restyle the accepted gallery, Alma, Slack or Opal as part of documenting it.
- **Don't** claim Libre Baskerville, interactive navigation pop or whole-screen crossfade is built.
- **Don't** generalize specialized velocity springs or the new subtree pose subset into arbitrary velocity handoff or a gesture graph.
- **Don't** promise connected camera, pronunciation, purchases, nutrition tracking, Slack accounts or composing.
- **Don't** copy or distribute private reference source or original artwork in this handoff.
- **Don't** infer hardware touch, tactile haptics, VoiceOver, dark or enlarged-text checks from captures.
- **Don't** infer a current Opal phone installation from historical device evidence; this extension is simulator-only.
- **Don't** report the historical desktop pass or stale aggregate status as the latest rerun result.
