# Trace waterfall behavior, before implementation

Drive the desktop example through Kumiko's offscreen Tester. Never open a native
window, send system input, or access the user's clipboard or application state.

Failure scenarios:

- Parallel spans appear sequential, start before their parent, or extend beyond
  the displayed time; live bars, totals and the current-time line disagree.
- A paused or completed replay continues advancing or keeps a timer alive.
  Restart retains stale selection/collapse state or an old tick advances a pause.
- Folding one branch hides a sibling, loses a failed child from aggregate totals,
  or forgets nested fold state when the parent is reopened.
- Selecting a failed call shows another call's input/output, hides its error,
  or retry data erases the original failure. Escape fails to close the inspector.
- Keyboard navigation moves into folded children, cannot fold/open a branch,
  loses focus when the inspector closes, or gets stuck between two calls with
  the same display name (the two web_search calls).
- The slowest marker picks an unfinished call or a parent that aggregates children.
- Theme changes reset the replay; resizing loses timing alignment or clips controls.
- Recording/replay changes the trace or its visible state. Repeating the same
  offscreen flow produces different screenshots.

Run `dotnet run --project tests/Kumiko.TraceWaterfall.E2E -c Release --
artifacts/tracewaterfall/e2e`. Keep light/dark, live, collapsed, failed-detail and
compact PNG/TXT/warnings artifacts, a recorded JSONL session, and report.json
with SHA-256 hashes. Run the same executable under NativeAOT.
