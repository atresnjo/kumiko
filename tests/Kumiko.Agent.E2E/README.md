# Agent behavior checks

Run after building the solution with the pinned .NET SDK:

```sh
dotnet run --project tests/Kumiko.Agent.E2E -c Release -- artifacts/agent-readiness/implementation
```

The C# harness creates companies/deals through semantic actions, then repeats the
flows through actual NSAccessibility selectors on macOS. It also checks modal
isolation, repeated expansion, removed controls and offscreen selection. Owned
native windows remain hidden and have no system clipboard delegates. The form
fixture checks Enter, Primary+Enter, multiline editing, IME, disabled submit and
nested forms. It retains PNGs, semantic snapshots, actions and Kumiko recordings.

`ExternalAX.swift` is an optional cross-process macOS probe. Compile it with
`swiftc tests/Kumiko.Agent.E2E/ExternalAX.swift -o artifacts/agent-readiness/external-ax`
and set `KUMIKO_AGENT_AX_CLIENT` to that absolute executable path when running the
C# harness. It never requests accessibility permission. A trusted process may
still see no controls in a hidden app; that result is recorded as **blocked**,
not passed. The normal harness verifies provider calls in-process and does not
claim visible-window, screen-reader or external AX coverage.

Exit codes are 0 for pass, 1 for failure and 2 for an optional blocked external
probe. `scenarios.json` and the main E2E acceptance document record the motivating
failure scenarios. All artifacts live under the supplied output directory.
