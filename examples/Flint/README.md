# Flint for Kumiko v2

A C# 14 / .NET 10 port of the native Git client in
`/Users/backo/projects/personal/flint`. The original light, Fork-inspired
interface is rendered directly by Kumiko: no React, GPUIX, webview or native shim.
Git and optional GitHub authentication use installed `git` and `gh` executables.
They and Git hooks are found on the login shell's PATH, so a Flint opened from
Finder or the Dock still finds Homebrew and user-installed tools.
Icons come from the bundled Hugeicons catalogue, chosen to match the original's set.
Remaining differences are listed in [the handoff](HANDOFF.md); timings come from the
[repeatable performance harness](../../benchmarks/Kumiko.Flint.Benchmarks/README.md).

```
dotnet run --project examples/Flint -c Release -- --repo=/path/to/repository
```

## What matches the original

- Layout from `App.tsx`/`Toolbar.tsx`: resizable sidebar (290 px initially) with titlebar space, 54 px
  toolbar of icon-over-label buttons with ahead/behind badges, the 340 px head pill
  (repository, branch, ahead/behind, pinned gh account), a segmented tab bar with
  dirty dots, and bottom-right toasts instead of a status bar.
- History (`CommitList.tsx`, `GraphCell.tsx`): 22 px rows, 12 px lanes, rounded
  merge elbows, ten lane colours, up to 14 lanes, local identicons, ref badges by
  kind (local/HEAD, remote, tag, current), bold conventional-commit prefixes,
  170/74/158 px author/SHA/date columns and "Today at" dates. Further history loads
  automatically near the end. Each repository keeps its history scroll position.
- Details: author identicon, separate committer line, refs, SHA, parent links,
  message, file list; Changes tab with a resizable file list (300 px initially)
  and the diff. Drag the divider or focus it and use Left/Right, Home/End.
- Local Changes (`ChangesView.tsx`): resizable Unstaged/Staged trees (420 px initially), the working-diff
  header with Stage/Unstage File, Find bar, word-level diff emphasis and the
  composer with Amend, Commit & Push and Commit N files.
  Drag the file/diff divider to change the tree-column width, or the divider
  between Unstaged and Staged to change their heights. Focus either divider to
  resize with the arrow keys or Home/End. Each repository keeps its split sizes.
- Sidebar: branch folders (`feat/x/y`), remotes per remote, tags, stashes, worktrees,
  filter, keyboard navigation, double-click activation and context menus. Drag its
  divider or focus it and use Left/Right, Home/End. Long worktree names and branch
  labels use ellipses, and navigation selection fills the available row.
  The toolbar sidebar toggle uses CRMLite's `Motion.Gentle` easing, moving the
  main pane with it. Rapid toggles reverse smoothly; Reduce Motion is immediate.
- Quick Launch: open tabs, other workspaces, recent and discovered repositories,
  commands, branches and workspaces; arrow-key highlight; workspace GitHub, Rename and
  Delete row actions; a gh account picker; prompts for branches and workspaces.

## Behavior

Use **Focus current branch** beside the sidebar filter to highlight and reveal
the checked-out branch. It clears the filter and expands that branch's folders.
The button is disabled for detached HEAD or a branch with no commits yet.
Use **Collapse all** beside it to clear the filter and close every reference
section and nested folder. Reopening a section leaves its folders collapsed.

Use **+** beside Remotes to add a remote. Right-click a remote folder (including
`origin`) to fetch it, edit its URL, rename it or remove it with confirmation.
Editing the URL preserves an explicit push URL. Right-click a local branch to
set its upstream from fetched remote branches or remove its upstream. Tracking
changes keep the current checkout and working files intact.

Push uses the branch's configured upstream; only a first push sets `origin`.
Checking out a remote branch reuses an existing local branch. Commit & Push pushes
only after the commit lands. A failing hook opens a dialog with the complete output,
Copy Output, and an explicit "Commit/Push Without Hooks" retry for that request only.
Reverts copy files into Flint's trash first. Deleting a worktree renames it aside,
prunes Git's metadata and deletes the files in a detached low-priority process.
Open/Reveal/editor/terminal actions go through the injected `ILauncher`.

In Local Changes, Command-click (Control elsewhere) toggles files, Shift-click and
Shift+arrows extend from a fixed anchor, Left/Right collapse and expand folders,
Home/End jump, Space/Enter stage or unstage, S stashes, Backspace/Delete revert,
Primary+A/C select all and copy paths. Control-click on macOS opens the menu.

## Elm architecture and injected services

