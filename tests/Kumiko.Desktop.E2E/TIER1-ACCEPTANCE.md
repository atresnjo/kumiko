# Tier 1 desktop shell acceptance

Recorded before implementation. Native checks create hidden windows only. Shell
services use isolated or injectable backends; never register real shortcuts,
publish trays/notifications, activate applications, change defaults, or use the
general clipboard in development. Retain tier1.json and offscreen PNG/tree
artifacts. Report executed, fixture-only, compiled and unavailable separately.

## 1. Window chrome and state

- Invalid/nonfinite sizes, contradictory limits, opacity outside [0,1] or a
  disposed/foreign parent reaches native code or partially creates a window.
- Runtime limits apply to the frame instead of content; frame/content geometry
  disagrees after resizing, centering, moving or changing DPI.
- Maximize/full-screen transitions lose normal bounds or persisted full-screen
  state; repeated transitions emit duplicates, or restore the wrong state.
- Focus/show/hide/resize/move/state events precede native state changes, fire
  after disposal, or miss native changes. ReadyToShow fires before first paint.
- Canceling Closing still disposes the context or removes the window; an
  exception escapes a native close delegate; model-driven close dispatch doubles.
- Transparent/material, shadow, taskbar/workspace, protection, mouse passthrough,
  attention or progress requests silently claim unsupported behavior.
- A modal child leaves its parent interactive or disabled after child disposal;
  a parent disposes before its children or callbacks retain either indefinitely.

## 2. App lifecycle and single instance

- BeforeQuit cancellation closes windows; closing cancellation is bypassed;
  repeated/reentrant quit dispatches duplicate lifecycle events.
- WillQuit/Quit order differs across signal, menu, Cmd+Q and explicit quit;
  Exit runs cancelable events or loses the requested exit code.
- A second process wins the lock, loses argv/cwd, delivers off-thread, hangs
  indefinitely, or leaves a stale lock after either process crashes.
- A malformed/oversized IPC message causes unbounded allocations; disposal races
  dispatch, or an idle listener polls the native loop.

## 3. Open files and URLs

- Launch delivery is lost before subscription, replayed twice, or reorders files
  and URLs; command options and executable names are misreported as documents.
- Relative paths resolve against the receiver instead of the sender's cwd;
  forwarded arguments bypass the launch parser.
- macOS delegate and GetURL callbacks lose launch events, retain stale delegates,
  or allow exceptions through Objective-C.
- Scheme registration overwrites another app, incorrectly quotes executable
  paths, accepts invalid schemes, or unregisters someone else's association.

## 4. Notifications

- Unbundled macOS invokes UNUserNotificationCenter and crashes rather than
  reporting the bundle requirement; denied authorization claims success.
- Replacing stable IDs duplicates notifications; close/clear targets the wrong
  group; a click after replacement/disposal invokes a stale callback.
- Native callbacks run user code off-thread or escape unmanaged boundaries;
  a fixture accidentally displays a real notification.

## 5. Tray

- Multiple stable IDs alias native handles; invalid PNG/2x data leaks an icon;
  template rendering, tooltip, menu updates or anchoring bounds become stale.
- Click/right click executes twice, targets a disposed menu, or ignores guarded
  desktop actions. Appearance callbacks survive disposal.
- Missing Linux SNI host and appindicator are silently treated as success.

## 6. Menus

- Native per-window menus invoke another window or revive stale callbacks;
  rebuild/disposal leaks HMENU/GtkMenu/Objective-C targets.
- Runtime insertion and setters leave labels, accelerators, visibility or check
  state stale. Adjacent radio groups interfere across separators/submenus.
- Mac-only roles appear elsewhere; quit bypasses lifecycle; native accelerators
  steal editor/IME/modal input or dispatch the same action twice.

## 7. Clipboard and drag-out

- Format discovery invokes lazy providers; a provider runs more than once per
  snapshot; clipboard and drag caches alias, or mutable byte arrays escape.
- Foreign representations exceed 64 MiB, change owner during reads, recurse
  into clipboard operations, or lose empty/alternative/ordered items.
