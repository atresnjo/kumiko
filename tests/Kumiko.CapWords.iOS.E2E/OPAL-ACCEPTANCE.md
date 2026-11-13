# Opal failure scenarios, before implementation

The public reference and licensed local checkout define this scoped native port.
No private source or assets are copied into this repository.

- The fourth gallery preview intercepts its opening button, restyles earlier
  studies, or returning loses the selected duration or expanded presentation.
- Minus/plus leave the 5-minute through 24-hour range, skip the 60m/2h boundary,
  or three consecutive taps within 300ms fail to expand. Slow taps must reset
  that sequence. The value button toggles on its own.
- Hidden minus/plus, Block or slider intercept touches during the opposite
  presentation. Back, value, Start Timer and the bottom controls stay reachable.
- Native contact fails to select the touched stop, markers/fill use the wrong
  denominator, the final stop fails to fill the track, or dragging reports values
  outside the selectable range. A stationary contact must also select a stop.
- Dragging past either end fails to stretch about the opposite edge, loses
  vertical compression, or release/cancellation leaves a deformed control.
  Cancellation retains the last selected step, as the source does.
- Interrupting the return spring jumps to its old endpoint. Rapid presentation
  reversals restart at an old pose or expose hidden controls. Reduce Motion,
  resizing and detached hosts must settle safely without stale animations.
- Native callbacks escape exceptions; retained recognizers/layers/actions leak;
  an idle app uses a managed render polling loop.
- Block, Start Timer and unrelated tabs imply connected services. They should
  disclose demo scope through the existing native drawer pattern.

Verify through detached and live simulator E2E flows with controlled native
recognizer input, native hit testing, phone/narrow/tablet view-layer PNGs,
session replay and JSON results. Record Release CADisplayLink pacing separately
for idle, presentation toggling, in-range dragging, edge stretch and return.
Callback pacing is not GPU presentation FPS or physical-device touch evidence.
