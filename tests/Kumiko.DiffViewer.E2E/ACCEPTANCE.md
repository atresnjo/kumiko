# Diff viewer behavior, before implementation

Drive the DiffViewer example through Kumiko's offscreen Tester and temporary Git
repositories created by the harness. Never open a native window, send system
input, or read the user's clipboard, repositories or application state.

## Failure scenarios

Diffing

- An edit script reproduces neither side: applying it to the old lines does not
  give the new lines, or context/added/removed counts disagree with hunk headers.
- Identical inputs report changes; an empty side, a missing final newline, CRLF
  endings or a one-line file produce off-by-one line numbers.
- Unrelated 200k-line files, or a file of repeated identical lines, stall the
  worker for seconds instead of finishing in bounded time.
- Hunks merge across more than twice the context, or adjacent changes split into
  hunks that repeat the same context lines.
- Word emphasis marks an unchanged line, or highlights a whole line when only one
  identifier changed. Wildly different pairs get noisy character emphasis.

Highlighting (TypeScript, TSX, JavaScript)

- A block comment, template literal (with nested `${}` and inner backticks) or
  multi-line JSX attribute string loses its state at the next line.
- Regex literals and division are confused (`a / b / c` versus `x = /a\/b/g`).
- Keywords used as properties (`obj.default`, `{ type: 1 }`) colour as keywords.
- Generics, arrow functions, decorators, optional chaining, numeric separators,
  bigint and hex literals break tokenization or misplace later tokens.
- Token ranges overlap, leave the line, or are not ordered by start column.
- Tokenizing a 2 MB file takes longer than a frame budget of the load (50 ms).

