# Flint performance baseline

Measures the real Flint view and Elm runtime with injected real Git services and
throwaway repositories. It creates no windows and sends no system input.
Fixtures and state are removed on completion. No global Git configuration is read.

```
dotnet run --project benchmarks/Kumiko.Flint.Benchmarks -c Release -- artifacts/flint-performance

dotnet publish benchmarks/Kumiko.Flint.Benchmarks -c Release -r osx-arm64 \
  -p:PublishAot=true -o artifacts/aot/flint-bench
artifacts/aot/flint-bench/Kumiko.Flint.Benchmarks artifacts/flint-performance/aot
```

Optionally pass `--idle-pid=1234` for a five-second CPU/RSS sample of an existing
Flint preview process. This reads process counters; it does not interact with the
window. The preview is interactive, and recent filesystem notifications, user
input and other workloads can affect this interval. It is a no-harness-input
sample, not proof of zero idle wakeups or a controlled idle-power experiment.

The fixture uses git fast-import to create a linear 10,000-commit repository and
25-commit comparison tab. Separate repositories contain 5,000 untracked files or
a changed tracked file with 5,000 removed and 5,000 added lines.

Cases:

- Fresh-service 4,000-commit snapshot loads (OS page cache is warm).
- Revalidation of unchanged history with the existing service and graph cache.
- Forced UI frames over 10,000 history commits, 5,000 working changes, and a
  10,000-line diff at 1400x900 scale 1.
- Forced UI frames of the Commit tab for a synthetic 2,000-file commit (as an
  initial import adds) above the 10,000-commit history.
- Real pointer-driven cached tab switching, including Elm update, runtime model
  serialization and settled view frames. Debounced workspace persistence is not
  awaited; it remains a real, asynchronously scheduled effect.
- Real accessibility edits to diff search, including match computation, model
  serialization, reveal and settled frames.

Reports contain all raw timing samples, median, p95, maximum, current-thread
allocated bytes per operation, and final node/paint-instance counts. PNG and tree
artifacts establish the measured scene. Sample loops exclude fixture construction,
Git data import, screenshots and tree exports. Warmup precedes UI measurements;
explicit full GC precedes each case, but samples include naturally occurring GC.

Limitations:

- Single-machine measurements; other machine workloads are uncontrolled.
- Tester measures CPU build/layout/paint preparation, not GPU presentation,
  display latency, native input delivery, or sustained actual frame rate.
- Forced frames do not establish an application's idle wake frequency.
- Allocation counts cover the calling UI thread only. Async Git workers and
  storage tasks allocate on other threads; these figures are not total process
  allocations or total memory per Git operation.
- Git cold means a fresh service without its in-memory cache, not a cold OS cache.
- The parent did not time original GPUIX Flint. Its README performance claims
  must not be treated as measured comparisons against this baseline.
- The history is linear. Branch-heavy graphs, huge worktrees, large binary files,
  network latency and authentication need separate controlled scenarios.

Compare the same case, dataset, dimensions, build mode and machine before/after.
Do not turn a single p95 into a hard performance threshold or remove outliers.
Keep the E2E behavior suite and original-feature parity checks alongside timings.
