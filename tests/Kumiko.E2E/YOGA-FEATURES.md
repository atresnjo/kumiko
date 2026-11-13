# Yoga style expansion acceptance (before implementation)

Exercise the shared view on desktop Tester and detached UIKit. Preserve PNG,
TXT/geometry JSON and report artifacts for phone, wide, RTL and visibility changes.

Failure scenarios:

- Auto margins fail to consume free space, percent margins/padding/gaps use the
  wrong containing size, or logical start/end fail to mirror with RTL.
- Percent min/max constraints are ignored, stale after resize, or cannot be reset
  by the existing pixel APIs. Typed edges retain old values after rebuilding.
- Relative insets move siblings, static insets move their own box, absolute
  trailing insets fail to stretch, or a static ancestor incorrectly becomes the
  containing block. Explicit containing blocks fail to bound absolute descendants.
- Display-none descendants paint, take pointer/native/accessibility actions, keep
  scroll range or produce hidden-layout warnings. Restoring them loses identity.
- Display-contents contributes its own box, spacing or paint, offsets descendants
  twice, breaks RTL/native parenting, or loses semantic grouping.
- Content-box dimensions fail to include padding/layout borders; existing painted
  borders unexpectedly start consuming layout space.
- A reference baseline is ignored, a flex shorthand cannot reset, or overflow
  modes fail to update paint/hit clipping.
- New styles share mutable state between contexts or leave cached Yoga properties
  behind when changed/removed. Native measurement or actions leave the main thread.
- An advertised API is exposed but has no working layout implementation. Probe
  Grid tracks and intrinsic sizing against package 3.2.3 before claiming support.

Run existing desktop core/component/CRM behavior flows and all detached UIKit
flows, inspect representative existing screenshots as well as the feature scene,
and repeat the layout slice under NativeAOT. Preserve existing diagnostic failures
separately; never refresh warning suppressions automatically.
