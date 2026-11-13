# CRMLite

A light CRM in the style of Attio's Companies, company record and Deals board.
It uses its own fictional workspace ("Harbor"), team, companies and deals. A fake
C# backend owns the in-memory data. The clock is fixed at Tuesday 20 October
2026, 2:30 PM, so relative times ("3h ago", "Yesterday") and snapshots are repeatable.

```sh
dotnet run --project examples/CRMLite -c Release
dotnet run --project examples/CRMLite -c Release -- --demo --dark --page=Deals
dotnet run --project examples/CRMLite -c Release -- --demo --company=northwind \
  --kumiko-snapshot=artifacts/crmlite.png --kumiko-tree=artifacts/crmlite.txt --kumiko-dev
dotnet run --project tests/Kumiko.CRMLite.E2E -c Release -- artifacts/crmlite
```

Sign in with `maya@harbor.app` or `daniel@harbor.app` to edit, or
`nina@harbor.app` for read-only access. The sign-in form also offers account
shortcuts. This is deliberately fake auth: no passwords, credentials or network
server. `--demo` starts with Maya's preloaded session for snapshots and agents.

The sidebar shows loading, saving and error status, Refresh data and Sign out.
Settings repeats the same identity in its fixed sidebar. Switching accounts
changes My companies, owner defaults, avatars, “you” labels and activity authors.
Edits autosave after 100 ms of quiet; signing out and back in keeps them for this
process. Restarting creates a fresh backend. Personal settings stay local to the
current session; the backend document contains CRM records and favorites.

A failed save keeps the edited document and offers Retry save. Refresh and
sign-out wait for unsaved changes, and expired sessions clear private data.
Viewers can inspect records but cannot create, annotate or move them. The backend
checks its own session registry rather than trusting permissions sent by a client.

- **Companies** lists 23 accounts with stage tags, ARR, headcount, owner, last
  interaction and next step. Click a header to sort; numbers and dates sort
  largest first, text A to Z. Interactions older than 14 days turn orange. The
  All / My companies / Hot tabs, search field and Stage and Owner menus filter the
  table, and the footer counts the rows and sums their ARR.
- **A company** shows its activity timeline grouped by day: emails, notes,
  meetings, calls and stage moves. Type in "Add a note" and press Enter to add a
  note. Deals, People and Notes tabs list the rest. The details panel changes the
  stage and owner, and switches the hot flag. The star adds the company to the
  sidebar favorites. Previous/next follow the current table order.
- **Deals** is a board of every deal by stage. It shows the open pipeline, the
  weighted forecast, the amount closing in 30 days and the won total, plus a bar
  of each stage's share. Drag a card to another lane to move its deal; the stage
  menu in a company's details panel does the same from the keyboard. Deals
  closing within a week read "In 3 days".
- **New deal** and **New company** open forms with selects for company, stage
  and owner. The value field accepts "$120k", "120,000" or "1.2m". Errors appear
  after the first submit. Enter in a field or Primary+Enter creates the record;
  Escape closes an open select first, then the dialog.
- **Sidebar**: the button at the left of every page header collapses it to a
  52 px icon rail and back. One eased value (`Motion.Gentle`) drives the width;
  labels, counts and headings clip and fade out early on collapse and fade in
  late on expand, so icons never move. Rail icons show their names as tooltips
  and headings become short rules. Drag the sidebar's right edge, or focus it
  and use the arrow keys or Home/End, to set its width between 200 and 360 px; the
  width follows the pointer without easing and survives collapsing.
- **Settings** open from the avatar at the bottom of the sidebar: one page that
  scrolls through General, Appearance, Notifications, Security, Members and
  Billing. The nav jumps with an eased scroll (`Div.Reveal`) and follows you as
  you scroll, sliding its highlight to the section at the top. Search narrows the
  page to matching settings. Theme, accent color, workspace name and the
  collapsed sidebar apply live; Escape closes an open select, then the sheet.
- **Dragging a deal** lifts the card under the pointer with a deeper shadow.
  The lane under it tints and opens a slot while the other cards slide aside;
  on release the card glides into the slot, and the forecast updates. Drag
  within a lane to reorder it; Escape sends the card back.