Highlighting (C#)

- Raw `"""` literals, verbatim `@"..."` strings with `""` escapes, block comments
  and interpolation holes lose their state across lines, or `{{`/`}}` escapes open
  holes. A `$$"""` literal treats single braces as holes.
- `#region` names or `///` documentation colour as code; attributes, generic type
  arguments, base lists and declared types are not recognised as names of types.
- Contextual keywords (`var`, `record`, `with`, `get`/`init`) colour as keywords
  where they are names, such as `record.Name` or `var = 1`.
- Escapes inside verbatim strings, or `'\n'` character literals, are mis-split.

Sources

- Working-tree diffs miss staged, unstaged, untracked, deleted or renamed files,
  or report files ignored by `.gitignore`.
- Commit ranges, a single revision against the working tree, and two folders
  produce different results from `git diff --no-index` line counts.
- Binary files are decoded as text; non-UTF-8 text crashes the loader.
- Paths with spaces or Unicode are mangled. Git is invoked through a shell.
- An edit in the working tree does not refresh the diff, or refreshing loses the
  cursor file and line when that file still exists.
- A loaded document keeps the text or colours of lines no row shows, so memory
  grows with file size instead of diff size; or dropping them changes a shown
  line's text, number or colour (a block comment opened above a hunk must still
  colour the hunk's lines as comment).

Remote checkouts (SSH)

- `host:/path`, `host:~/path`, `ssh://user@host:port/path` or `--ssh=HOST` with
  `--repo=` / a path do not select a remote checkout, or a revision argument such
  as `main..feature` or `HEAD~1` is taken for a host.
- A remote working tree, staged change or commit range differs from the same
  checkout loaded locally: staged, unstaged, untracked, deleted, renamed, binary
  or non-UTF-8 files, or paths with spaces and Unicode, go missing or change.
- Any Git call for a remote checkout runs locally, a path is not quoted for the
  remote shell, `~` is not the remote home, or each working-tree file starts its
  own SSH session.
- An unreachable host or missing folder hangs, prompts, or fails without the
  SSH or Git message. A remote checkout starts a local file watcher.
- The palette lists a remote checkout's commits from a local repository, marks a
  local project with the same path as current, or reopens a recent remote
  checkout as a local folder. Switching to staged changes, a commit or a pull
  request and back loses the host.
- ⌘K does not list the `Host` aliases of `~/.ssh/config` and the files it
  `Include`s, or lists wildcard or negated patterns, `Match` blocks, Git hosting
  hosts (github.com and aliases whose HostName is one) or one alias twice. A
  host's detail is not its user@HostName.
- Choosing a host closes the palette instead of filling in `host:~/` with the
  field ready for the path. While a `host:path` is typed, that host's recent
  checkouts and the Git repositories found on it (home, /srv, /var/www, /opt;
  not inside hidden or dependency folders) are not offered, are not narrowed by
  the typed path, or come after the bare path row, so Enter right after
  choosing a host opens the home folder. The host is searched more than once
  per palette session, or a failed search shows no reason.
- With a query, the palette mixes projects, hosts, commits, pull requests and
  commands in one ranked list without headings: a group's rows are not
  together under its heading, groups are not ordered by their best match (the
  best match is not first), or a host and a remote repository share an icon.
- Opening a remote folder that is not a repository shows only Git's message,
  without naming the folder and how to choose a repository.
- Tests reach a real host or the user's SSH configuration; offscreen runs list
  the user's hosts.

Viewing and keyboard

- Scrolling builds rows for the whole diff instead of the viewport; a 100k-line
  diff frame (build, layout and paint) exceeds 8 ms at the 95th percentile, or a
  frame allocates unboundedly.
- Unified and split views disagree on additions/deletions, misalign split rows,
  or lose the cursor line when switching views.
- j/k, arrows, n/p, ]/[ , ⌘[/⌘], Space/Shift+Space, PageUp/PageDown, g/G, Home/End
  and ⌘↑/⌘↓ do not move the cursor as documented, or leave it off-screen.
- File navigation (Alt+↑/↓, sidebar selection, ⌘P filter) does not reveal the
  file header; collapsed files still show their lines or keep the cursor inside.
- Keys typed in the filter field also move the diff cursor; Escape does not close
  the filter or help overlay.
- The sticky file header shows the wrong file at a file boundary.
- Long lines wrap or are clipped without horizontal scrolling; horizontal
  scrolling moves the line-number gutter.
- Light/dark, unified/split and collapsed scenes produce warnings, or the same
  offscreen flow produces different PNG hashes.

Zen mode

- `z` or the toolbar button leaves the toolbar, sidebar or status line on
  screen, shows the unified view instead of the two sides, or does not ask the
  window for full screen. The pane keeps a sidebar-sized gap.
- Zen mode shows any file but the one under the cursor, or shows only that
  file's hunks instead of every line from its first to its last; outside zen
  mode a file is read whole. The cursor's line leaves the view when the whole
  file loads. ⇧N, ⇧P or the ⌘P finder does not replace it with the next, previous
  or chosen file, or ⇧N past the last file leaves zen mode or shows nothing.
  → / ← do not switch files in zen mode. n / p / ] / [ do not step between the
  zen file's blocks of changed lines (it is one hunk), or leave the file.
  Leaving zen mode does not bring every file back with the cursor in the zen
  file; a reload that drops the zen file shows every file instead of one.
- `z` or Escape does not leave zen mode, restore the previous view mode and
  chrome, or release full screen; a window that was already full screen before
  zen mode is taken out of it.
- Leaving full screen from the window (green button, ⌃⌘F) keeps the chrome
  hidden. Escape closes an open search before it leaves zen mode.
- Offscreen runs or tests touch a native window to enter full screen.
- Holding the native window fails because full screen is entered or released
  while the subscription starts or stops, where Kumiko forbids window calls.
  Every frame after the failure stops short, so the palette or finder query
  repeats its last character until the window freezes. ⌘P, ⌘K and their typed
  query must work in zen mode; a hidden window is never made full screen.

Project palette

- ⌘K, ⌘O or ⇧⌘P does not open the palette, or typing into it also moves the
  diff cursor. Escape or a backdrop click leaves it open.
- Discovery descends into dependency, build or hidden folders, follows
  symlinks, or misses a linked worktree whose checkout lives outside the
  scanned roots (worktrees are listed from the repository's own metadata).
- A worktree shows the main checkout's branch, or no branch for a detached HEAD.
- Opening the palette waits for a scan: cached projects must show at once and
  the scan result replaces them without moving the highlight off-screen.
- Choosing a project or worktree keeps the previous diff, folds, search or
  watcher, or a late result from the previous project replaces the new one.
- Recent projects are not listed first, repeat, or survive in the wrong order.
- A `>` query lists projects; command rows run the wrong action or stay open.
- Launched outside a repository, the app shows a Git error instead of the
  palette. A directory argument is read as a revision.
- Tests read the user's home folder, recent list or known-project cache.

Pull requests and commits

- The palette leaves out pull requests of an account gh knows, lists one twice,
  or searches with another account's token. A token is shown, logged or sent to
  a host other than the account's API.
- Reopening the palette within a minute searches again, or after a minute does
  not; "Refresh Pull Requests" waits for the minute or closes the palette.
- `#` lists anything but pull requests; `@` anything but the open repository's
  commits, newest first.
- A pull request opens against its base branch's tip instead of the merge base,
  loses a renamed file's old path, or needs a local clone.
- A commit is shown against anything but its first parent; a first commit fails.
  A commit made since the palette last opened is missing when it opens again.
- The first five pull requests the palette shows are not loaded in the
  background, or choosing a loaded one asks GitHub again. One chosen while it
  still loads is fetched a second time. A refused one is retried while it is
  unchanged; an updated one keeps its earlier load.
- Tests use the user's gh accounts, tokens or the network.

Open repository

- The palette does not start with the open repository's worktrees (local or
  on an SSH host, from Git, the current one marked) and then its open pull
  requests from GitHub, before recent projects and other pull requests; or a
  pull request of the open repository is listed twice.
- The repository is not found from its remote: `git@host:owner/repo.git`,
  `https://host/owner/repo`, `ssh://git@host:22/owner/repo.git`, or an SSH
  alias whose HostName is the GitHub host (`git@github-work:owner/repo.git`).
  Local paths and `file://` remotes are taken for GitHub repositories.
- A repository on a host without a gh account shows an error, or one GitHub
  refuses shows none.

Launching from other tools (T3 actions, terminals)

- `--pr` does not open the open pull request whose head is the checkout's
  current branch (also in a worktree or over SSH); `--pr=N` or a pull request
  URL does not open that one; a branch without one, a detached HEAD or a
  repository without a GitHub remote fails without saying so, or the
  working tree flashes before the pull request.
- A second launch while Diff runs starts a second app, is dropped, or replaces
  the diff of an open window. A launch for the worktree or `--pr` a window
  already shows opens another window instead of focusing and reloading it; a
  window that switched elsewhere since is still taken for it. A closed window
  keeps its watcher and loads running. Reloading, or toggling whitespace,
  while `--pr` is still being looked up leaves the window loading forever.
- A pull request of the open repository is read file by file through
  GitHub's API instead of fetching its head into the checkout (locally or on
  the SSH host) and diffing merge base to head there; a pull request whose
  commits are already present is fetched again; a failed or hanging fetch,
  or a checkout of another repository, does not fall back to the API. The
  open repository's pull requests are preloaded through the API.

