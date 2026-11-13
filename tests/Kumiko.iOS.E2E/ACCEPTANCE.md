# UIKit button vertical slice

## Standard iOS component catalog

Failure scenarios recorded before implementation:

- Switch, slider, stepper, segmented control, wheel picker, date picker, search,
  text editor and page control render imitations instead of retained UIKit views.
- Native edits dispatch twice, overwrite pending input, use stale model bindings,
  select by an obsolete index after reordering, or accept invalid range values.
- Disabled, modal-covered, removed or disposed controls still dispatch queued
  edits, menu actions or bar actions; programmatic reconciliation generates edits.
- Search and multiline text lose editing state or marked IME text on unrelated
  model updates; native delegates or target/actions outlive their owning controls.
- Images fail to decode, decorative symbols are exposed to accessibility, or
  progress and activity indicators do not follow model state.
- Navigation/toolbar/menu actions lose stable identity, enabled state, labels,
  symbols or destructive styling when their items change.
- Phone, narrow, tablet and dark layouts overflow their scroll container, native
  views are replaced on resize, or replay changes the final visible controls.
- Fixed-width switch/stepper assumptions clip a newer iOS intrinsic size or
  leave the native control aligned against the top of its 44-point layout slot.

Drive actual UIKit events and
delegates in-process in a private headless simulator. Retain catalog-*.png,
catalog-session.jsonl and catalog-report.json. Captures may initialize native
layout in a private hidden window; layer-only captures do not reproduce every
iOS glass or wheel effect. Retain geometry TXT files alongside the PNGs.
No visible window, real-screen input,
system clipboard or user data is involved. These checks do not cover physical
gestures, keyboard presentation or VoiceOver traversal.

## Button behavior

Failure scenarios recorded before implementation:

- A button declaration produces painted text instead of a real UIButton.
- A primary action sends no Kumiko message, sends twice after reconciliation, or
  uses a stale message binding after the model changes.
- Coalescing frame requests drops a burst of primary actions before the next render.
- Updating a label or resizing replaces a keyed UIButton, loses its accessibility
  identity, or leaves its frame outside the available content bounds.
- Disabled controls or detached controls still dispatch messages.
- Removing and restoring a control leaks its target/action registration.
- A worker result cannot wake the native host, or wakes a disposed host.
- A second host takes over an already attached program.
- Native measurement loads desktop fonts or silently guesses button dimensions.
- Unsupported drawing primitives disappear silently in a native-controls view.
- Replaying the session fails to reproduce the final button label.

Run the actual UIKit controls in a dedicated headless iOS simulator process.
Send only in-process UIControl actions; never synthesize system input. No UIWindow
is shown and no Simulator application is opened. Render the detached view layer
into a bitmap, never capture the display. Keep before.png, after.png, replay.png,
session.jsonl and report.json in artifacts/ios-button. Repeat the same command
to reproduce the behavior. This does not verify real touch or VoiceOver gestures.

## Hot reload

Failure scenarios recorded before implementation:

- A real source edit reports success in dotnet watch but the detached UIKit view
  stays stale until input, or reconciliation runs on a worker thread.
- A view or update edit replaces a keyed UIButton, resets the model/history, or
  keeps invoking the old update delegate on subsequent primary actions.
- Only one of several native views refreshes, repeated edits stop working, or
  idle views continually rebuild between updates.
- Queued metadata updates render disposed views or the registry retains them.
- Release builds enable the interpreter or hot reload development transport.

The check edits disposable
source copies under the artifact directory and uses dotnet watch's SDK transport
to an executable spawned directly in a dedicated headless simulator. Keep
hot-before.png, hot-after.png, hot-final.png, hot-state.json, hot-report.json and
watch.log under artifacts/ios-hot-reload. No UIWindow or Simulator UI is shown.

## NativeAOT release comparison

Failure scenarios recorded before enabling NativeAOT:

- The native registrar or trimming removes the scene/controller, UIButton action,
  generated Kumiko serialization metadata, or replay data needed at runtime.
- NativeAOT builds produce compiler, trimming, or AOT warnings, or the shared
  button behavior differs from the Mono Release build.
- Size comparisons mix simulator/device architectures, Debug/Release settings,
  external symbol files, or stale output from another compilation mode.
- Editing the example's surrounding button wording makes the counter behavior
  checks fail, or accepting new wording stops the checks from catching stale counts.

Publish the button app separately with Mono and NativeAOT for the same RID and
settings. Run the existing detached UIKit behavior flow in both simulator modes.
Retain build logs, PNG/JSON behavior artifacts, bundle file-size inventories and
a machine-readable comparison. Compressed local bundles are not App Store sizes.