- **Agent plans**: Plan with agent in a deal's drawer or card menu drafts a plan
  for the deal. Press a step to edit it, drag a row (or Alt+Up/Down) to reorder
  it, remove or add steps, and Keep planning for the agent's suggestions. Approve
  plan and the agent works through it: each step's work lines rise in as the
  step grows, finished steps fold away with their time, the agent adds a step of
  its own, and booking the meeting fails once to show Retry and Skip. Finishing
  the forecast step moves the deal on the board. Closing a running plan leaves a
  progress toast (`Kit.Toaster`); click it to reopen the plan or swipe it away.
  Plans tick every 700 ms only while one is running; tests send the ticks.
- **Search** (Primary+F) jumps to any company, person or page. Inbox opens a
  thread's company and marks it read; People lists every contact. The account
  menu at the bottom of the sidebar switches dark mode.
- **Desktop menu and shortcuts**: one typed action set ([`Commands.cs`](Commands.cs))
  drives the keyboard shortcuts and the native menu: Settings and Notification
  Settings in the app menu; New Company (Primary+N), New Deal (Primary+Shift+N),
  Save Changes (Primary+S), Refresh Data (Primary+R), Close Window and Sign Out in
  File; the standard edit roles plus Search… (Primary+F) in Edit; Hide/Show Sidebar
  (Primary+\\), Dark Appearance and full screen in View; Inbox, Companies, People and
  Deals (Primary+1–4) and Back in Go; the standard Window and Help menus.
- **Desktop notifications**: while CRMLite is in the background, an agent plan
  that finishes or fails and an autosave that fails post one notification each.
  Clicking a plan notice opens its company and plan; see below.

Stage and ARR are derived: a company takes the stage of its latest deal, and its
ARR is the sum of its deals. A stage change is recorded on the timeline. Logos
and avatars are drawn from initials. App icons use the optional
[`Kumiko.Icons.Hugeicons`](../../src/Kumiko.Icons.Hugeicons/README.md) package's
free Stroke Rounded SVGs on macOS, Linux and Windows. The favorite star and
Open company diagonal arrow use the same glyphs on every platform, with tinting
from the app palette. The upstream MIT notice ships with the package.

The app is a standard five-file Kumiko program (`Model.cs`, `Msg.cs`, `Update.cs`,
`View*.cs`, `Subscriptions.cs`). `Queries.cs` holds derived values and
`Format.cs` the money and date formatting; views receive only the model.

[`Styles.cs`](Styles.cs) groups reusable C# 14 styles for action buttons, form
fields, table rows and raised/outlined surfaces. Styles take the current
`Palette` when they need colors, configure the supplied `Div` and compose with
ordinary setters. For example, settings reuse the dialog field surface with
their own dimensions and corners:

```csharp
using var field = c.Div(key: "select").FieldSurface(p)
    .Width(220).Height(36).Rounded(Palette.Corners.Card);
```

`PrimaryAction` composes `ControlRow`. Styles modify the supplied node explicitly;
they do not cascade into another component.

[`Parts.cs`](Parts.cs) and [`Parts.Timeline.cs`](Parts.Timeline.cs) hold the parts
the desktop and [phone](../CRMLite.iOS/README.md) views share: label, rule, dot,
company logo, avatar, stage tag, deal card and activity timeline. Each takes a
`PartScale` ([`Parts.Scale.cs`](Parts.Scale.cs)); desktop sizes live in
`View.Parts.cs`, phone sizes in CRMLite.iOS `Harbor.cs`. The desktop board adds the
card shadow at its call site, because native phone views draw no shadows.

[`Components`](Components) contains reusable controls that take values and typed
messages instead of the full CRM model. `ActionButton` centralizes primary,
quiet and icon actions. `ToggleSwitch` owns compact/settings variants and private
track/thumb styles. `SegmentedChoice` reports typed values with stable option
keys, removing label parsing from the theme and card-tilt settings. `PropertyRow`
shares a label/value-slot renderer between company details and the deal drawer.
Each component owns its child scopes and feedback; the caller supplies its root
key, current palette and message bindings. These are controlled views; the CRM
keeps their values in its existing Kumiko model. Components with independent update
behavior can use the framework's `IKumikoComponent` and `Outcome.Fold` pattern.

The [component E2E host](../../tests/Kumiko.CRMLite.E2E/ComponentFlows.cs) uses the
same controls with different messages and sibling palettes, maps each instance
through `Ui.Child`, reorders them and replays their recorded session offscreen.