Text selection

- Dragging over characters of a line does not select exactly them, or selects
  the characters beside them; a single character cannot be selected. In the
  split view a drag follows the other side of a context row.
- A click without a drag leaves a selection behind or does not move the cursor.
  A double click does not select the word, a triple click the line without its
  indentation; Shift+click does not move the selection's head.
- Other shown places of the selected text are not tinted or not counted,
  occurrences overlap, a removed line's text is not found where it was added,
  or a drag recounts the whole diff on every pointer move.
- m / ⇧M / ⌘G / ⇧⌘G do not move the selection to the next / previous
  occurrence (wrapping) and reveal it, or the count is wrong. ⌘G steps search
  matches while search is open. ⌘C does not copy the selected text. Escape keeps
  the selection, or clears it before closing an open search.
- Switching unified/split, folding or reloading keeps a selection on stale rows
  instead of finding the text again.

## Framework

- A plain letter (h, j, m, n, p, /, [, ]) never reaches `Context.Shortcut`
  while no text field is focused, or reaches it while typing in a field.
- A variable-height virtual list builds rows outside the viewport plus overscan,
  places a row at the wrong offset, or a reveal request repeats every frame.
- An animated scroll is paced by a fixed 60 Hz timer on a 120 Hz display, or an
  idle window keeps a display link running.

Run `dotnet run --project tests/Kumiko.DiffViewer.E2E -c Release --
artifacts/diffviewer/e2e`. Keep PNG/TXT/warnings artifacts for each scene and
report.json with checks, timings and PNG SHA-256 hashes. Run the same
executable under NativeAOT.
