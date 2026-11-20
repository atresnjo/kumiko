# Diff

A native macOS diff viewer for Git changes, commit ranges and workspaces, inspired by
[Legend Diff](https://legend.so/diff/). It is built with Kumiko views and the Elm
architecture: no webview, no native shim and no third-party highlighter. Every
scrolling frame stays far inside a 120 Hz refresh, even for a 100k-line diff.

```sh
dotnet run --project examples/DiffViewer -c Release                 # working tree vs HEAD, untracked included
dotnet run --project examples/DiffViewer -c Release -- --staged      # index vs HEAD
dotnet run --project examples/DiffViewer -c Release -- main          # working tree vs a revision
dotnet run --project examples/DiffViewer -c Release -- main..feature # two revisions (or: main feature)
dotnet run --project examples/DiffViewer -c Release -- --compare left/ right/   # two folders or two files
dotnet run --project examples/DiffViewer -c Release -- --repo=/path/to/repo --dark
dotnet run --project examples/DiffViewer -c Release -- --demo        # built-in sample changes
```

For the smoothest first scroll, publish it with NativeAOT (no JIT warm-up):

```sh
dotnet publish examples/DiffViewer -c Release -r osx-arm64 -p:PublishAot=true -o artifacts/aot/diff
artifacts/aot/diff/DiffViewer --repo=/path/to/repo
```

A first argument naming an existing folder is the repository (`-- ~/code/app main`).

## Remote checkouts over SSH

A checkout on another machine opens the same way, read with your `ssh` client: Git runs
on the host, so nothing is cloned or mounted locally.

```sh
dotnet run --project examples/DiffViewer -c Release -- devbox:~/code/app            # working tree on devbox
dotnet run --project examples/DiffViewer -c Release -- me@devbox:/srv/app main..feature
dotnet run --project examples/DiffViewer -c Release -- ssh://me@devbox:2222/srv/app --staged
dotnet run --project examples/DiffViewer -c Release -- --ssh=devbox --repo=code/app  # relative to the remote home
```

The host is anything `ssh` accepts, including aliases from `~/.ssh/config`. It must log
in without prompts (a key or an agent; the host already in `known_hosts`), and needs Git,
`sh` and `tar`. Each Git call is one quoted `sh -c` script; the changed working-tree files
arrive in one `tar` stream. A load's calls share one connection, kept for a minute in
`/tmp/kumiko-diff-$USER` (or `$XDG_RUNTIME_DIR`), so reloading opens no new login.
`DIFF_SSH` names another client, such as a wrapper adding `-i` or `-J` options.

`⌘K` lists the hosts of `~/.ssh/config` (and the files it `Include`s) under "SSH hosts",
leaving out wildcard patterns, `Match` blocks and Git hosting services such as GitHub.
`Enter` on a host fills in `host:~/` and keeps the palette open: one SSH call finds the Git
repositories on it (below the home folder, four levels deep, and `/srv`, `/var/www`, `/opt`,
three; never inside hidden or `node_modules` folders), listed after that host's recent
checkouts and narrowed by the path you type, the bare path last. Opening a remote folder
that is not a repository says so. Typing `host:/path` or
`host:~/path` directly works too, and opened checkouts are remembered with their host.
Staged changes, commits, pull requests and "Show Working Tree Changes" stay on that host.
The disk is not watched remotely: press `r` to reload.

## Projects and worktrees

`⌘K` (or `⌘O`, or the folder button) opens a palette of projects, after
[Flint](../Flint/README.md)'s Quick Launch: worktrees of the open project first, then
recent projects, then every repository found under the home folder, then commands.
Type to fuzzy-match names, branches and paths; `Enter` switches the window to that
checkout's working tree. A leading `>` (`⇧⌘P`) lists commands only, and a typed `/…`
or `~/…` path opens directly.

Discovery is Flint's walk (`Sources/Projects.cs`): it stops at each `.git`, skips hidden,
dependency and build folders, bundles, symlinks and other volumes. Linked worktrees
and their branches are read from each repository's `.git/worktrees` metadata, so
checkouts kept outside the scanned folders are listed too; no Git process starts.
The repositories found are cached, so the palette lists them at once on the next
launch while a fresh scan runs the first time it opens (or on "Rescan Projects").
State lives in `~/.kumiko-diff/` (`DIFF_HOME` or `--state-dir=PATH`); add scan roots
with `DIFF_ROOTS` (platform path separator).

## The open repository

The palette starts with the open repository: its worktrees as Git lists them (also on an
SSH host), the current one marked, then its open pull requests on GitHub, before recent
projects and other pull requests. The repository is found from its `origin` remote
(`git@host:owner/repo.git`, `https://host/owner/repo`, `ssh://…`); an SSH alias such as
`git@github-work:owner/repo.git` resolves through `~/.ssh/config`. Its pull requests are read
with the gh account of that host and open like any other, so `#` lists them first too.

## Pull requests and commits

With [gh](https://cli.github.com) installed, the palette also lists the open pull
requests that involve any account `gh auth status` shows (authored, assigned, review
requested or mentioned), each once, newest first; `#` (or "Pull Requests…") lists only
them. `Enter` shows one from its merge base to its head, read through the GitHub API
with the token of the account that found it, so no local clone is needed. The list is
kept for a minute and searched again the next time the palette shows it after that;
"Refresh Pull Requests" searches at once. Tokens come from `gh auth token` for each
request and are only sent to that account's API host.

The first five pull requests the palette shows load in the background, so choosing one
shows it at once; one chosen while it still loads waits for that load instead of
starting another. A background load is kept until its pull request changes on GitHub
or leaves the list, and one GitHub refuses is not retried until then (choosing it
loads it normally). Zen mode reads its file whole, so it always loads.

The palette also lists the open repository's newest 200 commits, read again each time
it opens: the ten newest without a query, all of them with `@` (or "Commits…").
`Enter` shows a commit against its first parent, and a first commit against an empty
tree. "Show Working Tree Changes" returns to the working tree.

Working-tree, staged and folder comparisons watch the disk with native file
notifications (coalesced for 250 ms) and reload in place; the cursor stays on its
file and line. Idle windows wake for nothing.

## Keyboard

| Keys | Action |
|---|---|
| `j` `↓` / `k` `↑` | Next / previous line |
| `Space` `PgDn` `⌃F` / `⇧Space` `PgUp` `⌃B` | Page down / up |
| `⌃D` / `⌃U` | Half page down / up |
| `g` `Home` `⌘↑` / `G` `End` `⌘↓` | Top / bottom |
| `n` `]` `⌘]` / `p` `[` `⌘[` | Next / previous hunk |
| `⇧N` `}` `⌥↓` / `⇧P` `{` `⌥↑` | Next / previous file |
| `⌘K` `⌘O` / `⇧⌘P` | Open a project or worktree / commands |
| `t` `⌘P` | Go to file (fuzzy) |
| `/` `⌘F`, then `Enter` / `⇧Enter` / `⌘G` | Search changed lines, next / previous match |
| Drag / double / triple click / `⇧Click` | Select text / a word / a line / move the selection's end |
| `m` `⌘G` / `⇧M` `⇧⌘G` / `⌘C` / `Esc` | Next / previous occurrence / copy / clear the selection |
| `u` `⌘1` / `s` `⌘2` / `v` | Unified / split / toggle |
| `x` `o` `Enter` / `⇧X` | Fold the file / fold or unfold all |
| `w` / `⌘B` / `r` `⌘R` | Ignore whitespace / sidebar / reload |
| `z`, then `z` or `Esc` | Zen mode: one file, full screen, both sides / leave |
| `?` / `Esc` | Shortcut list / close |

Zen mode (`z`, the full-screen toolbar button or "Enter Zen Mode" in `⇧⌘P`) takes the
window full screen and hides the toolbar, sidebar and status line, leaving the old and
new sides of the file under the cursor and no other file. The file reads whole, from its
first line to its last, with the changes in place; zen mode reloads the diff to read it. `⇧N` / `⇧P` and the
`⌘P` finder change which file it shows, as do `→` / `←`; `n` / `p` (and `]` / `[`) jump
between the file's changes, each block of changed lines; search looks only in that file. Leaving it
restores the previous view mode, chrome and every file, with the cursor where it was;
leaving full screen from the window ends it too. Offscreen runs (`--zen` with
`--kumiko-snapshot`) only change the layout.

Dragging over code selects text in that line, down to a single character; a double
click selects a word and a triple click the line without its indentation. Every other
place the text shows, on either side of the diff, is tinted too, so a renamed
identifier or moved code stands out. A bar in the corner shows the text and which
occurrence is selected; `m` / `⇧M` move the selection through them, wrapping, and
`⌘C` copies it. Switching views, folding and reloads find the text again.

Letters follow the active keyboard layout (`/` is Shift+7 on German keyboards) and
never fire while a search or finder field has focus. Click a row to move the
cursor, a file header to fold it, or a sidebar entry to jump to its file.

## How it stays fast

- **Highlighting** (`Highlight/`): hand-written, single-pass TypeScript, TSX,
  JavaScript, JSX, JSON and C# lexers. No regular expressions and no per-token strings;
  state carries across lines (block comments, nested template literals, multi-line
  JSX; C# verbatim, raw and interpolated strings), regex/division is decided by the
  previous significant token. 2 MB of TSX tokenizes in about 11 ms, 2 MB of C# in
  about 22 ms. Whole files are highlighted, so a hunk inside a comment
  or template colours correctly.
- **Diffing** (`Core/LineDiff*.cs`, `Core/WordDiff.cs`): Git's histogram diff with a
  Myers fallback, slider/indent heuristics and a hard work budget, plus word-level
  emphasis for paired lines. 100k lines with 1000 edits diff in about 11 ms; two
  unrelated 200k-line files in about 45 ms.
- **Loading** (`Sources/`): Git runs with argument lists, never a local shell; every old
  blob comes through one `git cat-file --batch` process. Files are diffed and
  highlighted in parallel on worker threads. Whole files feed the diff and the
  highlighter, then only shown lines keep their text and colours, so memory follows
  the size of the diff (a 95 MiB patch settles at about 0.75 GB instead of 1.3 GB).
- **Rendering**: updates flatten the diff into rows and pixel offsets once; views
  build only visible rows through `Context.VirtualList` with pinned file headers and
  a shared horizontal offset (gutters stay put).
- **Framework work for this example**: display-link frame pacing and coalesced
  scroll input on macOS, layout-aware character shortcuts, incremental Yoga relinking,
  reused accessibility records, single-run tinted code lines and incremental atlas
  uploads. See the changelog.

## Measured

`tests/Kumiko.DiffViewer.E2E --perf-only` loads 40 generated TSX files (200k source
lines, a 110k-line diff), opens a 1280×820 window at 2× scale offscreen and times
complete frames (input, build, layout, paint and the accessibility snapshot) while
scrolling 36 px per frame (trackpad) or 480 px per frame (fling), and while holding
`j`. NativeAOT on an Apple Silicon Mac:

| Scenario | p50 | p95 | Max |
|---|---|---|---|
| Unified, trackpad | 0.83 ms | 1.08 ms | 4.7 ms |
| Unified, fling | 0.86 ms | 1.10 ms | 2.0 ms |
| Split, trackpad | 1.00 ms | 1.41 ms | 3.3 ms |
| Split, fling | 1.04 ms | 1.75 ms | 3.0 ms |
| Held `j` / `Space` | | 1.87 ms | |

The check fails above 8 ms at p95 (a 120 Hz frame is 8.3 ms). Loading, diffing and
highlighting the whole benchmark takes about 35 ms after warm-up. GPU encoding and
presentation are not included; the Metal renderer skips frames that would redraw the
image already on screen.

## Verify

```sh
dotnet run --project examples/DiffViewer -c Release -- --demo \
  --kumiko-snapshot=artifacts/diffviewer/demo.png --kumiko-tree=artifacts/diffviewer/demo.txt --kumiko-dev
dotnet run --project tests/Kumiko.DiffViewer.E2E -c Release -- artifacts/diffviewer/e2e
dotnet publish tests/Kumiko.DiffViewer.E2E -c Release -r osx-arm64 -p:PublishAot=true -o artifacts/aot/diffviewer
artifacts/aot/diffviewer/Kumiko.DiffViewer.E2E artifacts/diffviewer/e2e-aot
```

The harness creates temporary repositories with isolated Git configuration; it never
reads your repositories, Git settings or clipboard. Failure scenarios were written
before implementation in `tests/Kumiko.DiffViewer.E2E/ACCEPTANCE.md`. It writes
PNG/tree/warnings artifacts for unified, split, collapsed, search, selection, finder, help and
dark scenes, and `report.json` with checks, timings and PNG SHA-256 hashes.

TypeScript-family files, JSON and C# (`.cs`, `.csx`) are highlighted; other files show
as plain text.
