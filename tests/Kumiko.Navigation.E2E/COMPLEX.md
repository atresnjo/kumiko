# Nested navigation and overlay behavior

Failure scenarios recorded before the fix:

- Escape closes an outer drawer/dialog instead of the dialog/drawer over it.
- A popover or searchable dropdown inside a stock dialog dismisses the dialog.
- Dismissing an inner layer also dismisses its parent on a rebuild, or leaves its
  parent unable to accept input; Tab or semantic invocation reaches the background.
- Escape dismisses an overlay while its editor is composing text.
- Desktop Back skips a child Ui.Stack even when it supplies onBack, or dispatches
  to both child and root for one input.
- Nested stacks consume Back in outer-first order, fail to bubble at their root,
  or bypass application admission policy after a child rejects Back.
- Sibling stacks receive another stack's Back instead of following keyboard focus.
- A built but hidden stack consumes Back, or an exhausted focused sibling pops
  another sibling instead of bubbling to the enclosing screen.
- A modal built after a child stack lets Back escape to the covered stack; a stack
  inside the active modal cannot handle its own Back.
- Keyboard Back differs from mouse Back; IME input is lost before admission.
- Forward is swallowed by a path-only child stack, removed stacks retain stale
  callbacks, or replay/restore creates navigation messages.

Run the focused offscreen slice:

    dotnet run --project tests/Kumiko.Navigation.E2E -c Release -- artifacts/navigation-complex --complex-only

Retain complex-*.png/.txt/.warnings.json, complex-session.jsonl and report.json.
The fixture uses only Tester, private input and model state, never native windows.
