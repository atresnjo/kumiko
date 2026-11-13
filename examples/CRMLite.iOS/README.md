# CRMLite on iOS

The CRMLite (Harbor) design on a phone. It shares the desktop app's models,
messages, updates, queries, subscriptions and `Palette`; only the views are
phone-specific. Light, dark and accent settings come from the same palette, so a
screen reads as the desktop app at touch size. The small parts (logo, avatar,
stage tag, dot, rule, label, deal card, timeline) are not copies: both projects
compile CRMLite's `Parts*.cs` and pass their own `PartScale`. The phone view calls
`NativeExactText`, so those parts' `Context.Text` labels keep their exact size and
color here as on desktop.

| Desktop | Phone |
| --- | --- |
| Sidebar sections | System tab bar (Liquid Glass on iOS 26) with the accent on the selected section and an Inbox unread badge |
| Companies table, list tabs, search, stage/owner menus | List tabs, search field, filter chips opening sheets, touch rows with logo, owner, ARR and stage |
| Company page, details panel, section tabs, timeline | One scrolling page: identity, details card with hot switch, the same tabs, note composer and timeline |
| Deals board with metrics | Metrics grid, pipeline share, then each stage lane as a section of the same cards |
| Deal drawer | Native bottom sheet with stage choices and fields |
| People table, inbox | Contact rows; email threads with unread state |
| Leave confirmation dialog | A bottom panel over a scrim |

Company pages are pushed onto a model-controlled `UINavigationController` styled
with `NativeNavigationAppearance`; Back and the edge swipe are native and guarded,
so an unsaved note asks before it is discarded. Each visit has its own identity and
draft, including repeated visits to one company. Data is fictional and
memory-backed; the native host starts a seeded demo workspace with fake auth/API
services and never touches user documents or defaults. Settings, the agent plan,
and the New company/New deal forms remain desktop-only.

It uses Kumiko.iOS's app-styled native pieces: exact labels, pressable containers,
SVG icons, styled inputs and stack chrome.

Build with the pinned SDK on a Mac with its matching Xcode/iOS workload:

```sh
dotnet build examples/CRMLite.iOS \
  -c Debug -r iossimulator-arm64 -p:KumikoCompileMetal=false
```

Physical swipe
feel, device VoiceOver and full background/foreground lifecycle still need
device-level verification.

URL intake, iPad split/scenes and restoration across application restarts are
deferred. Runtime replay and explicit model restore are supported.
