# Behavior acceptance, before implementation

Run real views through build, layout, input, paint, rendering and PNG export.
No screen capture, synthetic system input, system clipboard or user files.

Compiler E2E verifies
the screen/component/root boundary without analyzers. TypedViews.E2E exercises
Back/Forward, guards, repeated visits, retention, auth reset and replay through
Tester; the main suite also retains the reused generic-path behavior fixtures.
Nested child Back routing and stacked dialog/drawer/popup dismissal scenarios
are recorded in [Navigation.E2E/COMPLEX.md](../Kumiko.Navigation.E2E/COMPLEX.md),
with focused PNG/tree/warnings/session evidence in the navigation E2E host.

## Frame memory

Failure scenarios, recorded before implementation:

- A visible line far wider than its clip, such as a minified file in a diff,
  paints one instance per glyph in the whole line. A megabyte line then needs
  hundreds of megabytes of paint and GPU buffers.
- Culling changes the visible part of a clipped, shifted, rich or
  overflow-clipped line, or drops glyphs whose bearing reaches into the clip.
- One spike frame grows the paint lists and every Metal/Direct3D instance
  buffer, and they keep that size for the life of the window.

Run `Kumiko.E2E --long-line-only`. It compares long clipped lines with short
references pixel for pixel, counts glyph instances, and measures retained paint
and Metal buffer bytes after a 40,000-cell spike followed by small frames.

## Idle GPU frames

Failure scenarios, recorded before implementation:

- Apple GPU drivers charge a process about 230 MB of graphics memory for about
  a second after each Metal render pass, and only then let it become
  reclaimable. Pointer motion over static content redraws an identical image on
  every event, so the memory stays in the app's footprint while the user works.
- Skipping frames hides a real change: a hover that changes the image, a new
  drawable size or a new clear color must still render.

Run `Kumiko.E2E --idle-frames-only`. It presents Tester frames through a
standalone CAMetalLayer and counts the frames that reach the GPU.

## Text overflow

Failure scenarios, recorded before implementation:

- Ellipsized text keeps its natural minimum width and pushes fixed siblings out
  of a row, or fails to use the final allocated width after resize.
- Truncation splits an emoji, surrogate pair or combining sequence; the marker
  exceeds the box, or an empty/narrow label loses its line height.
- Clip inserts an ellipsis, Visible changes existing rendering, or unbounded
  text truncates. Explicit newlines lose lines or wrapped ellipsis is ignored.
- Rich spans lose their color/weight or their widths are measured as plain text.
- RTL/mixed-direction labels put the marker at the wrong visual end.
- A same-sized source edit, font/style change or repeated resize leaves stale
  glyphs even though Yoga legitimately reuses its geometry.
- Accessibility receives shortened text; selecting through the marker copies
  the synthetic marker instead of the original source; highlights escape the box.
- Intentional overflow produces false warnings or hides a real vertical overflow.
- Native labels truncate without opting in, or fail to honor explicit clipping
  and ellipsis. Platform limitations must be documented, not silently promised.

Run `tests/Kumiko.Text.E2E` (or `Kumiko.E2E --text-overflow-only`) for offscreen PNG/tree/warnings artifacts and the
hashed JSON report. Compare rendered labels with independently authored expected
text, check row geometry, and select/copy through Tester's private clipboard.

Failure cases to cover:

- A click changes state but the displayed frame still contains the old value.
- Press/release on different controls triggers the wrong control; disabled
  controls activate; keyboard focus cannot reach or activate a control.
- Control/Option/Command+Tab moves keyboard focus, so an application's tab
  switcher never sees it; held modifiers are forgotten between frames, are not
  updated when a modifier alone is released, or stay held after focus leaves.
- Text editing splits surrogate pairs or grapheme clusters, loses selection,
  applies composition twice, or copies to the system clipboard in a test.
- Wrapping text overflows a constrained row, absolute children consume flow
  space, max sizes are ignored, or growing children overlap.
- Wheel input escapes a nested scroller; a virtual list creates every row or
  shows the wrong row after scrolling; keyed controls lose identity.
- Dragging a scroll thumb does not follow the pointer, loses its grab point,
  or clicks the content beneath the bar.
