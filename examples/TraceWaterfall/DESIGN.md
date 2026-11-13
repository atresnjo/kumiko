---
name: Trace Waterfall
description: Native Kumiko + Elm desktop trace inspection example
colors:
  page-light: "#f8f9fa"
  page-dark: "#141416"
  surface-light: "#ffffff"
  surface-dark: "#19191b"
  text-light: "#202124"
  text-dark: "#f1f1f3"
  muted-light: "#696a73"
  muted-dark: "#a0a0a9"
  border-light: "#e7e8eb"
  border-dark: "#303034"
  grid-light: "#f1f2f4"
  grid-dark: "#222225"
  hover-light: "#f0f6ff"
  hover-dark: "#242b36"
  blue-light: "#2565b9"
  blue-dark: "#8ab8ff"
  llm-light: "#7963b7"
  llm-dark: "#b4a3f9"
  llm-fill-light: "#eeeafb"
  llm-fill-dark: "#2d2843"
  tool-light: "#397f90"
  tool-dark: "#6ac2d3"
  tool-fill-light: "#e5f3f6"
  tool-fill-dark: "#1e343c"
  agent-fill-light: "#f0f0f1"
  agent-fill-dark: "#29292c"
  danger-light: "#b42318"
  danger-dark: "#ff9b95"
  danger-fill-light: "#fdeceb"
  danger-fill-dark: "#3d2224"
  slow-light: "#914509"
  slow-dark: "#ffb16c"
  slow-fill-light: "#fff1e3"
  slow-fill-dark: "#3a2b21"
typography:
  heading: {fontSize: "18px", fontWeight: 600}
  body: {fontSize: "14px", fontWeight: 400}
  caption: {fontSize: "12px", fontWeight: 400}
  metric: {fontSize: "23px", fontWeight: 500}
rounded:
  bar: "4px"
  control: "6px"
  inset: "8px"
  inspector: "10px"
  trace: "16px"
spacing:
  small: "8px"
  content: "12px"
  section: "16px"
  inspector: "18px"
  column: "24px"
  page: "28px"
---

# Design System: Trace Waterfall

## Overview

