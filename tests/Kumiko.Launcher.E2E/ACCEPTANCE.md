# Launcher failure scenarios (recorded before implementation)

Behavior flows must use the real example view/update/host with deterministic
catalog, launch, startup and shortcut adapters. Never register desktop effects.

- Empty/loading/failed catalogs remain usable and allow an explicit retry.
- Duplicate identities and duplicate canonical paths produce one result; equal
  names with different identities remain selectable. Filtering ignores case and
  accents; changing the query resets selection; Up/Down stays in bounds.
- Revisiting a previously selected row after scrolling must reveal it again;
  keyboard navigation cannot leave the selected row partially clipped.
- Enter launches exactly the selected identity after hiding. Launch failure shows
  a recoverable error; Escape and blur hide without disposing or quitting.
- Repeated summons reuse the same panel. Close requests dismiss. Placement fits
  the requested work area, including negative origins and small displays.
- IME composition owns Enter/Escape/arrows. Search editing and settings controls
  retain keyboard precedence; no launcher action steals modified keys.
- Delayed or superseded catalog results cannot overwrite newer data. Completion
  after host disposal is ignored, including already queued main-thread replies.
- Startup reads never write. Enable/disable happens only on explicit actions;
  failure preserves the observed setting. Foreign registrations are not removed.
- An unsupported global-shortcut service must leave the launcher and Quit usable.
- Shortcut replacement reports a conflict and retains the previous registration.
  A late callback after disposal cannot summon a window.
- A second instance routes to the existing host; login launches remain hidden.
- Native boundaries use hidden windows and temporary app-bundle/desktop-entry
  fixtures only. Native panel class/style/key eligibility and teardown execute
  without ordering it front. No real installed-app launch or login registration.

Evidence: JSON check descriptions/outcomes, light/dark PNGs, semantic/layout trees,
error/empty/settings scenes and hidden native capture. Rerunning the same command
must reproduce the behavior assertions. PNGs are version-local evidence.

Interactive acceptance remains necessary for real hotkeys, focus/menu ownership,
Spaces/full-screen placement, desktop activation, actual icons/catalogs, startup
approval/login detection and other operating systems. RID compilation is not
native execution.

## Review failure scenarios (before fixes)

- Explicit host Quit and App.Quit must reach WillQuit and release a hidden real
  panel; the fixture's QuitRequested flag cannot establish host termination.
- Ordinary close must dismiss/reuse the same native panel. An unrelated window
  veto, or an additional panel Closing handler, must still cancel application
  quit. A later retry must succeed, in either window order. Reentrant ordinary
  close during BeforeQuit must still dismiss rather than destroy the panel.
- Worker IsVisible access must fail before touching a live presentation; a
  disposed panel must retain its false result without accessing native state.
- Finishing AppKit initialization and draining dispatch must not summon while
  launch provenance is unknown. A delayed private open-application login event
  must keep the controller hidden; an ordinary event must summon once. Duplicate
  events, cancellation and completion after controller disposal cannot resummon.
  Events before controller subscription must be remembered per App lifetime.
- Windows placement must convert the configured logical size with the target
  display scale before fitting. Repeated 1x/2x/1.5x movement must not compound
  scale or use the previous display DPI; negative origins and small work areas
  remain valid. Hidden geometry and scaled rendering are fixture evidence on
  macOS, not Windows native execution.
