# DiskMap behavior, before implementation

Drive the DiskMap example through Kumiko's offscreen Tester and temporary folders
created by the harness. Never open a native window, send system input, move
anything to the user's Trash, or read the user's clipboard or home folder. The
Trash, Finder and folder picker go through a recording fake.

## Failure scenarios

Scanning

- A folder's disk size is not the sum of its children, or its file count misses
  files of nested folders. Apparent size and allocated size are confused: a
  sparse file reports its logical length as disk size.
- A hard-linked file is counted once per link, so two links to 10 MB show 20 MB.
- A symbolic link to a folder is followed, counting its target twice or looping
  forever on a link to an ancestor. Another volume mounted inside the scanned
  folder is counted.
- An unreadable folder aborts the scan instead of being counted as an error and
  shown with the size that could be read.
- A cancelled scan keeps running, or delivers a result after a newer scan began.
- Names with spaces, Unicode or very long components are mangled or truncated.
- Scanning 100k files with the native enumerator is not faster than one stat per
  file, or progress messages arrive more often than about ten times a second.
- An idle window keeps a timer running after the scan finishes.

Treemap

- Tile areas are not proportional to their share of the shown size, tiles
  overlap, leave their parent, or have negative or NaN sizes.
- Squarified tiles grow needle thin when a near square split exists; the worst
  aspect ratio of a typical folder exceeds that of a slice layout.
- Thousands of tiny files produce thousands of tiles; tiles below a few pixels
  are not merged into one "smaller items" tile.
- Folders nest deeper than the space allows, or a nested folder hides its own
  header so it cannot be named or opened.
- Changing the metric (disk, apparent, file count) keeps the old order or sizes.
- A tile's identity changes when the same item moves between depths, so zooming
  in snaps instead of gliding from its old place.

Viewing and keyboard

- Zooming into a file, or past the scanned root, is possible; Escape at the root
  leaves the window in a broken state.
- The breadcrumb, sidebar and treemap disagree on the current folder or on the
  selected item. Selecting a list row does not select its tile.
- The filter hides the current folder itself, or matches case sensitively.
- Marked items survive a rescan that removed them; trashing removes the wrong
  item, leaves stale sizes in ancestors, or happens without confirmation.
- Copy Path copies a relative or wrong path.
- Light and dark appearances have layout, contrast or identity warnings, or text
  overflows tiles instead of truncating.

Performance

- Hovering across the treemap, zooming in and out, or switching metrics takes a
  frame longer than 8 ms at the 95th percentile at Retina scale.
- A frame builds more nodes than the visible tiles need.
