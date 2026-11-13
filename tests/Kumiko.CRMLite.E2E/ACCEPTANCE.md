# Reusable CRMLite components

Failure scenarios, recorded before extraction:

- A control depends on CrmModel/CrmMsg and cannot render in a different Kumiko host.
- Two keyed instances using identical internal keys share selection or focus,
  route a mapped message to the wrong parent instance, or change identity on reorder.
- A component's private surface styling changes an adjacent plain Div or a sibling
  instance that uses a different palette.
- A disabled icon action still dispatches, or keyboard activation fails after
  styling and interaction move into a reusable component.
- Switching a segmented choice reports a label instead of its typed value;
  choosing the selected option dispatches unnecessarily.
- Compact and settings switches lose their distinct size, thumb or colors.
- Property rows lose caller-supplied content or overwrite neighboring slot keys.
- Existing CRM forms, settings, menus, drawer controls and plan actions stop
  working, or their light/dark/narrow layout or focus behavior changes.

Use the existing full CRM input flows plus a separate Kumiko host using Ui.Child.
Keep PNG/tree/warnings artifacts and a recorded session that can be replayed.
No native windows, real-screen input or system clipboard access is permitted.

## Navigation (failure scenarios before implementation)

Exercise repeated destinations
with independent drafts, late entry-addressed edits, stale Back/confirmation,
unsaved intermediate pages, pending input before Back, cancelled/completed native
pops, model changes mid-transition, scope disposal, restore/replay, covered-page
input/accessibility and deterministic state retention. Retain per-step PNGs,
controller/tree geometry, model-versus-native JSON and replayable sessions.
Desktop baseline screenshots must remain unchanged for existing flows.

### Review failure scenarios (recorded before the fixes)

- Command+[ with the New deal or New company dialog open, settings open or a
  pending confirmation must not navigate or discard the dialog draft.
- Command+[ during an uncommitted IME composition must not navigate; text typed
  in the same frame as Command+[ must reach its entry before the guard runs.
- Editing a draft while "Discard unsaved notes?" is shown cancels that pending
  navigation; confirmation never becomes an inert button. No-op edits keep it valid.
- "Back to companies" always shows Companies: it pops to a Companies root and
  otherwise navigates to Companies. Previous/Next company replaces the visit and
  guards its draft; reopening the current company does not push a duplicate.
- Ordinary bracket text reaches a focused field. Leaving through a guarded
  Companies list change applies the list only after confirmation.
- Restored paths whose root is a company page, or whose pushed entries are not
  company pages, are rejected. Malformed pending confirmations cannot apply.
# Injected authentication and CRM backend

Failure scenarios, recorded before implementation:

- An unknown email opens the workspace, or duplicate submissions start multiple sign-ins.
- A signed-out model retains customer data, dialogs, agent subscriptions or a previous user's identity.
- A late sign-in, load, search or save response restores data after sign-out or an account change.
- Switching from Maya to Daniel leaves "My companies", default owners, note authors,
  avatars or "you" labels tied to Maya.
- A viewer can change CRM data through a form, a board action or a direct message;
  the fake API trusts a caller's altered session permissions.
- A failed load leaves a blank screen without retry; a failed save loses optimistic
  changes or claims they were saved. Refresh or sign-out discards unsaved changes.
- Out-of-order saves overwrite newer edits; signing in again loses saved changes.
- API services are created in a view/update or a replay executes backend work.
- Reusing the auth identity component in the sidebar and settings creates duplicate
  keys, stale identity or inaccessible controls.

Exercise these through the actual CRM host with injected fake services, semantic
input, controlled backend failures and held responses. Retain login, shared-account,
viewer, loading/error PNG/TXT/warnings and a JSONL session that replays without I/O.

## Desktop notifications and native menu (failure scenarios before implementation)

Notifications go through an injected `ICrmDesktop` service; the model decides,
commands deliver, and a subscription returns clicks, activity and capability.

- One agent beat that finishes or fails a plan posts zero or two notifications,
  or every later tick, re-render, replay or restored model posts it again.
- A notification appears while CRM Lite is frontmost, while the preference is
  off, after the platform reported unavailable/denied, or for a viewer's
  read-only workspace; repeated save failures re-prompt for permission.
- A denied, unbundled or hidden host throws into the update loop, leaves a
  blank settings row, or keeps retrying; the user cannot see why nothing arrives.
- Clicking a notification whose plan, deal or company was removed, whose
  account signed out, or which was already closed/clicked routes, activates
  or restores customer data; sign-out leaves customer text in notification history.
- A routed click discards a New deal/New company draft or bypasses the unsaved
  note confirmation; it routes twice when the native click is delivered twice.
- A notification remains after the user opens its plan in the app or a later
  save succeeds; a click activates a hidden/test host.
- Snapshots, tree dumps, agent sessions or E2E tests construct the native
  notification centre, prompt for permission or touch user defaults.
- Menu, toolbar, palette and keyboard shortcut dispatch different messages for
  one command, dispatch twice, or run while a dialog, settings, search palette,
  confirmation or IME composition owns input; editor chords are stolen.
- A native menu item keeps a callback from an old frame, stays enabled while
  its action is disabled for the current account/state, or survives disposal.
- Native menu key equivalents preempt Kumiko's editor/modal routing, or a
  template placeholder silently binds to an ambiguous window.
- Standard App/File/Edit/View/Window/Help roles are missing, or the app menu is
  rebuilt every frame while a plan runs.

Exercise these through the real CRM host (Tester), a hidden App with hidden
window for native menu activation, and a recording fake desktop service. Retain
PNG/TXT and JSON evidence of notices, clicks, activation requests and menus.

### Results (25 October 2026, macOS arm64, pinned .NET 10.0.402)

- Executed: CRMLite E2E 454 checks (393 before, plus `DesktopFlows` and
  `DesktopMenuFlows`) in JIT and NativeAOT, all 86 paired PNGs byte-identical;
  desktop actions 79, desktop 318, core 6,864 and the agent suite in JIT and
  NativeAOT; analyzer fixtures; offscreen `--demo`, `--dark --page=Deals` and
  `--route=people` snapshot/tree runs with no warnings; benchmarks; public API.
  All five NativeAOT publishes have no compiler, trimming or AOT warnings.
- Hidden native: the CRM window in a hidden App with the real menu template,
  AppKit `performActionForItemAtIndex:` activation, installed key equivalents
  and teardown; `NativeCrmDesktop` reporting Unavailable in a hidden App.
- Compiled only: CRMLite and desktop actions for linux-x64 and win-x64, and
  CRMLite.iOS for the simulator. Windows/Linux menus and notifications did not run.
- Not exercised: real notification banners, permission prompts, clicks from
  Notification Center, activation, visible menu tracking and the packaged .app.
- Unchanged baselines: the solution fails only on the eight unmigrated examples
  (KUM011/MSB3073, zero warnings); the example warning check stops at the
  missing Gallery binary. Formatting passes excluding RS0016/RS0041.

## Held commands (failure scenarios before implementation)

`HeldCommandFlows` must fail if a
held sign-in still reaches the backend or the busy form accepts a second
submission; a failed sign-in or workspace load is not shown in the page and the
workspace status; a settled or replaced command can still answer; a fixture
answer bypasses the request-id checks that discard stale responses; a superseded
company search can fill the picker; or a released command cannot reach the real
backend. Retain held-{signing-in,loading,load-failed,company-search} PNG/tree/warnings.
