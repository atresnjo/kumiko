# Inventory workbench

A native reference app for Kumiko's reusable application controls. The data is
illustrative, lives in memory and is never written to user documents. The app
does not contact a server; its cancellable background loader supplies a large
fixture through the same dispatch path a real service would use.

- 100,000 editable, sortable records with viewport-only row construction.
- Name, stock and location editing with transactional validation.
- Supplier search, empty results, disabled-option support and keyboard choice.
- Right-click / Shift+F10 row menus with edit, restock and archive actions.
- Cancellable background loading, stale-result rejection and recoverable errors.
- Local edits and archives survive background refresh for the session.
- Light, dark and violet/compact themes using shared component tokens.

```sh
dotnet run --project examples/Workbench -c Release
dotnet run --project examples/Workbench -c Release -- \
  --kumiko-snapshot=artifacts/workbench.png \
  --kumiko-tree=artifacts/workbench.txt --kumiko-dev
dotnet run --project tests/Kumiko.Desktop.E2E -c Release -- artifacts/desktop
```

The snapshot CLI uses a deterministic 10,000-row fixture and never opens a
window. `--dark` selects the dark preset. `--kumiko-script` supports the usual
click/type/key/resize flow; the E2E suite also uses semantic locators, direct
isolated pointer events, native accessibility clients and controlled loader
completions.

`InventoryWorkbench` owns the application state and demonstrates main-thread
dispatch. `DataGrid<T>`, `SearchDropdown`, `ContextMenu`, theme tokens and text
validation live in the framework, with no dependency on this example's domain.

The visual reference is shadcn's neutral control styling. Base UI supplies the
interaction reference; these components are implemented in C#, not embedded web
components. The original Kumiko theme presets remain available to existing apps.


Pass `--desktop-shell` to opt into a tray menu, text/HTML clipboard offers,
notifications and the global shortcut `Primary+Shift+K`. Notifications require a
packaged macOS `.app` with a bundle identifier; errors appear in the workbench.
The tray menu exposes Copy and Notify actions. The same flag with
`--kumiko-snapshot` produces an offscreen image without constructing shell
services or changing the system clipboard:

```sh
dotnet run --project examples/Workbench -c Release -- --desktop-shell \
  --kumiko-snapshot=artifacts/desktop/shell-example.png
```