- Images lose alpha, clipped content escapes rounded corners, CPU and Metal
  disagree materially, or glass reads the wrong backdrop.
- IPC accepts the wrong argument shape, loses queued events, executes unknown
  methods, emits invalid TypeScript, or fails under NativeAOT.
- Hidden native windows fail to create/render/resize/close, callbacks outlive
  disposed state, dispatch happens on a worker thread, or idle apps poll.
- PNG artifacts are missing, invalid, or not reproducible with the same script.

The harness writes screenshots and a machine-readable report with pixel hashes,
checks, runtime information and timings. Exit code is nonzero on any failure.

## Reusable styles

Failure scenarios, recorded before implementation:

- Applying the same extension style to several keyed cards leaks appearance,
  padding or identity between instances, or prevents input reaching a card.
- Composing selection and density variants loses base styling; changing selection
  or removing a conditional variant leaves the preceding frame's appearance.
- A local width or horizontal-padding override is ignored or changes vertical padding.
- A reused style captures the old theme instead of reading the theme passed on
  this frame, including a different theme within the same tree.
- Clearing a shared background, border, shadow or glass material leaves visible
  paint; restoring a color after a clear fails to paint it again.
- Clearing an aspect ratio still forces the old height instead of content sizing.
- Pure C# 14 extension styles are rejected, impure extension bodies escape the
  Kumiko checker, or a styled Div without using escapes the scope checker.

Keep light/dark, selected/compact and reset PNG/tree/warnings artifacts, plus
compiler fixture reports. Verify geometry, rendered pixels and input behavior;
do not assert the internal Style fields.

## Agent authoring and inspection

Before implementation, exercise these failures through full views and compiler runs:

- A tree dump differs after an unchanged frame, depends on current culture, loses
  multiline/quoted text, or omits geometry, keys, role, disabled/clipping/scroll state.
- Role/name lookup silently picks one of two controls, clicks a disabled, covered
  or clipped control, or loses a keyed target when siblings reorder.
- Repeated Control calls repeat focus/click side effects or re-enable a disabled node.
- Duplicate sibling keys or repeated unkeyed construction sites hide state aliasing;
  virtual rows without an explicit rowKey hide their positional identity.
- Unclipped overflow, overflowing text, empty/overlapping controls or low contrast
  fail to produce actionable JSON paths; valid clipped scrolling/contained controls
  produce spurious overlap or overflow reports.
- Percent dimensions use sibling grow weights instead of the parent's inner size,
  or percentages break on resize, in absolute positioning or on the cross axis.
- Node animation channels interfere, reset on keyed reorder, overshoot, or ignore
  ReduceMotion. A malformed scope error fails to name its key/path.
- A tree-only CLI invocation opens a window or skips scripts; dev snapshots omit
  their adjacent warnings JSON. Outputs must be usable with NativeAOT.
- Compiler diagnostics miss discarded/fluent Div scopes, unkeyed loop elements,
  direct Task.Run/Thread/ThreadPool UI calls or computed Animate IDs; valid using,
  keyed loops and App.Dispatch callbacks must compile without false errors.

Write authoring-tree.txt, authoring-warnings.json, authoring.png and a compiler
fixture report so the checks are reproducible without viewing the real screen.

## Example authoring follow-up

Failure scenarios, recorded before implementation:

- A flexible column with long content pushes a fixed sibling outside the row;
  weighted flexible columns fail to resize with their parent.
- Implicit System usings make the typography token name ambiguous.
- A named icon cannot be located as an image, or a decorative icon is announced
  using its internal symbol identifier.
- Legacy key/animation calls compile without a migration warning; node-owned
  motion itself produces an obsolete warning.
- A helper invoked repeatedly creates unkeyed siblings without runtime warnings;
  a keyed helper reports a false warning. KUM002 is intentionally method-local.
- Published CallerFilePath strings contain the checkout directory in UTF-8 or
  UTF-16 instead of repository-relative mapped paths.
- The example warning gate accepts a new warning, a changed message/related path,
  a suppression without a reason, or silently removes raw diagnostic evidence.

Keep PNG/tree artifacts for the authoring view, the warning gate's CLI report,
and annotated warnings for every checked example. Preserve upstream geometry
when replacing literal values with shared tokens or named app palette values.

