# Document lifecycle failure scenarios (before implementation)

- A held save completes after another edit and clears the new dirty state.
- An old or duplicate completion changes a new document, path, or save attempt.
- Two saves started from the same disk version both overwrite the file.
- An external edit, deletion, or occupied Save As destination is overwritten.
- Cancellation before commit leaves a partial destination or temporary file.
- Revert races with typing, or a clean external reload discards dirty content.
- A recovery snapshot is lost on a failed save or decoded using implicit reflection.
- Closing or quitting drops a dirty, conflicted, or saving document without a decision.
- Cancelling one close leaks a partially accepted quit into a later request.
- Duplicate shares mutable state or reuses the original path/identity.
- Rename/move changes the model before the filesystem operation succeeds.
- A document menu targets another window; recents write system preferences in tests.

All files live in the explicit temporary test directory. No native windows,
user preferences, clipboard or documents are accessed. Managed filesystem evidence
is distinct from native window behavior. JSON reports include exact assertions.

Integration failures found before corrections: reusable effect factories must
compile in strict updates, their result/error callbacks run only in the worker,
and the generated app must validate the injected document store. Per-document
window identities must have their own generated JSON contract.

Before storage hardening: replacement must retain existing Unix permissions,
follow an explicit leaf symlink to its target, and always release the writer gate
even when temporary-file cleanup fails. Model save paths must be absolute so
Update never depends on process working directory. macOS/Windows replacement
must use the system replacement primitive rather than discard file metadata.

Before replay correction: a restored clean rich document must remain clean;
separately serialized save replies must match their model-owned ticket without
reference equality, retain edits made in flight, and use the model's captured
value. A stale sequence or changed target must never complete a newer save.

## Independent review failure scenarios (round 1)

P7: hold a codec inside an old recovery write, start disposal, and attempt a new
owner. Ownership must remain exclusive until old work cannot commit; disposal
must cancel old work and support nonblocking asynchronous joining. Remove during
a held write must prevent resurrection, including a queued write. Concurrent
reads/remove/dispose must keep the lease until all operations quiesce. All data
is in a private filesystem directory; preserve independent review evidence.