[Flo's source demo](https://x.com/flohoeller/status/2112823884689133905) is the visual authority; captured reference media lives in `artifacts/tracewaterfall/reference/`. This document records the built example only. Its quiet neutral split hierarchy/time chart, six metrics, violet LLM calls, teal tools, live cursor, folding and floating inspector preserve that reference's structure. MIT Hugeicons replace provider logos.

This is an independent C# desktop implementation using Kumiko views and immutable Elm Model/Update/Subscriptions, with the framework's macOS, Linux and Windows backends. The pricing-launch trace is illustrative; its displayed providers and commands perform no external work. The finish review accepted fidelity; the keyboard fix received a fix-scoped ship verdict.

## Colors

`Palette.cs` supplies the paired light/dark tokens above. Pale page and white surface become charcoal page and slightly lighter surface; primary text, muted metadata, borders and grid remain distinct.

- Violet tint/fill identifies LLM calls; teal tint/fill identifies tools. Agent bars use neutral fill and muted outlines. Normal bar outlines use the kind tint at 32% alpha.
- Blue identifies status, running-clock icons, keyboard focus and the current-time line (60% alpha). Hover and the inspected row share the hover fill.
- Red overrides failed bars, duration/error icon and the nonzero error metric; the inspector uses the danger fill for its error block. Warm amber marks the slowest completed non-agent call.

## Typography

The example inherits Kumiko's platform system font; it specifies no custom family, monospaced face, tracking or line-height. Values are logical UI pixels. The toolbar title and “Trace” use the heading token; the prompt uses the same size at regular weight. Agent names are semibold; call names, provider details and bar captions use body. Uppercase metric/axis labels and footer use caption; metric values use metric. Toolbar labels and inspector prose are 13px; inspector title is 16px semibold, small timing labels and SLOWEST are 10px. Wrapped inspector input/output remains proportional text.

## Layout

The initial window is 1360 × 1040 with an 840 × 600 minimum. The page uses the page inset and section gap: toolbar (34px), growing trace panel, then a small footer. The panel stacks header (62px), six equal metric columns (94px total height, 24px horizontal padding per column), axis (40px), and vertically scrolling rows (32px).

The label column is 34% of the inset-adjusted window width, clamped to 280–430px; the chart takes the remainder. Each tree level adds 13px with a fine vertical guide. Bars are 18px tall, offset 9px from row top, with a 2px minimum width; duration/cost captions follow at a 9px gap. Five grid positions share the same model-derived time range across the axis and every row. The range expands in steps as replay advances.

The compact artifact is 920 × 720: the split and metrics persist while rows scroll. Actual limits: provider/detail strings clip without ellipsis, short live traces retain the tall growing panel, and the current-time cursor is a plain line without a dot.

## Elevation & Depth

The trace panel is flat, separated by tonal contrast and a fine border. The inspector alone floats above the selected row: the Kit popover uses shadow parameters (blur 16, vertical offset 6, black alpha 12% light / 45% dark), a border and the inspector radius. It prefers top/end alignment, flips when necessary and clamps within an 8px window margin. Its width is 408px with inspector padding; timing/error blocks use inset radius and content padding.

## Shapes

The outer trace clips to its trace radius. Toolbar/status/icon tiles use control radius; bars, chevrons, close control and SLOWEST use bar radius. Provider-role icon tiles are 25 × 25px with a border and 16px Hugeicon. Toolbar targets are 32px high; fold targets are 24 × 28px. The status badge is 27px high with a 6px blue dot.

## Components

- **Toolbar and status:** named buttons pause/resume, restart, complete and switch theme. Pause/resume and complete disable at the final time. Restart clears inspector/folds and starts at zero, retaining theme. Theme changes preserve replay state. Status exposes Live, Paused or Done as a named Group; each metric is a named Group with its current value.
- **Tree and folding:** “Agent trace” has Tree role; span rows have TreeItem role, accessible name, kind/status/duration description, level and branch expanded state. Chevron buttons have explicit Expand/Collapse names. Folding hides only descendants, preserves nested folds, and places non-agent descendant marks inside the parent bar. Totals include hidden descendants; the original failure remains after repair/retry.
- **Identity:** rows are keyed by immutable span ID; descendant marks use child IDs and controls use stable local keys (`play`, `restart`, `complete`, `theme`, `chevron`, `inspector`). The two `web_search` rows remain separate focus targets despite matching display names.
- **Keyboard and focus:** Tab traverses controls; Enter/Space opens a focused row. Up/Down traverses visible rows; Home/End selects the first/last. Left folds an open branch or moves to its parent; Right expands a folded branch or moves to its first visible child. Tree navigation stops while details are open. Hover uses a hand cursor and hover fill; keyboard-visible focus adds a 2px blue border.
- **Inspector:** the named popover Group shows title/kind/provider, duration/start/status, available usage, input, completed output and actionable failure text. Focus enters the panel and Tab remains inside. Escape, an outside click or the named Close details control dismisses it; focus returns to the originating span ID through the model's restore-focus state.
- **Replay and motion:** initial time is 5.1s; the illustrative run ends at 92s. A 100ms subscription emits Tick, advancing model time by 400ms (4× playback). Paused/completed states remove the subscription; queued paused ticks do nothing. Bar position/width, captions, range and cursor use `Motion.Quick` (100ms); the inspector uses Quick pop-in/fade-out. Framework reduced motion makes transitions immediate while preserving trace progression and controls; artifact tests explicitly enable it.
- **Record/replay and evidence:** the offscreen E2E saves Elm messages to `session.jsonl`; replay with the same viewport/reduced-motion setting must match the recorded PNG SHA-256. Existing `artifacts/tracewaterfall/e2e/` and `e2e-aot/` contain live/light/dark/compact/collapsed/failed-detail PNGs, trees, warnings and `report.json` (26 passing checks each). Local macOS JIT and NativeAOT verification passed; `artifacts/tracewaterfall/core-e2e/report.json` records 6,779 passing core checks. These are existing results, not fresh runs by this documentation pass; Windows/Linux runtime verification is not claimed. Repeat commands are in [README.md](README.md).

## Do's and Don'ts

- **Do** preserve the aligned hierarchy/time split, neutral agent bars, semantic call colors, six metrics, failure history and floating inspection in both themes.
- **Do** keep timing and usage derived from immutable model data and focus tied to stable span identity; verify using offscreen PNG/tree/JSONL artifacts.
- **Don't** replace Hugeicons with provider logos, imply real service activity, or describe this native example using browser/CSS/mobile contracts.
- **Don't** treat the three documented polish limits as implemented refinements or extend this example's design rules to the repository root.