`FlintApp` implements `IKumiko<FlintModel, Msg>`. All durable UI state belongs to
immutable records and collections; `Update` returns a model and named commands.
The generated adapter creates a sealed render-local `FlintView` each frame. Views
cannot access services or perform I/O. Each repository has its own load generation,
so concurrent refreshes all land while obsolete results are rejected. Startup loads
the active repository, then its workspace, then other workspaces later. Session
replay restores recorded results without executing Git, storage or subscriptions.

`AddFlint` registers ordinary DI boundaries with typed `FlintOptions`: `IGitService`
(snapshots, status-only refresh, details, patches, serialized mutations),
`IProcessRunner`, `IWorkspaceStorage`, `IRepositoryDiscovery` (the original's scan
policy plus a known-repository cache), `IRepositoryWatcher` (working-tree edits run
`git status` only; ref/HEAD/index changes reload), `IRepositoryCache`,
`IGitHubAccounts` (lists `gh` logins without switching accounts) and `ILauncher`.

Commands request one interface each, and watcher subscriptions request
`IRepositoryWatcher`. `FlintStartup` uses constructor injection to restore
workspaces, known repositories and cached tabs from the same configuration.
The CLI owns a Generic Host while `Dev.Run` retains native/offscreen ownership.
Git snapshots/mutation gates and workspace persistence gates are singleton state;
DI command and subscription scopes do not create separate caches. The E2E and
benchmark hosts call `AddFlint` with temporary paths and safe replacements.
Shutdown drains `program.DisposeAsync()` on the entry thread before stopping the
host and deleting temporary state.

History, diff, commit-file, working-change and sidebar lists build only visible
rows. Git sorts files and refs into tree order once, so views flatten them in one
linear pass. Toasts dismiss through one-shot timers; idle programs block on the
native event loop.

Click a notification to read its full output in a scrollable dialog, or use
Copy Output to copy the complete text. Long toast previews show an ellipsis.
Open details stay available after the notification expires.

Normal sessions retain only the current runtime model, allowing obsolete patches
and snapshots to be collected. Offscreen diagnostics, `--kumiko-dev`,
`--kumiko-debug` and `--kumiko-record=...` retain event history for debugging and
replay; those modes can use more memory during long sessions.

Workspace configuration defaults to `~/.kumiko-flint/`, separate from the original
app. Override with `--state-dir=...` or `FLINT_HOME`. Discovery scans the home
folder plus `FLINT_ROOTS` (platform path separator), like the original. A pinned
`gh` login's credential is read into the child environment only, never into argv,
state, event history or the cache.

## Keyboard

Primary means Command on macOS and Control elsewhere.

| Shortcut | Action |
| --- | --- |
| Primary+P / K | Quick Launch |
| Primary+O / T | Open Repository |
| Control+Tab or Option/Alt+Tab (Shift reverses) | Recent tabs: a tap returns to the previous repository; hold the modifier and press Tab to step (a list appears), release to switch, Escape cancels |
| Primary+1…9, Primary+Shift+[ / ], Primary+Alt+Left/Right | Switch tabs in tab-bar order |
| Primary+W | Close repository tab |
| Primary+Shift+W | Workspaces |
| Primary+L / G | Local Changes / All Commits |
| Primary+B / Shift+B | Checkout branch / New Branch |
| Primary+Shift+F / L / P / S | Fetch / Pull / Push / Stash |
| Primary+Enter / Shift+Enter | Commit / Commit & Push |
| Primary+F | Find in diff (Enter / Shift+Enter step, Escape closes) |
| Primary+R | Refresh |
| Primary+\ or Primary+0 | Toggle sidebar |
| Up / Down / PageUp / PageDown / Home / End | Move the commit selection |
| Primary+G / R / D on a workspace row | GitHub account / Rename / Delete (press twice) |

## Repeatable offscreen checks

```
dotnet run --project tests/Kumiko.Flint.E2E -c Release -- artifacts/flint
dotnet run --project tests/Kumiko.Flint.E2E -c Release -- artifacts/flint-remotes --remotes-only
dotnet run --project examples/Flint -c Release -- \
  --repo=/path/to/disposable/repo --state-dir=/tmp/flint-state \
  --kumiko-snapshot=artifacts/flint-cli.png \
  --kumiko-tree=artifacts/flint-cli.txt --kumiko-dev
```

The E2E host injects temporary storage, real disposable Git repositories with local
bare "remotes", a recording launcher and fixed gh logins. It uses Tester input with
host modifier semantics and its private clipboard, and emits PNG, tree, warning,
session JSONL and report JSON artifacts. See its
[acceptance scenarios](../../tests/Kumiko.Flint.E2E/ACCEPTANCE.md). No network
operation, user repository, gh account or application is touched by verification.
