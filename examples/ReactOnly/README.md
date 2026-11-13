# React only

The whole window is a standard Vite React TypeScript app. There is no Kumiko view
and no Kumiko program. C# opens the window, serves the bundle, and offers a small,
explicit set of native features as typed services. React can call those services
and nothing else.

```csharp
using var app = new App(new("React Only"));
NativeServices.Register(app);                        // app.Services.Bind(...)
using var window = app.NewPage(new("React Only", 960, 640, HiddenTitleBar: true),
    new(Assets: WebBundle.Load()));                  // or new(Url: "http://localhost:5173")
app.Run();
```

## Native features

`NativeServices.cs` is the entire native surface:

| Client call | Native feature |
| --- | --- |
| `Native.info({})` | platform, OS description, dark mode |
| `Native.openText({})` | open-file dialog, then reads up to 1 MiB of text; `null` if cancelled |
| `Native.saveText({ name, text })` | save dialog, then writes the file; returns its path or `null` |
| `Native.copyText({ text })`, `Native.readText({})` | system clipboard |
| `Native.notify({ title, body })` | desktop notification |
| `Native.reveal({ path })` | shows the file in Finder, Explorer or the file manager |
| `onAppearance(dark => …)` | `NanoEvent` pushed when the system switches light/dark |

To add a feature, bind another method in `NativeServices.cs` and regenerate the
checked-in client:

```sh
dotnet run --project examples/ReactOnly -c Release -- --write-client=examples/ReactOnly/web/src/kumiko
```

Dialog handlers dispatch onto the main loop and complete a task, so modal
panels never run inside the bridge callback. Notifications need a signed app
bundle on macOS. Under `dotnet run`, the call may fail; React then shows the error
in its status bar.

## The title bar is React

On macOS and Windows the window uses `HiddenTitleBar`, and `TitleBar.tsx` draws
the title bar. Its CSS sets `--app-region: drag`, so pressing it moves the native
window and double-clicking toggles maximize. Buttons inside opt out with
`--app-region: no-drag`. macOS keeps its traffic lights over the page, so the bar
leaves 84 px for them. On Windows, React draws minimize, maximize and close and
calls `window.kumiko.window`. Linux keeps the GTK title bar, which already has
system buttons.

## Run

```sh
cd examples/ReactOnly/web && npm install && cd ../../..
dotnet run --project examples/ReactOnly -c Release
```

As in [ReactVite](../ReactVite/README.md), `dotnet build` rebuilds `web/dist/` when
React sources change. For hot reload, run `npm run dev` in `web/` and pass
`--dev-server=http://localhost:5173`. Publish one native file with:

```sh
dotnet publish examples/ReactOnly -c Release -r osx-arm64 -p:PublishAot=true -o artifacts/aot/react-only
```

## Verify without showing a window

```sh
dotnet run --project examples/ReactOnly -c Release -- --verify=artifacts/react-only
```

The check opens a hidden program-free page window on macOS. Hidden apps use a
private in-memory clipboard, so it never touches yours. It checks that:

- React mounts from the embedded bundle and reads native info;
- the Paste button reads the native clipboard;
- the Copy button writes it;
- a native appearance event switches the React theme both ways.

It skips dialogs and notifications, which would need a visible user session. The
drag region is covered by `PageWindowFlow` in `tests/Kumiko.DesktopFormats.E2E`.
Add `--dev-server=<url>` to run the same checks against a running Vite server.
