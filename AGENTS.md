# Kumiko C#

## Working rules

- Every source, script and documentation file is at most 600 lines. Verbatim
  license texts under third_party/licenses are exempt.
- Never show windows, activate applications, drive the real screen, synthesize
  system input, or capture the whole screen while developing. Use Tester,
  --kumiko-snapshot, hidden windows, and offscreen rendering.
- Tests use their own clipboard. Native integration tests never access the
  user's clipboard, defaults, documents, or other application state.
- UI and platform APIs run on the main thread. Use App.Dispatch for worker
  results. Retained Objective-C objects and native callback contexts have
  explicit lifetimes. Do not let exceptions escape unmanaged callbacks.
- Idle apps must block on the native event loop. Timers and descriptor sources
  wake it when needed; never add a polling render loop.
- Keep macOS, Linux and Windows building. NativeAOT publishing must produce
  no compiler, trimming, or AOT warnings.
- Prefer end-to-end behavior tests and repeatable PNG/JSON artifacts. Never
  add tests that merely restate the implementation. Write failure scenarios
  before isolated-system implementation; do not add unit tests after code.
- Examples with desktop and iOS views (CRMLite) keep both in step: a feature or
  route is not done until both views and their checks cover it.
- No PR, push, merge, deployment, release, tag or workflow dispatch unless
  explicitly authorized by the user. Deployments require specific confirmation.
