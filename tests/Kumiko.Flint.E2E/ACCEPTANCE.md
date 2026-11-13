# Flint port acceptance

Notification and memory failure scenarios, recorded before implementation:

- A long worktree checkout error is clipped without an ellipsis, cannot be opened
  by pointer or keyboard, or loses the beginning/end of its output when copied.
- Opening notification details permits input to reach the underlying page, or
  dismissing the toast also closes the output being read. Long output hides Close.
- Repeatedly selecting large real Git diffs keeps obsolete patches alive after
  leaving those files, causing memory to grow with the number of selections.
- Disabling runtime history loses the current model, runs effects twice, breaks
  subscription replies, or prevents explicit diagnostic recording and replay.

Run `--notifications-only` and `--memory-only` for offscreen PNG/tree artifacts
and repeatable JSON reports. Memory checks use disposable real Git repositories,
private clipboard state and forced collections to distinguish retained data from
temporary allocation; they do not predict total native GPU/process footprint.

Remote management scenarios, recorded before implementation:

- An empty Remotes section has no add action. A configured remote with no fetched
  branches cannot be edited, renamed, fetched or removed from its folder menu.
- Changing origin's URL changes a different remote or its explicit push URL.
  Cancelling an edit or removal still changes Git configuration.
- Duplicate or option-like names and blank URLs mutate configuration or leave
  the app busy. Failed URL loading overwrites a newer dialog.
- Setting or clearing a local branch's upstream checks it out, changes files,
  or targets HEAD instead of the selected branch. Missing upstreams cannot be
  replaced, and renamed/removed remotes leave stale tracking in the UI.
- Fetching one remote fetches all remotes. Slash-containing remote names lose
  their identity in the tree. Dialog shortcuts escape to the background app.

`--remotes-only` runs the real-Git, offscreen remote flow twice reproducibly;
retain remote PNG/TXT/warnings and report.json in separate output directories.

Tool lookup failure scenarios, recorded before implementation:

- Flint opened from Finder or the Dock inherits launchd's PATH, so `gh` and hook
  tools installed by Homebrew or in user directories are missing: pushing with a
  pinned account fails with "No such file or directory".
- A tool is looked up on Flint's own PATH instead of the PATH its child receives.
- Login-shell banners corrupt the queried PATH, inherited entries are dropped, or
  a hanging or broken shell blocks commands instead of keeping the inherited PATH.
- A missing tool reports a raw operating-system error without naming the tool.

`--tool-path-only` runs these against fake tools and fake login shells; it never
starts the user's shell or reads their profile.

Failure scenarios recorded before implementation:

- Opening a folder inside a repository fails to resolve the root, reopening it
  duplicates the tab, or a missing/non-repository path hides the recovery action.
- NUL-delimited status/log/ref parsing corrupts spaces, Unicode, renames, root
  commits, unborn/detached HEAD, conflicts, annotated tags or merge parents.
- History, graph lanes and detail disagree; selecting a merge diffs against every
  parent instead of its first parent. Long history builds every row each frame.
- Staging/unstaging changes the wrong file, loses the rename source, or fails on
  unborn HEAD. Selecting files, directories or multiple files loses stable identity.
- Commit permits an empty subject or an empty index; amend loses the prior message;
  hook output is swallowed. Failed commands leave controls permanently busy.
- Search counts patch metadata rather than code, misses repeated/case-insensitive
  matches, cannot navigate offscreen, or keeps a stale match after changing files.
- Checkout, new branch, stash/pop, worktree open/delete or backed-up revert changes
  a different repository. Cancelling confirmation runs the operation anyway.
- Changing tabs/workspaces drops selections/drafts; persistence writes snapshots,
  tokens or partial JSON, or restart cannot restore workspace tabs and accounts.
- Late command completion overwrites a newer selection, closes another workspace's
  overlay, or resurrects a closed tab. Concurrent Git writes corrupt the index.
- Watchers poll, survive tab closure, watch symlink trees, miss linked-worktree
  metadata or fail to debounce bursts. Replay runs Git or accesses user storage.
- Quick Launch cannot find repos/branches/commands/workspaces; keyboard commands
  leak through a dialog or text editor. Native rendering clips controls at 1000x700.
- Git arguments invoke a shell or option injection; cancellation leaves Git alive;
  GitHub account selection changes gh's global account or persists credentials.

Parity and performance failure scenarios, recorded before the second iteration:

- Push always targets `origin` with `-u`, so a branch tracking another remote
  pushes to the wrong remote; a first push of a new branch fails to set upstream.
- Commit & Push pushes after a failed commit hook, or pushes the previous HEAD.
  Hook failures lose all but one line of output and offer no explicit retry
  without hooks; the retry silently skips hooks for later commits too.
- Checking out a remote branch whose local branch already exists fails, creates a
  duplicate or detaches HEAD instead of switching to the existing branch.
- Primary+click only recognizes the synthetic Primary bit, so real Command/Control
  clicks from the host replace the selection. Shift ranges move their anchor, so a
  second Shift+click extends from the wrong row.
