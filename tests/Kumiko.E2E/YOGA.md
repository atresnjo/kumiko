# Yoga.Net migration acceptance (before implementation)

Replace the custom solver with the pinned managed Yoga.Net package. Exercise the
same Context through real views, rendering, semantic input, resizing and replay.
Existing FlexboxChecks, FlexboxEdges, localization, scrolling and application
flows remain the geometry and interaction contract.

Additional migration failure scenarios:

- Reusing a layout node caches old text, font metrics or wrapping constraints;
  changing text or the window width leaves following content overlapping.
- A rebuilt tree removes, reparents or changes a text leaf into a container, but
  Yoga retains old children, callbacks, alignment or dimensions.
- Moving between wrapping/reversed/absolute and ordinary flow retains stale Yoga
  properties, loses keyed semantic identity or moves input away from paint.
- Scroll offsets affect Yoga's content sizing, omit trailing margins or apply
  twice; follow-end and virtual lists no longer reach their final row.
- Yoga's default stretch/shrink, percentage containing box, pixel rounding or
  RTL behavior changes Kumiko's documented defaults without an explicit change.
- Pooling keeps removed text/native payloads alive, or separate Context instances
  share measurement state. Native text measurement leaves the owning UI thread.
- The package is missing from packed consumers, introduces native libraries, or
  emits compiler, trimming or NativeAOT warnings.

Retain yoga-*.png/.txt/.warnings.json, the E2E report, before/after benchmark JSON,
Gallery offscreen exports and NativeAOT E2E artifacts. Native UIKit verification
uses detached views only; no visible windows or system input.