Views use `Ui<CrmMsg>` bindings and scoped forms. `Model.Path.cs` stores identified navigation visits with per-visit tabs and note
drafts. `Model.Navigation.cs` declares typed screens and overlays; dialogs own
their drafts and pickers. Generated `ScreenRoutes` maps routes to typed pages,
JSON tags and relative paths; `Screen`, `Popup` and draft properties are
read-only projections. Back checks revisions and every removed
draft, and restores retained page state. Pre-library CRM models and recordings
retain their JSON tags and numeric message kinds; frozen fixtures verify loading.

[CRMLite.iOS](../CRMLite.iOS/README.md) shares this application logic with native
company list/detail/note views.

For persistent agent control without opening a window:

```sh
dotnet run --project examples/CRMLite -c Release -- --kumiko-agent
dotnet run --project tests/Kumiko.Agent.E2E -c Release -- artifacts/agent-readiness/implementation
```

The JSONL agent protocol shares the native semantic
action dispatcher. The checks create companies and deals, reveal offscreen
options, reject background/stale actions, and retain tree, session and PNG evidence.

## Injecting services and sharing authentication

[`Program.cs`](Program.cs) and [`Desktop.Host.cs`](Desktop.Host.cs) are the
composition root. One backend implements both contracts, registered explicitly in
a per-program service environment with the host's desktop shell:

```csharp
var backend = new FakeCrmBackend();
var services = new KumikoServices()
    .Add<IAuthService>(backend)
    .Add<ICrmApi>(backend)
    .Add<ICrmDesktop>(desktop); // NativeCrmDesktop, or OfflineDesktop offscreen
using var program = CrmApp.CreateProgram(CrmApp.Init, services);
```

`FakeCrmBackend.Services()` registers an `OfflineDesktop` for tests and other hosts.

[`Services.cs`](Services.cs) defines `IAuthService`, `ICrmApi`, the immutable
workspace response and the fake implementation. Replace those registrations with
real adapters to use HTTP or another identity provider. Real credentials should
remain inside the adapter; `AuthSession.Id` here is only a fake demo handle that
is safe to record in session artifacts. Hosts own service lifetimes.

[`Update.Auth.cs`](Update.Auth.cs) describes sign-in, loading, autosave, retry and
sign-out with `Cmd<AuthMsg>.Run<IAuthService>` and `Debounce<ICrmApi>`. The runtime
resolves services on workers and sends immutable result messages back to the UI
thread. Request IDs reject responses from an old account; document revisions and
keyed cancellation keep a slower autosave from replacing newer edits.
[`HeldCommandFlows`](../../tests/Kumiko.CRMLite.E2E/HeldCommandFlows.cs) holds these
commands and answers each one from the test.

The app's auth update overload implements `IKumikoComponent<CrmModel, AuthMsg>`.
The root wraps its events in `CrmMsg.Authentication` and maps commands back to the
parent; Kumiko generates the child union's replay/AOT metadata. No services are
stored in the model or resolved while rendering.

[`Model.Auth.cs`](Model.Auth.cs) holds the single session snapshot. All screens,
including retained company visits, read `CurrentUser` and `CanEdit` from it.
Sign-out clears the complete private model and retained navigation, so returning
as another account cannot expose a previous visit's drafts or running plans.

[`Components/AuthForm.cs`](Components/AuthForm.cs) takes AuthFormProps and emits typed
AuthMsg values. [`Components/AccountIdentity.cs`](Components/AccountIdentity.cs)
takes identity values and renders in both sidebar and settings. Neither accepts
the CRM model or resolves a service. [`View.Auth.cs`](View.Auth.cs) uses `Ui.Child`
to map AuthForm outputs into the CRM message union.

[`AuthFlows.cs`](../../tests/Kumiko.CRMLite.E2E/AuthFlows.cs) and
[`BackendFlows.cs`](../../tests/Kumiko.CRMLite.E2E/BackendFlows.cs) drive the real
views offscreen, replacing services to hold responses and fail requests. The E2E
output includes `auth-*.png`, trees, warning reports and `auth-session.jsonl` with
an effect-free replay. Existing company, board, component and navigation flows
still run with their own seeded backend instances.

## Desktop shell: menu and notifications

