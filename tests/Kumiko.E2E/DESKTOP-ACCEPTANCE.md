# Desktop foundations and workbench acceptance

Failure scenarios recorded before implementation. Exercise real views through
input, layout, native shaping, accessibility actions and rendering. Retain PNG,
tree and JSON evidence; never open a visible window or use system input.

## Text

- Arabic joining, Indic conjuncts, combining marks, mixed Hebrew/Latin and font
  fallback render as isolated or missing glyphs on Windows/Linux.
- Native shaping/raster resources outlive their owner or grow on every frame.
- Caret hit testing splits a grapheme or assumes logical order is visual order.
- Changing font family, size, weight or display scale reuses stale glyph pixels.
- Multilingual line wrapping loses characters or produces nonfinite geometry.

## Accessibility

- A native client can read a control but cannot set its value, change its range,
  toggle it, select text, or obtain its text and selection.
- Password contents appear in a native text/value query.
- Disabled, removed or disposed elements still accept accessibility actions.
- Worker-thread provider calls mutate the view outside its owning UI thread.
- Linux exposes no native ATK tree to the GTK accessibility bridge.
- Grid, row, cell, combobox and menu semantics lose selection/expanded state.

## Rendering

- Windows/Linux upload a full CPU-rendered image for every ordinary GPU frame.
- GPU clipping, alpha, glyphs, images, rounded borders or shadows materially
  disagree with the deterministic CPU renderer.
- Resize/DPI changes leave a stale framebuffer, viewport or atlas.
- GPU failure leaves a blank window instead of the supported software fallback.
- Capturing hidden app content requires showing a window or driving the screen.

### Rendering performance follow-up

Failure scenarios recorded before implementation:

- Moving, recoloring, removing or reordering translucent/shadowed content leaves
  stale pixels, or partial drawing loses unchanged overlapping content.
- A scale change with the same framebuffer dimensions, a background change,
  atlas growth/reset/replacement, or a new image reuses stale cached pixels.
- An unchanged software frame allocates another framebuffer or redraws pixels;
  a small hover change or appended text glyphs redraw the entire window even
  though existing atlas texels are unchanged.
- Row-band drawing races, changes blending order, or reads an incomplete glass
  backdrop. Worker threads must only rasterize immutable input into disjoint rows.
- Glass on OpenGL reads the framebuffer to the CPU during normal rendering,
  samples the wrong orientation or backdrop, loses tint/rounded clipping, or
  differs at fractional/2x scale, screen edges or overlapping panes.
- A resized or disposed GL renderer retains obsolete glass textures or leaves
  GL state that breaks the next ordinary frame.
- Forced software rendering opens a window, accesses user state, or reports the
  GPU as the active presenter. Captures must report the actual selected path.

Retain render-*.png, render-flows.json and render-performance.json. Compare
incremental/threaded CPU frames byte-for-byte with a fresh serial rendering of
the same full views. Record timing distributions, allocations, driver and frame
dimensions; offscreen GL timing includes final capture/readback.

### Direct3D 11

Failure scenarios recorded before implementation. Direct3D 11 is the Windows
path for VDI, RDP, Citrix and virtual machines that lack OpenGL 3.3.

- Device creation fails and the window stays blank instead of falling back to
  the software renderer with a recorded reason.
- No hardware adapter is available and WARP is never tried; `warp` selects a
  hardware device; selecting `d3d11`/`warp` changes the default selection.
- WM_SIZE or a DPI change leaves the swap chain, viewport or frame texture at an
  obsolete size, or stretches a stale frame.
- D3D11 and CPU materially disagree on quads, borders, text, images, rounded
  clipping, alpha or shadows at 1x, fractional or 2x scale.
- Glass samples the wrong backdrop or orientation, misses earlier panes, reads
  the frame on the CPU during normal rendering, or differs from CPU/OpenGL.
- Captures are vertically flipped, swap red/blue, lose premultiplied alpha or
  return padded staging rows; a capture changes the next presented frame.
- Disposing or recreating the renderer leaks COM objects, swap chains or views,
  or leaves a second flip-model swap chain attached to the window.
- DXGI_ERROR_DEVICE_REMOVED/RESET is ignored, recreated without bound, or not
  followed by the software fallback after one failed recreation.
- `Window.Renderer` does not name the adapter, does not distinguish WARP, or
  reports Direct3D after falling back.
- Offscreen D3D11 parity is silently skipped on Windows, where WARP is always
  present; other hosts must record it as unavailable in the JSON report.

## Reusable controls and inventory reference app

- Sorting edits the wrong record, loses selection, or creates all 100,000 rows.
- Enter/F2 cannot edit a selected cell; Escape commits a draft; invalid input
  overwrites a valid record; Enter commits more than once.
- Arrow, Home/End, PageUp/PageDown and Tab navigation cannot reveal the active row.
- Search dropdown cannot filter, choose by keyboard, handle no matches, dismiss
  on Escape/outside click, or restore focus to its trigger.
- A context menu opens offscreen, leaks pointer/keyboard input to the grid,
  activates disabled actions, or fails to restore focus when dismissed.
- Right click or Shift+F10 cannot open the row context menu.
- Background refresh blocks input, overwrites a newer load, accesses UI from a
  worker, or applies results after cancellation/disposal.
- Loading, retryable error, empty data, invalid edit and dark/narrow states lack
  usable feedback; popup contents are clipped by their originating scroll view.
- Switching light/dark, brand colors or density loses edits/focus/selection, leaves
  popup colors behind, or forces applications to copy component implementations.
- Selected/hovered/disabled/invalid controls use hardcoded colors, type or radii
  instead of semantic theme tokens; keyboard focus is invisible in either mode.

The E2E report must identify the native platform, text/render backend and checks.
Platform-specific native checks run on their owning OS; a macOS pass alone does
not establish Windows/Linux runtime correctness.