- Branch names with slashes are flattened; double-click, Enter and context menus do
  not check out branches, pop stashes or open worktrees; filters show stale rows.
- Reveal/Open/editor/terminal actions start processes from the view or test host,
  touch the user's applications, or ignore deleted files.
- gh accounts must be typed by hand; listing changes gh's active account or puts
  tokens in the model, history, cache or workspace file.
- History stops at the first page until a button is pressed; End/PageDown/Home do
  not move the selection; switching tabs loses keyboard position.
- Diff word emphasis only marks a prefix/suffix span, misses replacement blocks,
  or highlights unrelated lines. Committer identity/time disappears from details.
- Quick Launch lacks groups, arrow-key highlight, repositories from other
  workspaces, or workspace rename/delete/account row actions; deleting a workspace
  with repositories is refused or deletes repository data.
- A working-tree edit reloads refs, stashes, worktrees and history. Two repositories
  refreshing together drop one result because of a single global generation.
- Startup loads every repository serially before the active one is fresh.
- Thousands of working changes or refs build every row each frame.
- Deleting a worktree blocks until every file is removed, or leaves Git metadata.

Binary size failure scenarios, recorded before restricting the icon set:

- Flint's NativeAOT executable ships the entire Hugeicons catalogue for ~30 icons.
- A Flint icon is missing from its `KumikoHugeicon` list, so a toolbar, sidebar,
  palette or file row draws nothing or throws only in the published build. Every
  `Icons` field must rasterize from the subset; an unlisted icon must throw.

Commit file-pane resizing failure scenarios, recorded before implementation:

- Dragging the file/diff divider does not widen the file list, jumps when grabbed,
  or changes the selected file or its patch. Selection and commit navigation reset
  the chosen width.
- Dragging beyond either edge hides the file list or leaves no usable diff.
  Shrinking the window after widening the list pushes the diff offscreen.
- The divider cannot be focused or resized with the keyboard, or loses its width
  after switching between Commit and Changes.

Sidebar layout failure scenarios, recorded before implementation:

- Navigation selection only covers its text, rather than the available row.
- The sidebar divider cannot be dragged or used with the keyboard; widening it
  leaves the filter, navigation or file/diff split at their old widths.
- List padding moves scrollbars away from the right edge of their pane.
- Long worktree names are cut off mid-letter or squeezed away by a long branch.
  Truncation splits a Unicode text element or changes the full accessible name.
- Widening the sidebar then shrinking the window pushes the main pane offscreen.

Framework text-overflow integration scenarios:

- The worktree label uses the framework policy but its branch badge still loses
  its original text, overlaps the name or paints beyond the sidebar.
- Narrowing to the minimum width fails to truncate both long labels; widening
  to the maximum width leaves either shortened despite enough available space.
- A narrow/wide/restored resize cycle changes either complete accessible text
  or leaves stale bounds. Retain all three Flint PNG/tree/warnings snapshots.

Local Changes resizing failure scenarios, recorded before implementation:

- The file/diff divider cannot resize the Unstaged/Staged column, or leaves the
  composer fields at their previous width. Extreme drags hide either pane.
- The Unstaged/Staged divider cannot move, or an empty section collapses and
  prevents staging/unstaging. Keyboard resize and section scrollbars stop working.
- Resizing or switching pages discards the selected file, diff, commit draft or
  chosen split. A smaller window hides section headers or composer actions.

Sidebar toggle animation failure scenarios, recorded before implementation:

- The toolbar toggle instantly removes/inserts the sidebar rather than easing
  the sidebar and main pane together, unlike CRMLite.
- Reversing a toggle mid-animation jumps to an endpoint or loses the saved width.
- Composer widths jump ahead of the moving main pane or retain the old bounds.
- The collapsed sidebar leaves invisible controls or a resize handle accessible.
  Animation keeps requesting frames after settling, ignores reduced motion, or
  changes the selected file, diff or commit draft.

The E2E host uses real disposable Git repositories and an injected temporary
state directory, plus Kumiko Tester input and its private clipboard. No remote
service, user repository, screen, window or application state is changed.
Run twice to reproduce PNG/TXT/warnings and report.json evidence:

```
dotnet run --project tests/Kumiko.Flint.E2E -c Release -- artifacts/flint
```

Window resize stability scenarios, recorded before implementation:

- At a fixed window size, successive frames alternate pane boundaries or move
  controls despite no model/input change. Resizing back reuses stale bounds.
- A live width/height resize uses the previous viewport for pane splits, composer
  fields or the diff, then visibly corrects itself in subsequent frames.
- Narrow/wide cycles retrigger sidebar motion or keep requesting redraws after
  resizing stops. Disabling motion merely conceals a layout feedback loop.
- A divider whose pixels are stored as a fraction sends resize messages on
  every render while the pointer stays still, due to float round-trip error.
- Resizing changes selection, patch, drafts or saved divider preferences. A
  vertical split ceases to fit when the header or composer changes height.

Retain per-frame geometry/animation/due-frame traces, PNG/tree/warnings at the
affected sizes and before/after performance samples. Offscreen geometry checks
do not claim to measure native compositor presentation or physical resize feel.