## Labels, lists and drawers

Failure scenarios recorded before implementation:

- Text loads desktop fonts, vanishes, or fails to wrap at a narrow width.
- A list selection sends twice, sends an index after reorder instead of the row's
  stable ID, or a disabled/deleted row still sends a message.
- Rebuilding a list replaces its UITableView or resets its scroll position.
- Empty lists cannot recover when an item is added; duplicate IDs are accepted.
- Opening a drawer leaves background buttons/list selections active, exposes
  background accessibility elements, or allows panel taps to dismiss it.
- Close and backdrop actions cannot dismiss/reopen the drawer, or use stale bindings.
- Resizing leaves the drawer outside the safe content area or clips its controls.
- Removing native containers leaves live children, callbacks or queued selections.
- Recorded add/select/close actions cannot replay to the same model and image.

Extend the existing windowless simulator command with the actual Components.iOS
example. Retain components-{phone,drawer,narrow,tablet,dark,empty,live,replay}.png,
components-session.jsonl and components-report.json beside the button artifacts.
Use native in-process actions and table delegate selection, never system input.

The standalone example must build with strict Kumiko analysis enabled. Failure
scenarios before extending the analyzer's framework boundary: immutable list-row
records from Kumiko.iOS are rejected as unknown effects, or message-bound native
components are rejected in a view; accepting those components must not permit
native UI access from Update or effects. Keep the existing analyzer E2E checks.

## Reference focus UI and reusable mobile components

Failure scenarios recorded before implementation:

- The focus view is a flattened screenshot rather than reusable UIKit components.
- Floating tab items lose their stable identity, report the wrong destination,
  truncate at narrow widths, or remain interactive under the bottom drawer.
- A duration selection closes the drawer without changing the countdown; Start,
  Pause and Resume diverge from the model, or the session timer keeps running idle.
- The blocking summary does not reflect list changes; demo controls imply real
  device blocking or a Mac connection that the example does not implement.
- Drawer content is square, clipped, lacks a dismiss action, or its backdrop
  dismisses when a button inside the panel is pressed.
- Timer ticks, timeline segments, large numerals or bottom navigation overflow
  phone/tablet safe bounds, or native drawing exceptions escape callbacks.
- Message replay loses selected tabs, duration, blocking selections or session state.

Retain focus-{phone,narrow,tablet,running,duration,blocking,replay}.png plus
focus-session.jsonl and focus-report.json. Test native actions in-process in a
headless simulator; never show a window or synthesize system input for verification.

## System bottom-sheet presentation

Failure scenarios recorded before the fix:

- The visible handle belongs to a plain UIView, with no UISheetPresentationController
  to implement pulling up, dragging down, detents or native scroll handoff.
- A native interactive dismissal leaves the Kumiko drawer state open, re-presents the
  dismissed sheet, or dispatches Close twice.
- Expanding/collapsing the system sheet leaves its content at the old height, loses
  list selection, or permits background controls to act.
- Closing a sheet leaks its controller/delegate, leaves callbacks live, or prevents
  reopening it; closing/disposal during presentation leaves another modal onscreen.
- A short detent clips duration controls; the blocking list cannot scroll at the
  expanded detent, or its callbacks survive after sheet removal.

Use the dedicated `--sheet-only` headless flow to inspect the real UIKit sheet
controller, detents and scroll-expansion policy. Drive UIKit's presentation
lifecycle callbacks in-process and verify Kumiko state, dismissal/reopen, selection
retention and disposal, retaining sheet-*.png, sheet-session.jsonl and
sheet-report.json. This does not synthesize touch or claim physical gesture coverage.

## System bottom navigation

The reference example must use UITabBar with UITabBarItem destinations. A native
selection must reach the Kumiko model and update SelectedItem. Retained destinations
must survive navigation, model-driven changes must update selection, and modal
sheets must reject background selection. Removing/disposal must disconnect native
delegates. Verify these through native callbacks offscreen and retain PNG/JSON
artifacts; do not synthesize system input.

## Tab icon and label layout

A system tab bar can dispatch selections correctly while displaying overlapping
icons and titles. Verify the rendered UIKit descendants, not only item count:
every phone destination needs a visible icon above its readable title, with both
inside its touch target. Preserve that layout after selection and narrow resize.
Retain PNGs and native geometry JSON so the visual failure is repeatable.

## Shared flex layout