[`Desktop.Host.cs`](Desktop.Host.cs) routes `--kumiko-snapshot`, `--kumiko-tree`,
`--kumiko-audit`, `--kumiko-agent`, `--kumiko-replay` and `--kumiko-step` runs
through `Dev.Run` with an `OfflineDesktop`; they never create native shell
services. An interactive run owns its `App`, installs `CrmMenu.Template` with
`App.SetMenu` and binds a [`NativeCrmDesktop`](Desktop.Native.cs) over Kumiko's
`Notification`. `--demo`, `--dark`, `--route`, `--page`, `--company`,
`--kumiko-dev`, `--kumiko-debug`, `--kumiko-record` and hot-reload persistence work
as before.

**Menu.** [`CrmView`](View.cs) binds `CrmApp.Actions(model)` once per render with
`Ui.DesktopActions` and calls `Shortcuts()` after dialogs, settings, the search
palette and the back shortcut, so editors, IME composition and those overlays keep
their keys. The native menu uses `MenuRole.DesktopAction` placeholders bound to the
same actions by ID, so menu items, shortcuts and their enabled, checked and
permission state cannot disagree; open dialogs, settings or confirmations disable
them. Placeholders install no key equivalents; standard roles (Quit, Hide, Close,
the edit commands) keep theirs and forward edit commands into Kumiko's editor.
Settings has no shortcut because Kumiko has no comma key yet. On Windows and Linux
the app menu is dropped; Settings and Exit move into File (compiled, not run here).

**Notifications.** [`Update.Notices.cs`](Update.Notices.cs) compares each update's
model before and after: a running plan that becomes Done or Failed, or a save
that fails, posts one notice, but only while CRMLite is in the background, the
Notifications setting "Desktop notifications" is on and the shell reported
`Ready`. In front, the plan toast and the sidebar status say the same thing.
Notice IDs are `plan:{dealId}` and `save`, so a newer notice replaces an older
one; a second save failure while one is posted posts nothing. Notices are
withdrawn when their plan is opened in the app, deleted, retried, or the save
succeeds, and all of them are cleared on sign-out or when the setting is turned
off, so customer text does not outlive its session.

A click arrives through the `desktop` subscription as `CrmMsg.Desktop`. Only a
notice still listed in `Desktop.Posted` routes, once: it opens the deal's company
(`Open`, with its navigation guards) and its plan (`OpenPlan`), minimizing another
open plan and closing settings, then asks the shell to bring CRMLite forward. An
open New deal/New company draft or leave confirmation keeps the screen. Clicks on
withdrawn, cleared, duplicate or earlier-session notices do nothing.
`NativeCrmDesktop.BringForward` never activates a hidden app.

macOS posts only from a packaged `.app` with a `CFBundleIdentifier`;
`dotnet run` reports *Unavailable*, and a
declined permission reports *Denied* and stops further attempts. Settings explains
both under the switch. The desktop model state is omitted from JSON until the
shell first reports, so pre-integration recordings still replay.

[`DesktopFlows.cs`](../../tests/Kumiko.CRMLite.E2E/DesktopFlows.cs) and
[`DesktopMenuFlows.cs`](../../tests/Kumiko.CRMLite.E2E/DesktopMenuFlows.cs) cover
background/foreground posting, duplicate and stale clicks, preference and
permission states, drafts, save failures, replay, shortcuts under dialogs, the
palette, settings and IME composition, and the native menu in a hidden app
(AppKit activation, absent key equivalents, disposal). A recording `ICrmDesktop`
stands in for the notification center. Real banners, permission prompts,
notification clicks from Notification Center and visible menu tracking have not
been exercised; those need the packaged app on an interactive macOS session.

## Responsive layout

`Context.WidthBreakpoint` selects a navigation rail and company list below the
Expanded band; larger hosts retain the full table. Search and filters reflow and
remain available. Toolbar compaction follows the available main-pane width,
including a resized sidebar. While a rail is forced, sidebar controls are disabled
and the saved expanded/collapsed preference is preserved.
Company details move below the activity feed at narrow widths; the main canvas
is capped at 1440 points and centered on ultrawide windows. Layout decisions use
the shared responsive API and named app sizing tokens.

Run the responsive E2E matrix for repeatable offscreen
PNG/tree/JSON evidence at 480, 800, 1280 and 1800 logical points.
