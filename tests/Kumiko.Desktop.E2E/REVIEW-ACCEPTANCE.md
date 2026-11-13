# Desktop shell review failure scenarios

Review baseline: 6097a9c..2001051. These scenarios are recorded before implementing
review fixes. Use hidden windows, private pasteboards, isolated files and injected
native endpoints. Never show UI, install shell services, use real clipboard or
change user defaults. Every exercised flow writes repeatable JSON/PNG artifacts.

## Astra 6 round one

- A Resized or Moved subscriber disposes its window synchronously. Resize,
  SetBounds and delegate continuations then message freed native objects or emit
  later shell events. A hidden subprocess must exit cleanly with one Closed event.
- A lazy pasteboard provider throws while disposal attempts persistence. Native
  providers/pasteboard, snapshot release and previous clipboard scope restoration
  must finish exactly once, on the App thread, and App teardown must complete.
- Drag writers have already been written to a private pasteboard and cannot be
  transferred to the dragging pasteboard. A private native session endpoint must
  accept fresh unbound writers, preserve ordered formats, laziness and cleanup.
- An interactive frameless NSWindow cannot become key. Query eligibility on a
  hidden native window and verify its input route without activating it.
- One Quit closes only the first model-owned clean window, then abandons pending
  shutdown. Exercise two model windows and a dirty-veto variant; retain lifecycle
  and model transcripts without bypassing the existing close-message precedence.
- Carbon owners reuse event identities or swallow unrelated events. Two owners
  and a chained native ABI fixture must route distinct chords exactly once and
  return eventNotHandledErr for unrelated IDs/signatures.
- Carbon nonexclusive registration reports success for an unusable shortcut.
  Inject the native registration boundary and verify exclusive flags, conflict
  results and cleanup without installing a real global shortcut.
- Foreign RTF/TIFF/native pasteboard types break default transfer reads because
  they are not MIME names. A named pasteboard with text/native/custom formats must
  support discovery, selective/default reads and reversible native mappings.
- Clear runs between native and public notification dispatch for a recovered
  prior-owner ID. No click may escape the clear generation, and native response
  completion/release must still happen.
- Appending one menu item recreates existing standard-role objects and discards
  retained item setters/submenu changes. Inspect native state after structural
  changes using the same logical items returned by Find.
- A tray-only app has no ordinary window to produce appearance changes. An
  offscreen status-item appearance source must deliver main-thread changes,
  support icon updates and stop all callbacks on disposal.
- Renames across ignore boundaries hide removal/addition of tracked paths.
  Rename an included file into and out of the ignored set; preserve endpoint
  semantics, main-thread batching and disposal suppression in a JSON transcript.

## Opus 5.5 round one

- A killed single-instance owner leaves a stale socket. A replacement isolated
  child must acquire the same identity within a bounded deadline.
- A native termination request always returns Cancel, blocking logout. Inject
  termination replies and verify Later followed by approval or explicit veto.
- Non-closable chrome must not silently prevent programmatic close or app quit.
- Material wrapping removes the content view as first responder. Hidden native
  windows must retain keyboard eligibility and content responder after wrapping.
- Dock progress removes the app icon. Render a private tile view offscreen and
  verify image plus progress without displaying the actual Dock tile.
- Production Carbon listens for release instead of press. Verify native event
  class/kind and exclusive flags at the injected ABI boundary.
- Blur calls a notification hook without changing focus. Verify a valid native
  focus transition through a private endpoint or explicitly report unsupported.
- Hidden child construction attaches to a parent and can order it on screen.
  Attachment must follow Show/Hide, with hidden construction remaining detached.
- Restore requests asynchronous full screen or zoom twice. Count native requests
  and require exactly one transition, including repeated pending requests.
- Traffic-light positioning is reset by AppKit layout. Verify hidden native
  button positions after resize and title changes.
- Cancelled drag disposal fetches lazy data and leaks its private pasteboard.
  Fresh unbound writers must release without forcing representations.
- AppKit and argv parsing may deliver the same launch operand twice. Exercise an
  isolated hidden launch and distinguish duplicate launch delivery from later opens.
- Standard menu roles discard caller submenus, and Primary accelerators can match
  unmodified letters. Preserve submenu identity and require platform modifiers.
- Menu mutation inside its own action may release the active sender. Exercise a
  native hidden menu action and defer destructive rebuild until dispatch returns.
- Sustained watcher traffic can starve a trailing debounce; ignored directory
  names can leak descendants. Verify bounded batch delivery and subtree filtering.
- Native GetURL registration and notification cold-start activation have launch
  timing uncertainty. Inspect ordering and document installed-bundle limits;
  do not claim unexecuted system delivery as runtime evidence.

## Astra lifecycle follow-up

- Queued reconciliation retries a WillQuit veto when the model is empty and
  QuitOnLastWindow is enabled. Drain queued work after one veto; only a later
  explicit Quit may start another attempt.
- A model close creates a replacement or confirmation window during deferred
  quit. A window outside the approved set must survive and cancel shutdown.
- A DidQuit subscriber throws after shutdown commits. Native termination must
  still receive exactly one affirmative reply, with the error reported.
- BeforeQuit or Closing calls Exit. The enclosing normal quit must stop emitting
  subsequent stages, preserve the requested code and stop the backend only once.
- Carbon ItemCount/ByteCount must match the SDK's native-width unsigned types;
  compiled ABI declarations must remain valid under NativeAOT.
- Named ownership can differ between CoreCLR and NativeAOT on Unix. Live owners
  must exclude a second process in both runtimes, and crash recovery must work
  without replacing an active server socket. Run the same process checks in AOT.
