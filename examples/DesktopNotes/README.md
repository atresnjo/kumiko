# DesktopNotes

Two small note windows, separate text/focus, and injected in-memory saves.
`NotesApp.Actions` defines Save, Pin and Close once. `NotesScreen` projects them
into toolbar buttons, document/context menus, keyboard shortcuts and the existing
searchable palette. `NotesApp.Update` stays the single owner of document state;
`Cmd<NoteMessage>` runs storage work.

Save is Primary+S. Pin is Primary+Shift+P. Open the palette with Commands; use
Note menu, right-click the hint, or focus the hint and press Shift+F10 for actions.
Dirty notes veto closing. Editing during a save preserves dirty state for the
newer text. Closing a note discards only a clean, already saved in-memory note;
there is no disk persistence or recovery after quitting this demonstration.

Create a snapshot without opening any native windows:

```sh
dotnet run --project examples/DesktopNotes -c Release -- \
  '--kumiko-window=Note 1' --kumiko-dev \
  --kumiko-snapshot=artifacts/desktop/example.png \
  --kumiko-tree=artifacts/desktop/example.txt
```

Native menu bars use the existing macOS backend and explicitly named note
submenus. Windows/Linux use the in-window surfaces and shortcuts.