- Remote file URLs become local files; file lists read/delete files; HTML,
  PNG, URI/custom formats lose native interoperability.
- Replacing/clearing/disposal releases providers off-thread or more than once;
  Flush claims Linux persistence without a clipboard manager.
- Cancel/end of native drag leaks delayed renderers, retains a view, or also
  clicks/drops the internal drag; tests access the user's clipboard.
- Text coalescing strips authored trailing newlines instead of preserving them.

## 8. File watching

- Create/change/delete/rename events vanish or lose rename source; recursion
  follows unintended paths, ignore filters leak entries, or file watches include
  siblings. Bursts exceed the entry limit without an overflow signal.
- Debouncing loses the final batch, callbacks run off-thread/after disposal,
  hints are mistaken for an exhaustive journal, or idle operation polls.

## 9. Global shortcuts

- Accelerator aliases collide; invalid chords reach native registration;
  conflicts claim success or unregister another registration.
- Callbacks run off-thread, after unregister/dispose, or escape native code.
- X11 grabs ignore lock modifiers; Wayland bypasses its portal or silently claims
  registration without user/desktop approval.
- A portal denial, timeout, disconnect or nested quit leaves its request/session
  alive, or a late activation revives a disposed registration.

## Evidence boundary

Fixtures establish routing, ownership and error handling, not desktop daemon or
compositor behavior. Windows/Linux managed compilation on macOS does not establish
native runtime correctness. Every unimplemented native path remains incomplete,
even when it throws an explicit exception.

## macOS completion checks

- Clearing notifications while authorization is pending allows an obsolete request
  to display later; all pending identities must be invalidated before removal.
- Clearing one window's Dock progress removes another window's active progress.
- A tray retains a disposed menu and later dereferences its released native handle.
- AppMenu and OpenRecent roles render inert labels. Standard menu roles must
  expand, recent selections must deliver OpenFile, and hidden fixtures must keep
  recent document history private rather than accessing NSDocumentController.
- Two macOS notification owners overwrite the shared center's weak delegate and
  silently lose click delivery; reject a second native owner explicitly.
- Native notification callbacks lose their copied Objective-C blocks, replacement
  content, subtitle/group, or click identity. A private Objective-C center fixture
  must drive authorization/add/close/clear and late authorization, never the real
  center or permission UI.
- Native tray updates drop PNG representations, template status, tooltip/title,
  or retain callbacks after disposal. Exercise the adapter against an owned
  offscreen NSButton fixture, never NSStatusBar.
- Carbon uses incorrect physical key/modifier codes, loses its callback context,
  or treats native key conflicts as success. Drive the production adapter through
  an injectable Carbon ABI fixture without installing a system-wide hotkey.
- Content protection reports success on current macOS even though Apple documents
  NSWindow.SharingType.none as unused. Reject the request explicitly.
- A native file URL contains URI-list CRLF framing, so other apps cannot interpret
  a drag or paste. Verify the actual named pasteboard's public.file-url bytes.
- Notification Center completes adding a request after Close removed it. A late
  add completion must remove the obsolete request again and cancel its Show task.
- Standard Window and Help roles create ordinary submenus without registering
  them with NSApplication; native window-list/help behavior must own those menu
  pointers and release them when replaced, without displaying a menu.
- Foreground notifications are silently suppressed because the native delegate
  never completes willPresentNotification with banner/list presentation options.
- A delivered notification outlives its managed owner: clicking it after launch
  loses its stable ID because only in-memory IDs are recognized. A new owner must
  decode its own native identifier and raise Clicked; Close/Clear must still
  suppress stale queued responses and clear prior delivered requests.
- WKWebView windows bypass shared shell configuration, show despite App.Hidden,
  or never publish ReadyToShow. Verify a local-asset page in a hidden app with
  shell limits/opacity and readiness, without mapping any native window.
- Hidden WKWebView fixtures use the persistent website data store instead of an
  isolated in-memory store, potentially reading/writing cookies and caches.