## Kumiko programs and recorded sessions

Failure scenarios, recorded before implementation:

- A view sends a message twice during same-frame rebuilds, a worker updates the
  model off the UI thread, or commands execute while the view is still building.
- An async success/failure is lost, command exceptions escape callbacks, or a
  completion from a discarded history branch changes the selected model.
- Recording omits a control's role/name/key/path, message payload, model changes,
  command names, frame/time, initial state, or branch parent.
- A replay executes effects or subscriptions, depends on the original services,
  accepts a corrupt/nonmatching log, or produces a different displayed result.
- Stepping backward/forward changes retained snapshots, or sending while rewound
  destroys the old branch. Invalid steps change the model before failing.
- A model-driven subscription starts twice, survives removal/disposal/history
  navigation, or idle programs request continuous rendering.
- Text contents remain in a private buffer after a rewind instead of following
  the selected model. Mapped child messages lose their origin.
- A rewound animated view continues scheduling frames against a frozen clock,
  or committed pipeline drag moves stay outside the shared model/history.
- Counter or Companies sorting, select-all, selection clearing and filter flows
  stop working after migration. Filter cancellation commits a draft, or invalid
  filter stages can be reached through messages.
- Offscreen record/replay/step CLI options open a window, omit PNG/tree/warnings,
  or break under NativeAOT with reflection-based serialization.
- Opt-in pure views mutate fields/properties or mutable collections; pure update
  methods start tasks, access clocks/randomness, or perform I/O; message switches
  omit a known concrete message type even when a catch-all arm is present.

Retain kumiko-session.jsonl, kumiko-{live,replay,step,branch}.png/.txt/.warnings.json,
compiler reports and CRMLite artifacts.

## Interactive event debugger

Failure scenarios, recorded before implementation:

- Debug mode changes normal views when disabled, needs app-specific UI wiring,
  or cannot be enabled by code and by the app's command line.
- Back/forward controls select the wrong model, create application events, run
  commands again, or navigate across a branch instead of following its parent.
- Selecting an event does not display its message, source, model changes and
  commands; an empty history or its ends leave invalid navigation enabled.
- A debugger click or keyboard activation leaks into application shortcuts,
  text editing, modal dismissal or model updates; stepping destroys debugger
  keyboard focus or throws because the app view's scopes are still open.
- Pausing leaves subscriptions running or accepts stale results; resuming fails
  to restart the selected model's subscriptions or re-executes old commands.
- Branching removes an old future, forward cannot return along the chosen
  branch, or jumping to the latest event silently resumes live effects.
- The collapsed/expanded inspector blocks the app without a way to collapse it,
  loses controls on small windows, changes app node identity, produces new layout
  warnings, or keeps waking an idle native loop while paused.
- Debug actions cannot be exercised through offscreen scripts, or debug mode
  breaks source-generated serialization/NativeAOT.

Keep debug-{live,paused,branch,expanded,narrow,dark,cli}.png/.txt/.warnings.json,
a debug-session.jsonl and the E2E report as repeatable evidence.

## Hot reload

Failure scenarios, recorded before implementation:

- A metadata update arrives on a worker thread and the open native window keeps
  showing the old view until the next input, or the view is built off the UI
  thread. Updates for a disposed App throw or keep it alive.
- Hot reload adds a polling loop: an idle window rebuilds without an update.
- A `dotnet watch` restart loses the selected Kumiko model, or restores a model whose
  JSON no longer round-trips through the current schema (missing, renamed or
  retyped fields). Malformed or missing state files throw instead of starting
  from the initial model.
- A restored model stays paused, keeps the old history, or does not start its
  subscriptions; new messages do not branch from the restored step.
- Model changes are not persisted without a window close, or the final change
  before closing is lost. Persisted state is written outside the app's own
  build output, or under NativeAOT the handler produces trimming warnings.

Keep hot-reload-{before,after}.txt and hot-reload-state.json as evidence.

## Mandatory Kumiko applications

- Callback-only Dev, App and Tester hosts, ref controls, mutable-buffer text fields
  and the Changed setter must be inaccessible from external application assemblies.
- Unannotated or helper-hidden view mutations, update effects, mutable model/message
  payloads, captured state and UI access inside effects must fail compilation with
  KUM005–010. Primitive and immutable collection/record graphs must compile.
