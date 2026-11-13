# Vendored Yoga.Net

This directory is a modified copy of [chenrensong/Yoga.Net](https://github.com/chenrensong/Yoga.Net)
3.2.3, source commit 1c7fd4a1ede445f5a3f9d5ae9239364b09d25154, a managed C# port
of Meta's Yoga. It is MIT licensed; the notice is preserved in
[Yoga.Net-MIT.txt](../../../third_party/licenses/Yoga.Net-MIT.txt) and packed
with Kumiko. The `Facebook.Yoga` namespace is kept so upstream changes can be
compared file by file. All types are internal to Kumiko.

## Changes from upstream

The core is synchronized with [react/yoga](https://github.com/react/yoga) at
843dba8619420992e492bbb4b3d2f63d9ad6c683 (21 October 2026), adapting applicable
fixes to managed storage. C++ memory/lifetime mechanics, bindings and value-pool
optimizations do not apply. The original C# port's defaults for direction,
alignment, shrink and pixel scale remain; the sizing corrections below are
intentionally enabled by default.

- `StyleLength`, `LayoutableChildren` and flex-line enumeration are value types
  or reused buffers, so Yoga scratch does not allocate per pass.
- `Style` stores lengths and numbers directly instead of in a compact value pool,
  normalizing values exactly as the pool did. `Style.Equals` compares values;
  upstream treated any two defined numbers as equal.
- Event payloads, and the per-pass measure-reason counters, are created only
  while an event subscriber is registered.
- Indexed style setters compare without boxing.
- `Config.RelayoutBoundaries` (Kumiko enables it): an absolutely positioned flex
  box with point width and height cannot change size or position because of its
  content, and absolute boxes never contribute to an ancestor's size or baseline.
  Dirtiness from descendants therefore stops at such a box, which is laid out
  again alone, with the constraints of its last layout, after the root pass. A
  boundary's own style changes still propagate. Pixel-grid rounding needs
  ancestor offsets, so boundaries and the skipped rounding walk require a point
  scale factor of zero.
- Files over 600 lines are split into partial classes, and step 8 of
  `CalculateLayoutImpl` is the unchanged method `AlignMultilineContent`.

## Verifying changes

The upstream xUnit suite is not part of this repository. To repeat it, copy
`tests/Yoga.Net.Tests` from the commit above into a temporary directory, replace
its project reference with `<Compile Include="<repo>/src/Kumiko/Yoga/**/*.cs" />`
and remove `StyleValuePoolTest.cs` and `SmallValueBufferTest.cs`, whose types no
longer exist. Before this synchronization, all 808 remaining tests passed (35 skipped upstream).
That suite encodes legacy sizing; current acceptance is covered by the engine
and rendered-view scenarios below.

Relayout boundaries were checked differentially: identical random trees laid out
with boundaries on and off, through random text, style and child mutations. Yoga
results also depend on cache history once margins or baseline alignment are
involved: incremental layout differs from a fully dirtied layout there with or
without boundaries, and boundaries add no further difference from it. Without
those features the boundary, incremental and fully dirtied layouts are identical.

## Upstream synchronization, October 2026

- aa6ede70 / 433d4634: guard near-zero and positive residual shrink totals.
- 4a4e7810: tolerate float noise when deciding whether a flex line wraps.
- d81d9c02: recount contents children when an attached child changes display.
- 033064fe: key measured/layout caches on percentage-dependent owner sizes,
  including absolute relayout boundaries and subsequent descendant changes.
- 667da3cf: include box sizing in style equality.
- 96fc051d / b7ff95d2: only flag contents/hidden nodes for actual layout;
  propagate absolute descendant updates through boxless ancestors.
- 4a59d094 / db7fc6d8 / 3b372324: content-based automatic minimum size,
  padding/border-aware measured leaves, static/dynamic min-content declarations,
  and per-axis/per-generation memoization. Container memos also key owner width.
- 73bebc0d: first-pass flex distribution uses original totals for every sibling.
- 98806ccf / 6ac92f4c / bd8fe0d6: double-precision rounded edge differences,
  whole-pixel text dimensions and ownership-aware rounding traversal.
- c64d0609 / 1dc925c5: replace the early `FixFlexBasisFitContent` implementation
  with the conservative scroll-subtree independence proof. The internal
  experimental flag remains off in Kumiko; both states are behavior-tested.
- 966107fd / f6206ec3: clone contents descendants before mutating shared trees,
  clean up absolute contents paths, and invalidate parents when children are freed.
- 98300847: allocate Grid track lists lazily, preserving independent style clones.

Upstream's two new legacy errata bits are intentionally omitted. Automatic
minimums and original-total flex sizing are always active. Kumiko's existing
`AbsolutePercentAgainstInnerSize` setting continues to define absolute percentage
resolution; it cannot accidentally disable these new corrections.

The adapter supplies an explicit min-content callback: whitespace wrapping uses
its longest unbroken word instead of its emergency grapheme-break width. Icons
contribute their rendered dimensions instead of measuring their symbol identifiers. These
answers are reused for unchanged text/style/font metrics and participate in text-cache invalidation, so equal ordinary measurements
cannot hide a changed word minimum. Unspecified view minima map to automatic;
explicit zero minima and hidden/scroll overflow opt out per item. Scroll viewports
also declare zero min-content on their scroll axis so recursive ancestor probes
do not turn a viewport into its entire scrollable content.

Run `tests/Kumiko.E2E` with `--yoga-upstream-only` for the focused acceptance
flow, or `--flexbox-only` / the full suite. `yoga-upstream.json` records each
scenario, and `yoga-upstream-{initial,changed,resized}` PNG/TXT/warnings files
show actual Kumiko rendering. The same flows run under NativeAOT.
