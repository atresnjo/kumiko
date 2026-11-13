# Hugeicons free icon package

Failure scenarios recorded before implementation:

- A packaged glyph is absent, blank, clipped, or replaced by the built-in missing
  symbol box. Enumerate and render the entire bundled catalogue through Context.Icon.
- Paths, circles, ellipses, rectangles, lines, polygons or polylines lose their
  geometry or rounded stroke attributes during import.
- Icon coverage is black on black or keeps a fixed source colour instead of
  taking the requested foreground; switching theme reuses the wrong tint.
- Reusing an icon after a scale change keeps a low-resolution raster; atlas
  resets lose the glyphs. Exercise the same context at multiple scales.
- A Kit button/menu rejects package icons, loses its accessible name, or can no
  longer be activated through Kumiko input.
- An unknown catalogue name silently draws a placeholder; a malformed registered
  icon bypasses SVG validation; prefixes collide with another source.
- Trimmed/NativeAOT or NuGet consumers lose the embedded SVG resources or their
  registration. The package must contain the full upstream MIT notice and SVGs
  for optional web consumers, without requiring Node or a network at runtime.
- CRMLite's favorite and Open company menu actions regress while replacing their
  glyphs. Run the existing CRMLite behavior flows in light and dark modes.

Icon subset (`KumikoHugeicon` items) failure scenarios, recorded before implementation:

- NativeAOT keeps every manifest resource, so an app that draws thirty icons still
  ships all 6,067 SVGs. Measure the published binary with and without a subset.
- A misspelled subset name builds and only fails when the icon is first drawn.
  Unknown names must fail the build, naming the icon and the catalogue location.
- A subset changes behaviour for apps that set no items: they must still list,
  resolve and render the entire catalogue, in JIT and NativeAOT.
- Under a subset, `Names` still lists removed icons, or `Get`/`GetSvg`/drawing an
  unselected icon returns a blank, a placeholder or another icon. It must throw
  `KeyNotFoundException` naming the icon and `KumikoHugeicon` in every
  configuration, not only after trimming, so JIT runs match the published app.
- The resource removal list is written somewhere other than obj, depends on item
  order, or produces trimming/AOT warnings. NuGet and in-repo consumers differ.

Retain hugeicons-catalog.png, hugeicons-catalog.json, hugeicons-tint-*.png,
hugeicons-kit.png, hugeicons-star.svg, the E2E report and CRMLite PNG/TXT/JSON
artifacts. Run the same E2E executable under NativeAOT. Linux and Windows use
their own native SVG backends in CI; local macOS artifacts do not verify them.