FlexboxFlow covers the failure scenarios in ../Kumiko.E2E/FLEXBOX.md that need
UIKit: native frame reflow, retained identity, button dispatch after wrapping,
and first text baselines across different native fonts and nested containers.
Retain flexbox-phone.png, flexbox-wide.png and flexbox-report.json.

## Focus flex composition

Failure scenarios recorded before adapting the example:

- Resizing leaves Start at a manually calculated width, overlaps Pause, or puts
  either target outside the centered session column.
- Duration choices fail to wrap on a narrow phone, leave unused row width on a
  larger host, fall below 44-point targets, or lose native identity when reflowing.
- Reflow changes the selected duration or breaks the existing action/replay flow.

Keep detached phone, narrow and
tablet PNGs, duration-grid PNGs, native frame JSON, session JSONL and the Focus
report. Resize while the same drawer is open and exercise its retained buttons.

## Navigation (failure scenarios before implementation)

Exercise repeated destinations
with independent drafts, late entry-addressed edits, stale Back/confirmation,
unsaved intermediate pages, pending input before Back, cancelled/completed native
pops, model changes mid-transition, scope disposal, restore/replay, covered-page
input/accessibility and deterministic state retention. Retain per-step PNGs,
controller/tree geometry, model-versus-native JSON and replayable sessions.
Desktop baseline screenshots must remain unchanged for existing flows.

### Review failure scenarios (recorded before the fixes)

- UIKit's own pop-gesture delegate must remain the arbiter: the stack forwards to
  the real `_UINavigationInteractiveTransition`, not a null managed wrapper.
- Every system pop recognizer, including iOS 26's content pop gesture, settles
  input and checks guards before beginning.
- A cancelled interactive pop that never delivers didShow must not leave the
  stack frozen; later model pushes still reach UIKit.
- A late didShow for a controller that is no longer the native top, or for a
  transition the stack started itself, sends nothing.
- More queued text than one frame's settle passes still reaches the model before
  a Back request is formed. The custom Back item uses localized UiStrings text.
- The existing UIKit suite (button, components, focus, sheet, tabs, flexbox and
  catalog) must pass after shared control-host changes. Its static views (KUM011)
  are now migrated to model-owned navigation roots, so it builds on v2.

## Responsive environment and CRM adaptation

Failure scenarios recorded before implementation:

- Horizontal/vertical UIKit traits are inferred from width despite an explicit
  override, or unspecified traits have no deterministic logical-size fallback.
- A trait-only or safe-area-only update leaves the rendered view stale; unchanged
  layout schedules frames indefinitely. UIKit callbacks escape exceptions.
- Safe-area values are physical pixels, swap edges, double-inset an already safe
  host, or fail to keep foreground controls inside a full-bounds host's safe area.
- CRM cannot switch between compact navigation and regular list/detail without
  losing the selected visit or note draft; list selection bypasses pending guards.
- At 320x640, 390x844, 844x390, 320/507/678x1024 and 1024x1366, native actions,
  lists or note controls overlap, clip or become unreachable after reflow.
- A regular-width host with compact height has no way to reach its lower details;
  keyboard avoidance is lost when the list/detail shell is introduced.
- Example iPad orientation declarations omit upside-down portrait or block
  multitasking unintentionally; Focus must retain its deliberate full-screen mode.

Use detached controller-owned NativeView instances in a private headless simulator,
trait overrides, and controller AdditionalSafeAreaInsets. Never show a window,
activate an app or simulate system input. Retain responsive-*.png/.txt, a JSON
geometry/behavior report, and CRM before/after images for phone, landscape, split
and full iPad layouts. Physical keyboard presentation/gestures are not simulated.

Embedded-stack failure scenario before the responsive shell implementation:
NativeStack pages use the entire Context size when the stack occupies only a
pane, causing a first-frame overflow before UIKit reports its page viewport.
Bound pages to their Yoga stack container while retaining native chrome/keyboard
viewport feedback. Verify actual list/detail geometry and actions after resizing.

## Responsive review fixes (failure scenarios before implementation)

- In a regular-width RTL culture, a forced LTR safe-area shell keeps the company
  directory on the left. Removing that shell while using flow-relative Edges
  padding swaps asymmetric physical safe-area left/right insets. Verify both the
  actual CRM panes and the responsive probe with left=40/right=12, retaining
  responsive-rtl-*.png/.txt/.json and a report before any assertion can stop capture.
- Focus's deliberate full-screen manifest acquires untested iPad orientations.
  Its original orientation policy must be preserved; six other example manifests
  retain their multitasking orientation declarations.
