# Desktop formats failure scenarios (before implementation)

- Native raster decoders leak streams/loader/pixel locks on malformed data.
- Raster dimensions/strides overflow or a decompression bomb allocates unbounded output.
- BGRA/RGBA conversion or negative stride flips channels/rows or loses alpha.
- Prereleases sort lexically (beta.10 before beta.2), a stable app selects a prerelease,
  or build metadata/unsupported RID makes a release look newer.
- A custom update feed accepts an untrusted scheme, tampered metadata/package,
  wrong target runtime, oversized download, cancellation or partial staging file.
- A browser stream delivers replies after cancellation/navigation/disposal, hides
  producer exceptions, or grows an unbounded queue under backpressure.

These fixtures decode explicit bytes and use local feed/transport fixtures.
They never install an update, open a browser, or change live traffic.

Hidden embedded-browser acceptance: native child bounds must follow the pure
layout slot, local assets must execute in an ephemeral data store, an overlapping
Kumiko overlay or removed slot must hide the child, and disposal must detach it.
The PNG contains the framework placeholder only; native browser geometry is
queried directly and recorded in JSON. Windows/Linux remain unexecuted here.
Failed construction with an untrusted file URL must reject before adding a native
child. Worker disposal must reject before any owner-thread native call (this
negative thread case is executed only with the guard in place).

## Independent review failure scenarios (round 1)

P5: actual link, location.assign and reload navigation must cancel old unary and
stream work before any new request. New documents may reuse ID 1 without collision.
Late old results and already queued replies/events must never resolve new IDs or
reach new listeners. Page.Load and disposal must retain the same guarantees;
exercise held and cancellation-ignoring producers in a real hidden WKWebView.
P6: synchronous Bind and the synchronous prefix of BindAsync must retain their
existing owner-thread contract. A hidden UI-bound service must SetTitle and
process typed messages successfully; async continuation results remain protected
by cancellation/document identity. No implicit worker dispatch is introduced.

## Independent review failure scenarios (round 2)

R2-P3: rejected Page.Load URLs leave the current ready DOM and bridge untouched.
Failed/cancelled provisional navigation retaining a DOM must give that DOM a
fresh usable bridge identity. Old unary/stream work stays cancelled; late old
replies/events and old generator cleanup cannot reach fresh reused IDs. Exercise
actual hidden WKWebView refused loopback navigation, native stop, and superseding
a pending navigation with a valid local load; use native title independently of
bridge readiness. All adapters must own failure callbacks and detach on disposal.

For GTK retained failures, the browser URI may still name the failed provisional
request. Validate restored DOM messages against the last committed origin only
after handling that provisional failure; new loads must restore normal origin
validation. Never trust a foreign provisional document as the old origin. This
GTK runtime case remains unexecuted on macOS.

## Independent review failure scenarios (round 3)

R3-P3: host fragment additions/changes/empty fragments on a ready current document
must keep the bridge usable and retain active unary/stream ownership. Include
script hash, pushState/replaceState, back/forward and host fragment after history
changes; confirm DOM location/sentinel with native title independently of IPC.
Cross-document host targets WITH fragments must still cancel old work, drop
queued/late old replies/events and safely reuse IDs. Preserve rejected/failed
navigation and disposal guards. All native tests remain hidden/private.

## Independent review failure scenarios (round 4)

R4-P1: supersede an accepted-but-held provisional load with a host fragment on the
surviving committed DOM. Cover changed/empty/identical fragments, history state
changes and both script/host provisional starts. Native title/sentinel must prove
DOM retention independently of bridge readiness. Old unary/stream work stays
cancelled; recovery uses a fresh generation with reused IDs. Release old unary,
old generator cleanup and the abandoned HTTP response after fresh calls start:
no stale replies/events, stream cancellation, document replacement or ready-state
regression. Keep rejected/failed/new-document and superseded-callback guards.
Use private loopback sockets, embedded assets and hidden windows only.

Also change the surviving DOM's history URI/hash WHILE the provisional load is
held, then request that URI or the previously identical fragment. Use the live
committed history entry, preserving the requested location and fresh generation;
a pre-navigation URL snapshot must not suppress or misclassify the host request.

## Program-free page windows and drag regions

P1: an application may open a whole-window web page with
`App.NewPage(WindowOptions, PageOptions)` and no Kumiko program. The analyzer must
accept it, while callback-only native windows stay forbidden (KUM010). The page
loads embedded assets and calls typed services exactly as program-hosted pages do.

P2: a primary-button mousedown inside an element styled `--app-region: drag`
must reach the owning window as one native move request. A nearer ancestor or the
target styled `--app-region: no-drag` must produce none. A move starts only while
the primary pointer button is physically held; a script-dispatched event in a
hidden fixture is refused without moving, showing or erroring the window.