Native resize timing scenarios, recorded before implementation:

- A hidden AppKit window delivers a size change while the rendered Flint root
  still has its previous bounds. Several notifications arrive before dispatch
  drains, so each resize stretches an older frame.
- A synchronous resize frame leaves an older invalidation queued, causing a
  redundant build after resizing stops. Duplicate size notifications also draw.
- Metal content is presented independently of the layer geometry transaction.
  Test only owned hidden windows and offscreen textures; physical presentation
  remains outside the automated evidence.
The same `AddFlint` registrations supply isolated E2E storage/discovery and
replacement Git/account/launcher services. Verify cached startup restore and
that offscreen startup skips stored workspaces, discovery and watchers. Drain
program cleanup before deleting fixture repositories. Generic runtime scope,
cancellation and hidden-host teardown evidence comes from `Kumiko.V2.E2E`.

Long-line scenarios, recorded before implementation:

- A generated or minified file puts a megabyte on one diff line. The row shapes
  and lays out the whole line and paints every glyph, so one visible row costs
  over a gigabyte of paint and GPU buffers that the process then keeps.
- Capping the displayed text drops the line from the diff model, splits a
  surrogate pair, or keeps word/find spans past the displayed prefix.

`LongLines` opens a 1.2 MB single-line untracked file and checks the model keeps
the full line, the frame stays viewport-sized and no node holds the whole line.

Open-latency scenarios, recorded before implementation:

- Opening a repository Flint has not loaded runs `rev-parse` alone, then the
  status/ref/stash/worktree/remote commands, then `git log`. Each wait adds a
  full Git round trip before the tab appears (about 0.2 s of 0.57 s on a
  4,000-commit repository).
- Running them together changes the snapshot, breaks repositories without
  commits, reports a different error for a folder outside Git, or runs
  `git log` again when an unchanged repository reloads.

`OpenLatency` (`--open-latency-only`) delays every Git command and checks that a
first open starts all of them before any finishes, matches a sequential load,
opens an unborn repository and reuses history on reload.

Open-feedback scenarios, recorded before implementation:

- Pressing Enter in the open palette closes it and leaves the previous
  repository on screen until every Git command answers, so the open looks
  stalled for its whole duration.
- The selected commit's header, files and patch load as three Git commands in
  sequence, after an open and on every click. Its parents are already in the
  history, so they can run together; guessing HEAD instead misses in team
  repositories, where a remote branch usually holds the newest commit.
- Showing the tab early leaves it behind when the folder is not a repository,
  duplicates it when a subfolder resolves to the root, or loses the previously
  active repository.

`OpenFeedback` holds the load, checks the tab and "Loading…" appear at once,
that the opened repository then shows the newest commit's details and diff,
that a failed open removes its tab and restores the previous repository, and
that a subfolder open leaves only the root. `OpenLatency` checks that a root,
ordinary and merge commit start all three commands at once and match the
sequential result.

Collapse-all failure scenarios, recorded before implementation:

- Filtering forces the tree open, so collapse-all appears to do nothing.
- Only visible folders collapse; reopening a section or parent reveals expanded
  descendants that were outside the viewport or hidden by a collapsed ancestor.
- Collapsing resets the selected commit, file, page or checkout, or a pending
  branch reveal immediately reopens the tree. Repeated keyboard activation toggles it.
- The extra button overlaps the filter or current-branch button at minimum width.
- Current-branch focus cannot reveal its branch after collapse-all.

Current-branch focus failure scenarios, recorded before implementation:

- A filter, collapsed Branches section or nested branch folder hides the checked-out
  branch; focusing it must clear the filter, expand its ancestors and reveal its row.
- A long virtual list leaves the branch offscreen, repeated activation does nothing,
  or the reveal keeps snapping back after the user scrolls away.
- Focusing checks out another branch or discards the selected commit, file or page.
  Switching repositories focuses the previous repository's branch.
- Detached HEAD and unborn repositories offer an enabled button with no branch row.
- The button cannot be activated with the keyboard or clips the filter at minimum width.

Recent-tab switching failure scenarios, recorded before implementation:

- Control+Tab or Option+Tab moves keyboard focus between controls instead of
  switching repositories.
- Tabs cycle in tab-bar order rather than most recently used order, so a quick
  Control+Tab does not return to the previous repository.
- Holding the modifier switches (and reloads) every repository passed over;
  releasing it does not commit, or Escape does not cancel the choice.
- The switcher overlay flashes on a quick press, never appears while held, or
  offers a closed repository or one from another workspace.

`RecentTabs` taps Control+Tab and Option+Tab between three repositories, holds
the modifier through several presses and checks only the release switches,
checks Escape and Shift reverse the choice, and records the overlay PNG.

## Physical edge review failure scenarios

- In RTL, the documented bottom-right toast stack lands on the left rather than
  20 points from the physical right. The toolbar count badge must continue to
  overhang the icon's trailing side after physical inset semantics are corrected.
  Capture offscreen LTR/RTL PNG/tree/geometry JSON before assertions. Use only
  in-memory models without subscriptions, persistence, Git or platform services.
