# Flint parity and performance handoff (second iteration)

The first port (7ea738f) established services, Elm state and a measured baseline.
This iteration rebuilt the views after the original's source geometry, restored
missing behavior and optimized the measured hot paths. Original source:
`/Users/backo/projects/personal/flint` (React/GPUIX; its `.git` has no HEAD).

Constraints remain: immutable Elm state, pure updates, typed effects, injected
services, render-local v2 views, an event-driven idle loop, files at most 600
lines, offscreen verification only, and no push/PR/release authorization.

## Done in this iteration

Visual composition (from `App.tsx`, `Toolbar.tsx`, `kit.tsx`, `theme.ts`,
`GraphCell.tsx`, `CommitList.tsx`, `RefBadge.tsx`, `CommitDetail.tsx`,
`FileRow.tsx`, `ChangesView.tsx`, `SearchableDiff.tsx`, `Sidebar.tsx`,
`Palette.tsx`, `Overlays.tsx`):

- 290 px sidebar with 40 px titlebar space (hidden native titlebar), repository
  name, Local Changes/All Commits, filter, virtual ref tree with branch folders,
  remote folders, tags (collapsed by default), stashes, worktrees and footer hint.
- 54 px toolbar: icon-over-label buttons, behind/ahead badges, 340 px head pill
  with busy label, branch, track counts and pinned gh account; workspace button.
- Segmented tab bar with equal-width tabs, dirty dots and close buttons.
- 22 px history rows, 12 px lanes, quarter-ring merge elbows, ten lane colours,
  14-lane cap, identicons (original MD5 algorithm, computed in the Git service),
  ref badges by kind, bold conventional prefixes, original column widths and
  Fork-style dates relative to the snapshot's load time.
- Detail pane with Commit/Changes tabs, AUTHOR block, committer line, REFS/SHA/
  PARENTS labels, message and file rows with status squares.
- Local Changes with 420 px Unstaged/Staged trees, working-diff header, Find bar
  and the original composer (Amend, hint, Commit & Push, Commit N files).
- Context menus (248 px, icons, shortcuts, separators, destructive rows), confirm
  dialog with scrolling monospace output and Copy Output, bottom-right toasts and
  the 680 px palette with group headers and inline workspace row actions.

Behavior: upstream-aware push; remote checkout reusing local branches; Commit &
Push chained after a landed commit; hook dialog with one-request no-verify retry;
gh account listing/picker; launcher actions (Open, Reveal, Zed, Terminal, gh login);
real Command/Control click semantics with fixed Shift anchors; file-tree keyboard
model; sidebar double-click/Enter/Delete; branch-from-here; history PageUp/Down/
Home/End; automatic paging; Quick Launch groups/highlight/actions; deleting
non-empty workspaces; per-repository history scroll memory; committer metadata;
token-LCS word diffs; notice rows; Primary+\\, Primary+0, Primary+Shift+[ ] and
Primary+1…9 (9 = last tab). Removed port-invented confirmations for push and amend
and the toolbar Refresh/Detach buttons, which the original does not have.

Performance: status-only watcher refresh; per-repository generations; per-repo
single-flight operations; startup priority (active, its workspace, then others);
virtual file and ref lists over service-presorted data; fast worktree removal;
original discovery policy with a known-repository cache; binary-searched matches.

Framework: Key.D/N/D0/Backslash/BracketLeft/BracketRight across hosts; dev
diagnostics now exempt disabled text from contrast like AuditAccessibility.

## Measured (same harness, machine and cases; NativeAOT medians)

| Case | Before | After |
| --- | --- | --- |
| changes-5000 frame | 16.50 ms, 5.13 MB, 20,180 nodes | 0.29 ms, 0.45 MB, 241 nodes |
| diff-search-10000 edit | 2.59 ms, 1.00 MB | 1.94 ms, 1.10 MB |
| history-10000 frame | 0.16 ms, 341 nodes | 0.30 ms, 610 nodes |
| cached-tab-switch-10000 | 1.77 ms, 0.28 MB | 2.85 ms, 0.51 MB |
| git-history-4000 cold / revalidate | 77.3 / 25.6 ms | 75–92 / 26–35 ms (three runs; Load adds `git remote`) |

History and tab switching cost more because rows now carry the original's graph
pieces, identicons and badges, and switching revalidates the repository like the
original. Release JIT runs are noisier (tiering): changes-5000 16.2 → 0.73 ms,
history 0.80 → 1.33 ms, diff 0.23 → 1.01 ms. Raw samples: `artifacts/flint-before` and `artifacts/flint-after`.
Scope limits from the benchmark README still apply (CPU frame preparation only,
calling-thread allocations, warm OS cache, single machine, no GPUIX timing).

## Remaining gaps (candid)

- No pixel comparison against the running original: showing a GPUIX window is not
  allowed here. Geometry follows the source; fonts are Kumiko's system/mono faces.
- Icons are Hugeicons stand-ins (requested), not the original Lucide-style SVGs.
- Text colours below WCAG contrast in the original (textFaint, white on bright
  status colours, #dce6ff on selection) use darker accessible tones.
- The graph column width uses the widest row of the loaded history, not of the
  mounted window. Identicons cost about eight nodes each.
- The diff uses uniform 18 px rows (the native diff has 28/24 px headers) and clips
  long lines; there is no horizontal scrolling or long-line navigation yet. Rows
  lay out at most 2,000 characters, so find matches beyond that are not shown.
- Palette highlight does not follow the pointer (hover is a separate tint) and is
  not scrolled into view beyond the visible results. "Browse for Repository
  Folder…" is absent: Kumiko has no native folder picker.
- The original's login-shell environment import for hooks (shellEnv.ts) is not
  ported; launching from Finder may lack PATH entries hooks need.
- Commit-file menus disable Open only for deleted files; the original also checks
  whether the working-tree file still exists.
- Focus does not return to the file row after a dialog closes; menu arrow keys do
  not wrap. Sidebar section rows are 22 px (original 24 + 2 px margin).
- Scroll memory covers the history list only (not file lists or the sidebar).
- Mount boundaries come from `DriveInfo` mount roots, not device numbers.
- Remote operations are verified against local bare repositories only; the gh
  token path and network remotes were not exercised. With `push.default=simple`, a
  differently named upstream branch fails exactly as in the original.
- Linux and Windows build; desktop behavior was verified on macOS only.
