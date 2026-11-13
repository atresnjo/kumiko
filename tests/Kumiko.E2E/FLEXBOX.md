# Flexbox behavior acceptance (before implementation)

Exercise real views through layout, rendering, semantic input, resize and export.
Failure scenarios:

- A responsive card row fails to wrap at its padded content boundary, counts
  absolute overlays in line breaking, loses gaps/margins or fails to unwrap.
- Wrapped columns, reverse lines or reversed main direction reorder identity or
  overlap; RTL reflects asymmetric margins incorrectly.
- A wrapped container's intrinsic cross size ignores later lines or reflowed text.
- Growing items stop at a max size but leave space undistributed; shrinking items
  stop at a min size but force other shrinkable items outside the container.
- Shrink weights are not scaled by basis, a zero weight still shrinks, explicit
  basis loses to width, or percentage basis includes padding.
- Margins collapse, consume the child's hit area, or disappear from intrinsic
  sizes, wrapping, cross alignment or scroll extents.
- Ratio-based cards lose their ratio after grow/shrink, fill or percentage sizing;
  explicit two-axis sizes or min/max constraints are ignored.
- AlignSelf changes siblings, stretch changes a fixed cross size, or baseline
  alignment uses font size instead of font metrics and ignores nested text.
- SpaceAround/SpaceEvenly or wrapped line alignment distribute gaps incorrectly.
- Resize changes keyed native views or clicking a reflowed card hits its neighbor.
- Invalid factors (NaN, infinity, negative shrink, zero aspect ratio) poison the tree.

Retain flexbox-*.png/.txt/.warnings.json and the E2E JSON report. UIKit verification
must use detached views and layer renders; retain flexbox-report.json and PNGs.
