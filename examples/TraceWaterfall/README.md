# Trace Waterfall

A native desktop recreation of [Flo's trace waterfall demo](https://x.com/flohoeller/status/2112823884689133905)
from [affectUI](https://www.affectui.com/components/trace-waterfall), built independently
with Kumiko views and the Elm architecture. No React, webview or copied Pro source.
Icons use the framework's MIT licensed Hugeicons package.

```sh
dotnet run --project examples/TraceWaterfall -c Release
dotnet run --project examples/TraceWaterfall -c Release -- --dark --complete
```

The illustrative pricing-launch trace plays at four times real time. Research,
writing and coding run in parallel. A failed checkout call remains visible after
its repair and successful retry. These are demo records; no APIs, tools, files,
repositories or payment systems are accessed.

- Pause/resume, restart, or show the completed trace using the toolbar.
- Fold an agent using its chevron; child calls become marks inside its bar.
- Click a row or press Enter/Space to inspect timing, usage, input and output.
  Failed calls show the error. Escape or an outside click closes details.
- Up/Down and Home/End move between visible rows. Left folds a branch or moves
  to its parent; Right opens a branch or moves to its first child.
- The moon/sun button switches themes while preserving replay state.

`Model.cs`, `Msg.cs`, `Update.cs`, `View.cs` and `Subscriptions.cs` form the Elm
app. Models, messages, spans and collapsed IDs are immutable. All timing comes
from the model; the subscription only emits `Tick` and is removed while paused
or finished. The view owns layout and binds controls directly to messages.
`Data.cs` supplies illustrative spans; costs and tokens belong to each call and
roll up independently of branch visibility. No parent contains pre-summed usage.

Desktop support uses Kumiko's macOS, Linux and Windows backends. The window's
minimum size is 840 × 600; smaller desktop windows scroll the trace vertically.
This example does not add platform-specific code or framework API changes.

Verify without opening a window:

```sh
dotnet run --project examples/TraceWaterfall -c Release -- --complete \
  --kumiko-snapshot=artifacts/tracewaterfall/example.png \
  --kumiko-tree=artifacts/tracewaterfall/example.txt --kumiko-dev
dotnet run --project tests/Kumiko.TraceWaterfall.E2E -c Release -- \
  artifacts/tracewaterfall/e2e
dotnet publish tests/Kumiko.TraceWaterfall.E2E -c Release -r osx-arm64 \
  -p:PublishAot=true -o artifacts/aot/tracewaterfall
artifacts/aot/tracewaterfall/Kumiko.TraceWaterfall.E2E artifacts/tracewaterfall/e2e-aot
```

The E2E harness writes PNG/tree/warnings artifacts for live, completed, collapsed,
failed-detail, dark and compact views, a recorded/replayed JSONL session, and
`report.json` with checks and PNG SHA-256 hashes. It checks unchanged rendering
and recorded replay against identical PNG hashes. Acceptance scenarios were
recorded before implementation in `tests/Kumiko.TraceWaterfall.E2E/ACCEPTANCE.md`.
