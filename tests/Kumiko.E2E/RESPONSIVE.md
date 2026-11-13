# Responsive environment acceptance

Failure scenarios recorded before implementation:

- A desktop view uses physical pixels or stale dimensions for its width/height
  classes; resizing across 600/900/1200 logical points does not rebuild, or a
  height-only resize fails to change the vertical class at 600 points.
- Named width breakpoints disagree at their boundaries or a desktop context
  reports nonzero safe-area insets without a platform source.
- At 480, 800, 1280 and 1800 points, CRM navigation or company actions overlap,
  clip, become unreachable, or dispatch a different row after reflow. A narrow
  sidebar overwrites the user's expanded preference on returning to wide.
- A very wide CRM stretches readable content without a bound. Compact company
  rows omit access to details; search/filter controls disappear instead of reflowing.
- Reflow loses the selected record, a draft, keyboard focus or stable action
  identity. A hidden native window resize fails to invalidate its responsive view.

Exercise Tester and a hidden native window, with no system input or clipboard.
Retain responsive-report.json and responsive-*.png/.txt/.warnings.json.
Retain the actual CRM before/after narrow, medium, wide and ultrawide images.

Additional failure observed before correcting the CRM table composition: a taller
responsive toolbar lets the table's intrinsic row height push its fixed count
footer below the viewport, including after creating a 24th company. Both scroll
viewports must share the remaining height while keeping that footer reachable.

## Review fixes (failure scenarios before implementation)

- Typed Left/Right padding, margin, layout border or absolute/relative insets
  move to the opposite physical edge under RTL, including nested direction
  islands. Start/End must still follow reading order. Exercise both LayoutEdge
  and LengthEdges, percentages, auto margins, and explicit direction boundaries.
- Edges shorthand padding/margins/borders or symmetric helpers change their
  established mirroring. Repeated culture-only changes reuse stale edge mappings.
  In particular, an explicitly LTR node must react when only its parent's
  direction changes, even though its own effective direction stays constant.
  A later typed physical override must still work after Edges shorthand; a
  shorthand border must not become a logical override that masks physical setters.
  Export the complete edge matrix as PNG/tree/geometry JSON before assertions;
  compare incremental geometry with a fresh layout of each state.

- Physical `LayoutEdge` safe-area padding must remain on the same screen edges
  after culture-only RTL/LTR changes, including nested LTR islands; ordering and
  hit testing still mirror. Capture both directions before checking geometry.

- At 1199/1200/1280 points with the sidebar at MaxSidebar (360), the main pane
  cannot fit the full toolbar although the window is Wide. Controls overlap or
  clip, search/filter clicks miss, or the count footer leaves the viewport.
- Below Expanded, header, menu/shortcut and Settings sidebar toggles remain
  enabled despite the forced rail. They silently change the saved preference;
  disabled/checked/accessibility labels disagree with the visible rail. Resizing
  back to Expanded must restore working controls and the original preference.

Retain responsive-review-wide-sidebar-*.png/.txt/.warnings.json and native-menu
state, plus repeatable before/after captures and action checks through Tester.