- Migrating the general UIKit suite leaves the responsive flows out of the
  default run. While legacy KUM011 failures prevent that run, preserve explicit
  integration wiring or a concrete migration TODO, including the default runner.

The responsive sources and CRM dependencies are included in both build modes;
only Responsive.Main.cs is specific to the focused mode. Main.cs invokes
ResponsiveFlow.Run in its default branch and the runner requires its report.
Legacy KUM011 migration failures still block the general executable, but no
follow-up inclusion change is needed once those views are migrated.

## Alerts, confirmation dialogs and context menus

Failure scenarios recorded before implementation:

- An alert or confirmation dialog is a painted imitation rather than a retained
  `UIAlertController`; actions lose order, labels, cancel/destructive styles or
  disabled state, or a disabled action still dispatches.
- Choosing an action sends no message, sends twice, sends both the action and the
  dismissal message, or uses a stale binding after the model changed the actions.
- A user dismissal without an action (iPad popover tap outside, or removal of the
  sheet beneath an alert) leaves the model believing the alert is still shown.
- Removing the declaration from the view leaves the controller onscreen, or
  dismisses it while also sending a dismissal or action message.
- Re-declaring after a dismissal re-presents the departing controller instead of a
  fresh one; a consumed controller is shown again while its declaration persists.
- On a regular-width host an action sheet has no source view and UIKit throws, or
  it anchors to the wrong control; a missing/removed source crashes instead of
  falling back to the host view. Compact hosts present a bottom action sheet.
- Alert text fields lose their model value, overwrite pending input or marked
  IME text on unrelated updates, or a field edit arrives after the action message.
- An alert declared while a system sheet is up is dropped, presented from a
  controller that is mid-transition, or leaves background sheet input active.
  A second simultaneous alert/dialog or a sheet declared above an alert is accepted
  silently instead of being rejected.
- Disposing the host while a controller is presenting or dismissing leaks the
  controller, runs callbacks into a disposed renderer, or lets exceptions escape.
- A context menu cannot be attached to native content, exposes stale or disabled
  commands, ignores SF Symbols/destructive styling, or keeps its interaction,
  delegate and actions alive after removal.
- Replaying the recorded session does not reproduce the final model and image.

Run in a private headless
simulator. Drive UIKit's own alert actions, presentation delegates and context
menu providers in-process; real presentation uses a private hidden window only.
Retain presentation-*.png, presentation-session.jsonl and presentation-report.json.
No touch, long press, VoiceOver traversal or physical iPad pointer is simulated.
The spawned executable has no UIApplication, so UIKit never completes a
presentation transition there; dismissal completion and a sheet carrying its
alert away are not verified by this slice.

The general executable builds now that its static views are migrated.

## Model-owned navigation in the UIKit examples

Failure scenarios recorded before migrating Focus, Todo and the single-screen
fixtures to model-owned `NavigationHistory`:

- Focus keeps its tab outside navigation, or builds history inside Root, so
  replay, `RestoreModel` or time travel lands on a different tab or visit.
- A tab switch pushes a history entry, leaving a Back route to the previous tab;
  reselecting the current tab allocates a fresh visit and resets its state.
- A stale, foreign-host or nonadjacent `HistoryRequest` changes the tab. A tab
  selection or history request delivered while a bottom drawer is open moves
  navigation behind the modal sheet.
- Each tab is a distinct screen inside a persistent navigation shell. A tab
  switch replaces the shell's UITabBar, its delegate or its items, leaves a second
  bar in the hierarchy, or shows a stale native selection. Drawer, duration and
  timer updates replace the UITabBar or its items.
- Restoring a saved model renders a different tab, sends messages, or leaves the
  native selection out of step with the restored visit.
- Single-screen apps and fixtures (Components, NativeButton, flexbox, native text,
  unsupported content, catalog validation, Yoga features and responsive probe)
  construct history per render or throw when given a history request.
- Todo's list detail is pushed without a native stack page, or Back with an
  unsaved new-task or rename draft discards it without confirmation. Keep
  editing loses the request's revision, Discard leaves stale drafts behind, a
  stale Back request (from an older path) removes a newer page, and a native pop
  of a guarded page completes without the model's consent.
- Todo replay or restore produces a different path, native page count or image.

Todo retains todo-{lists,detail,guard,discarded,replay}.png/.txt,
todo-session.jsonl and todo-report.json. Focus adds its navigation checks to
focus-report.json. Requests are delivered as ordinary messages and native
delegate callbacks in-process; no system input is synthesized.