- DevMode must reject a model-mutating view, an input-mutating update, or repeated
  updates producing different models/command names before recording a valid step.
  Validation must never execute a command twice.
- Controlled controls and native accessibility editing must produce recorded messages.
  Capturing an Input before starting an effect must not evade runtime UI guards.
- A freshly installed template must build as a standalone NuGet consumer with no
  handwritten serialization attributes, render offscreen and replay its session.
- Gallery tasks, Workbench edits/loading/pickers, CRMLite drag/filter state and
  services must remain usable through immutable models and replay.
- CRMLite must sort and filter its companies table, add notes, move deal stages by
  drag and from the details panel, validate and create deals and companies, and
  render every page in light and dark without warnings (`tests/Kumiko.CRMLite.E2E`).

Retain enforcement-session.jsonl, enforcement.png, compiler fixture reports and
artifacts/kumiko-template/{live,replay}.png/.txt, session.jsonl and report.json.

## SVG images

Failure scenarios, recorded before implementation. macOS renders through AppKit,
Linux through librsvg and cairo, Windows through Direct2D SVG documents:

- An SVG is upscaled from its intrinsic size instead of rasterized at the
  requested size and scale, so edges blur on Retina targets.
- Rendered SVGs are vertically flipped, lose transparency, or stretch a
  non-square viewBox instead of fitting it centered inside the requested box.
  A document sized only by width/height (no viewBox) stays at its intrinsic
  size in a corner instead of scaling to the box; an icon with fixed width,
  height and viewBox (width="24" height="24" viewBox="0 0 24 24") does the same.
- Garbage, empty, truncated or raster (PNG) bytes passed as SVG return a blank
  image instead of throwing InvalidDataException; zero, negative, non-finite or
  oversized dimensions are accepted.
- Rendering the same SVG twice differs, or CPU and Metal disagree on an SVG
  drawn in a rounded, clipped view.
- The three backends disagree on orientation, transparency, channel order,
  premultiplied alpha or aspect fitting for the same document.
- A missing librsvg or a Windows build without Direct2D SVG returns an empty
  image or crashes instead of throwing PlatformNotSupportedException.
- A native failure leaks COM objects, GObjects, cairo surfaces or GErrors, or
  leaves COM initialized on the calling thread.

Retain svg.png and svg-metal.png.

## Business-application controls

Failure scenarios, recorded before implementation:

- A calendar starts the week on the wrong day for its culture, shows month or
  weekday names from the wrong culture, or lays out a month in fewer than six
  stable rows so the popup height jumps between months.
- Arrow, Page Up/Down (Shift for years) and Home/End keys move focus to the wrong
  date, lose focus when crossing a month boundary, or land on a disabled date or
  one outside Min/Max. Clicking a disabled date selects it.
- A date picker shows the selection in an invariant or ISO format instead of the
  culture's short date, keeps its popup open after a choice, returns focus to the
  page instead of its trigger, or reports a change when Escape closes it.
- A time picker offers times outside Min/Max, ignores its step, highlights no
  option for the current value, or opens scrolled away from it.
- A number input rejects the culture's decimal separator, replaces a partially
  typed value ("1," or "-") while editing, accepts values beyond Min/Max on
  commit, steps by the wrong amount with arrows/buttons, or exposes no range.
- A tooltip appears before its delay, never appears for keyboard focus, keeps
  waking an idle loop, steals hover or clicks from its trigger, survives Escape
  or pointer exit, closes while the pointer moves onto it, leaves the window, or
  is not exposed as the trigger's accessible description.
- A tree builds rows for collapsed or off-screen branches, loses the selected
  item's focus after expanding or scrolling, ignores Right/Left expansion rules,
  moves to a hidden child, or omits tree-item level, expansion and selection.
- A resize handle moves beyond its limits, jumps when grabbed off-center,
  ignores the keyboard, or is not adjustable through accessibility range actions.
- Strings built into controls (calendar navigation, placeholders, validation
  messages, grid empty states) cannot be replaced per window.

## Data grid productivity

Failure scenarios, recorded before implementation:

- Primary-click or Shift-click replaces instead of toggling or extending the
  selection; Shift+arrows do not extend; Primary+A selects hidden or filtered
  rows; selection is lost when rows are re-sorted, filtered or reloaded.
- Primary+C copies nothing, copies to the user's clipboard in tests, omits the
  selected rows, uses a different column order from the screen, or breaks values
  containing tabs, quotes or newlines.
- CSV export ignores the current filter, sort or column order, quotes fields
  incorrectly, or writes cells beginning with =, +, - or @ that a spreadsheet
  would execute as formulas.
- Dragging a header edge resizes the wrong column, goes below its minimum width,
  or is lost on the next frame; dragging a header reorders without crossing a
  neighbour, or reorder/resize cannot be done from the keyboard.
- The quick filter matches hidden text instead of displayed values, is case or
  culture sensitive in surprising ways, or leaves a filtered-out row selected.
- Grouping duplicates or drops rows, builds collapsed rows, loses group collapse
  state on re-sort, or exposes group rows as ordinary data rows.
- Column widths, order, selection and collapsed groups do not survive the Kumiko
  state snapshot round trip, or capturing unchanged state produces a different
  snapshot and re-dispatches forever.

Retain controls-*.png/.txt/.warnings.json and grid-*.png/.txt/.csv artifacts.

## Localization and right-to-left layout

Failure scenarios, recorded before implementation:

- An Arabic or Hebrew window lays rows out left to right, keeps padding and
  absolute offsets on the left, leaves text left-aligned in full-width boxes,
  or keeps vertical scrollbars on the right.
- A subtree marked left-to-right inside a right-to-left window (a chart, a
  phone number, media controls) is mirrored anyway, or a nested mirror is
  applied twice.
- Mirroring flips glyph order or images, breaks text-field carets, selection or
  click-to-caret positions, or right-aligned fields lose the caret when empty.
- Popovers, tooltips and context menus open at the mirrored position instead of
  beside their trigger, or align to the trigger's left edge in RTL.
- Left/Right keys keep their LTR meaning in calendars, trees, sliders, tabs,
  radio groups, resize handles and grids; directional icons such as chevrons
  point the LTR way; dragging a slider or handle moves the value backwards.
- A window in a translated culture shows English control text although a
  translation exists, an explicit `Strings` assignment is overridden by the
  culture, or a missing translation leaves blank or format-breaking text.
- `--kumiko-culture` does not change formatting, direction or built-in text, or
  needs a code change to test another culture offscreen.

Retain rtl-*.png/.txt and localized control artifacts.

## Accessibility preferences and audit

Failure scenarios, recorded before implementation:

- A window ignores the operating system's reduced-motion setting, or keeps
  animating after the user turns it on while the app runs.
- High-contrast mode keeps the regular palette: text, borders, focus rings or
  selection fall below 7:1 contrast, disabled text becomes invisible, or a
  focused control is indistinguishable from an unfocused one.
- Windows high-contrast themes are replaced by Kumiko's own colors instead of
  the user's system colors.
- Larger system text sizes do not enlarge Kumiko text and control heights, or
  enlarge them so that controls clip their labels.
- The accessibility audit misses an unnamed control, an icon-only button
  without a name, a pointer target smaller than 24×24, a control unreachable by
  keyboard, duplicate sibling names that make controls indistinguishable, or
  low-contrast text; or it reports intentionally decorative or disabled content.

Retain contrast-*.png/.txt and accessibility-audit.json.

## Drag and drop

Failure scenarios, recorded before implementation:

- A press that moves a few pixels starts a drag (jitter), a drag never starts, or
  the source control also reports a click when the drag ends on itself.
- A drop lands on a target that does not accept the payload kind, on an outer
  target instead of the innermost one under the pointer, or on two targets.
- Releasing outside every target, pressing Escape or losing the pointer leaves a
  drag active, so the next click drops a stale payload.
- Files dragged in from the operating system are delivered to a target that does
  not accept files, outside its bounds, more than once, or with relative paths.
- A drop is reported outside the view build, so a Kumiko view cannot dispatch it
  as a message.
- Native windows do not register for file drops, or a drop on a hidden window
  reaches the user's files during tests.

## Window state and screens

Failure scenarios, recorded before implementation:

- A remembered window reopens off-screen after a monitor was removed, larger
  than the available work area, or under the taskbar/menu bar.
- Restoring a maximized window loses its normal size, or a window restored on a
  secondary monitor jumps to the primary one although that monitor still exists.
- State files are written into the user's real application data during tests,
  are corrupted by a partial write, or crash the app when malformed.
- Moving a Windows window between monitors with different DPI keeps the old
  scale, renders blurry, or ignores the system's suggested window rectangle.

Retain dragdrop-*.png/.txt and window-state.json.

## Typed views and agent actions

Failure scenarios recorded before implementation:

- Invoking a named menu option activates a different control after focus moves,
  submits twice during a rebuild, or survives removal of the target.
- A modal permits background invocation, focus, editing or scrolling; a nested
  popup loses its owning dialog or fails to restore the accessible scope.
- Typed views evade purity/immutability checks, capture persistent mutable state,
  or require a second application runtime instead of IKumiko.
- Form submission via Enter, Primary+Enter or Submit differs; multiline editing,
  IME composition, disabled buttons or nested popups accidentally submit a form.
- Message-bound custom controls lose their existing appearance, label, identity,
  keyboard behavior or accessibility actions.
- An agent cannot distinguish unsupported, stale, ambiguous, disabled, obscured
  or missing targets; a stale revision mutates the current view.
- Offscreen options cannot be revealed through semantic actions, scrolling leaks
  outside the active modal, or anonymous layout wrappers flood the native tree.
- Accessibility edits lose their original source in recorded Kumiko history.
- The opt-in JSONL agent process loses state between requests, blocks worker
  completion while awaiting a revision, polls while idle, or opens a window.
- CRMLite can represent two dialogs simultaneously, a company screen without an
  ID, or a picker belonging to a different dialog; record/replay loses variants.

Retain agent PNG/JSON/JSONL artifacts, protocol transcripts, compiler fixtures
and CRMLite interaction/replay artifacts. External native client checks remain
limited to hidden windows; do not claim visible-window verification.

## Repeated settings section clicks

Settings section scroll failure scenarios, recorded before the fix:

- Repeatedly clicking the same nav item restarts the easing curve, delaying the
  destination and making the scroll stop and start.
- Clicking an already aligned section moves the content, or ignoring duplicates
  prevents changing destination or realigning after manual scrolling.

CRMLite SettingsScrollFlows compares single-click and repeated-click positions
at the same elapsed times. Retain settings-scroll.json and its PNG/TXT artifacts.

## Menu highlight

Failure scenarios recorded before implementation:

- A popover inside a dialog trades focus with the dialog every frame, so its
  first option flickers while the pointer moves.
- A menu opened by the pointer highlights its focused first option as well as
  the hovered option.
- A resting pointer takes the highlight back from Up/Down, or the pointer
  leaving the menu leaves a pointer highlight behind.

CRMLite MenuFlows covers these through the New deal owner select.

## Shared layout motion and context menus

Failure scenarios recorded before implementation:

- A tab underline jumps to the new tab, slides from the wrong place, or lags
  behind a window resize; Reduce Motion still animates it.
- Clicking a tab or button leaves a focus ring; keyboard focus shows none.
- A secondary press opens nothing or opens at the wrong point; Shift+F10 on a
  focused control opens nothing; a right-clicked CRMLite row opens the company.

MotionChecks, CRMLite TabFlows and MenuFlows cover these.

## Enter and exit transitions, drawers and cell menus

Failure scenarios recorded before implementation:

- An entering node appears at once, or starts somewhere other than its
  transition; a removed node vanishes instead of animating out, stays in the
  semantic tree, takes input, or never disappears.
- Reduce Motion still animates entries or exits.
- A drawer does not dock to its edge, slide in, trap focus, scroll, close on
  Escape, or slide back out; a dialog appears without its pop.
- A right-clicked grid cell shows the row's commands instead of its own, or a
  cell without commands shows nothing.
- CRMLite: a card click opens the company instead of the deal drawer; a right
  press starts a drag; owner cells offer no owners; the sidebar selection jumps
  or a sidebar list never shows as selected.

TransitionChecks, DataGridChecks, CRMLite DrawerFlows and TabFlows cover these.

## Fuller flex layout

