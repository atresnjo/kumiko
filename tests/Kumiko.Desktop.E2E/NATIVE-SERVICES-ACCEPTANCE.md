# Native services: failure scenarios before implementation

## Credentials

- A refresh token is returned from a different application, environment or key;
  replacing/deleting one entry changes a sibling or another environment.
- Missing data is confused with a locked store, permission denial, unavailable
  daemon/library, unsupported OS, cancellation or unexpected native failure.
- Binary secrets containing NUL and non-UTF8 bytes are truncated or converted to
  strings. Empty secrets cannot round-trip. Native/managed copies outlive their
  owner, or diagnostic/JSON/session artifacts contain secret values.
- Cancellation before dispatch still mutates storage. Cancellation during a
  noninterruptible commit falsely claims the mutation did not occur.
- Disposal races queued work, frees native context early, leaks read results,
  permits new work, or reports a native exception with sensitive payloads.
- A worker accesses App/platform UI APIs; service callbacks escape unmanaged
  boundaries. A blocked daemon hangs the owner thread.
- Native fixtures fall back to the login/default credential store, alter keychain
  search lists, access existing credentials, prompt, or persist test secrets.
- Linux quietly uses a file backend or prompts to unlock/create a collection.
  Native libraries/daemon absence must be explicit with no plaintext fallback.

Behavior evidence must exercise store/read/replace/delete, sibling and namespace
isolation, disposal, cancellation, binary/empty values and status distinctions.
macOS may use a native temporary keychain only if creation leaves the user's
search list/defaults unchanged; otherwise use an isolated Security ABI fixture
and explicitly report that real Keychain storage was not executed. Never call
security(1), use user credentials, or include generated secret bytes in artifacts.
Windows/Linux adapter runtime requires the owning OS and isolated fixtures.

## Network and power/session

- OS connectivity is presented as proof of server reachability; unknown cost,
  battery or idle state is invented as an affirmative value.
- An initial snapshot and subsequent native notification are lost, reordered,
  duplicated after disposal, delivered on a worker, or retain their owner.
- Native registrations/blocks/handles survive disposal or partial startup.
- Keep-awake leases overwrite one another; releasing one drops another lease;
  disposal fails to release every assertion/inhibitor. Unsupported display
  inhibition or idle-time capabilities are silently accepted.
- Notifications are implemented with polling, wake the idle event loop, or
  actually suspend/lock/wake the desktop during tests.

Only read-only native snapshots and private injected notifications/assertions are
allowed on this host. Never change network state, lock/suspend the machine,
activate/show a window or synthesize input. Record unexecuted desktop transitions
and distinguish native macOS execution, native ABI fixtures and RID compilation.

## Review of 27ed46e: Linux power and credential races (before fixes)

- A System keep-awake request sends login1 sleep/block, blocking explicit
  suspend/hibernate despite the documented idle-only contract. Verify the actual
  private D-Bus request uses idle/block; never invoke real sleep, shutdown or
  hardware-key operations. logind idle logic need not control desktop-specific
  automatic sleep, so report this limitation and never substitute sleep/block.
- Constructing PowerSession waits for bus connection, UPower's three roundtrips
  or login1 discovery on the App thread. A delayed private daemon must leave an
  App.Dispatch heartbeat runnable before replying, with initial unknown state.
- A PropertiesChanged callback performs synchronous UPower calls on the App
  loop. Hold a real fixture reply and verify independent UI work still executes.
- Acquiring an inhibitor blocks the App thread, or returns an apparent lease
  before the daemon grants it. An async acquisition must complete only after a
  valid descriptor arrives, propagate denial/cancellation, and keep UI running.
- Disposal during connection/discovery/read/acquisition frees native resources
  while workers use them, waits on a slow daemon, delivers stale events, leaks
  a late inhibitor descriptor, or leaves outstanding Tasks incomplete.
- Bursts of power signals create unbounded concurrent refreshes. Coalesce
  pending invalidations without polling and deliver final status on App.Dispatch.
- An item is unlocked in SearchItems but locks before secret retrieval. A null
  libsecret result must preserve Locked, with no secret or authorization prompt.
- The item disappears after SearchItems. Resolve Missing rather than Locked or
  Failed, and do not retrieve or expose some other item. Retry after unlocking
  must succeed without logging secret bytes.

Use a private dbus-daemon and exported fixture objects only, with temporary
pipes/descriptors and explicit delayed replies. On macOS, host GIO exercises the
D-Bus wire/FD/cancellation boundary; this is not native Linux desktop verification.
Credential tests may replace only the unavailable libsecret session/crypto ABI
while running real GDBus SearchItems, GetSecrets and state-resolution calls;
explicitly report that libsecret itself is not executed on this host.

Power review fixture additionally holds a refresh after initialization, disposes
while it is pending, and verifies cancellation/cleanup before releasing the reply.
A cancelled lease queued behind a delayed refresh must complete before that
refresh's reply and must never reach the fixture's Inhibit method afterward.
Async power teardown must also release native signal callback contexts after the
App/GTK default context stops being pumped. A private subscription context must
drain destroy notifications before its cleanup Task completes, without polling.