See [FLEXBOX.md](FLEXBOX.md) for the pre-implementation failure scenarios.
FlexboxChecks and FlexboxEdges exercise responsive views, input, native text
metrics and PNG/TXT/JSON exports; `--flexbox-only` runs this slice repeatedly.

## Yoga.Net layout engine

See [YOGA.md](YOGA.md) for migration failure scenarios and repeatable artifacts.
YogaChecks exercises text/style/tree changes and resizing through offscreen views.
The existing Flexbox, scroll, localization and application flows remain required.

## Incremental layout

Layout may skip work only when the result cannot change. Failure scenarios: a
string with the same measured size, such as a monospaced counter, keeps stale
painted text; a fixed-size absolute box relaid out alone leaves its text at the
old width; an auto-sized absolute panel or a baseline-aligned row keeps stale
geometry after its text changes; a resize after skipped frames reuses old
constraints. IncrementalLayoutChecks compares every node rectangle after each
change with a fresh Tester of the same state and requires the new text.

## Yoga upstream synchronization (21 October 2026)

Failure scenarios, recorded before implementation:

- Frozen fractional shrink factors leave a positive or near-zero total and
  produce enormous boxes; mixed and tiny unfrozen items must remain finite.
- Fractional max-content widths plus padding spuriously wrap an otherwise
  single-line row, while real overflow still wraps.
- Switching an attached box to/from contents stops descendant dirtiness;
  measure-only/cache-hit passes falsely flag hidden or contents descendants;
  absolute updates through contents become unreachable or mutate shared nodes.
- Parent resize leaves percentage padding, margins, dimensions or bounds stale
  in measured and container caches, including fixed absolute relayout boundaries.
- Copying only box-sizing leaves the old geometry; freeing a child leaves its
  parent's old intrinsic size; rounding far-off or whole-pixel text loses pixels.
- Automatic minimum sizing shrinks below content, ignores leaf padding/border,
  explicit zero minima, non-visible overflow, specified/max sizes or aspect ratio.
- A nested min-content probe grows exponentially, reuses another owner's width,
  ignores static/dynamic declarations, or survives changed content next layout.
- First-pass min/max freezes depend on sibling order and collapse growable rows.
- Experimental scroll flex-basis sizing unbounds viewport wrappers, loses authored
  basis/percentage sizes or fails to fill flex-growing content after mutations.

Retain yoga-upstream.json plus yoga-upstream-*.png/.txt/.warnings.json, and run
both normal and NativeAOT E2E hosts. All rendering remains offscreen.

## V2 views, services and shared windows

`tests/Kumiko.V2.E2E` covers service mapping/debounce/subscription lifetimes,
replay isolation, shared documents with independent window selections, hidden
native close requests, idle behavior, history/restore and atomic replay failure.
It runs independently of deliberately unmigrated examples. Retain its PNG, tree,
JSON/JSONL and native-error artifacts for JIT and NativeAOT.

Compiler E2E covers view/library purity, mutable fields/props, retained callbacks,
unkeyed children and invalid root wiring.

## Typed composition

CompositionChecks exercises rejected/reused bindings,
keys, exceptions and subsequent sibling input through Tester. The focused
Kumiko.TypedViews.E2E host covers auth/dialog/sign-out, nested child routing,
reorder/caret/undo/removal and replay with PNG/TXT/JSON/session artifacts.
CompositionCompilerChecks uses no custom analyzers and compiles a reusable view
library without a reference to the consumer application.

## Desktop actions

`tests/Kumiko.DesktopActions.E2E` exercises DesktopNotes through
Tester, its shared native-menu projection and hidden native windows. It verifies
single dispatch across five surfaces, disabled/checked states, stale and removed
callbacks, explicit window/focus targeting, editor and IME precedence, modal
blocking, palette focus restoration, save/edit races and effect-free replay.
Retain its JIT/NativeAOT PNG/tree/session/JSON evidence.

## Responsive environment

See [RESPONSIVE.md](RESPONSIVE.md) for failure scenarios recorded before
implementation and the repeatable offscreen matrix/artifact contract.

## Kit content renderers

HeadlessChecks
runs custom command/tab/tree content via Tester (retain parts-*.png/.txt/.warnings.json).
