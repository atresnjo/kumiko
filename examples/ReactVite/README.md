# React + Vite inside Kumiko

A standard Vite React TypeScript app running in an embedded webview, under a
native Kumiko toolbar. The app in `web/` is an ordinary Vite project. It has no
Kumiko build plugin and no framework fork, and it also runs in a normal browser.

```
┌ Native Kumiko toolbar ─────────────── [Add from Kumiko] [Reset] ┐
│ ShellScreen (Kumiko view)                                        │
├──────────────────────────────────────────────────────────────────┤
│ Context.WebView("react", …)  →  Window.NewWebView("react", …)   │
│   React app: Host.watch() stream · Host.add() call · Host.info() │
└──────────────────────────────────────────────────────────────────┘
```

The shared count is native state in `CounterModel`. React gets it through a typed
`AsyncIterable` stream and changes it through a typed call. The Kumiko toolbar
changes the same model. Each side shows edits made by the other.

## Run the bundled app

```sh
cd examples/ReactVite/web
npm install
cd ../../..
dotnet run --project examples/ReactVite -c Release
```

After `npm install`, every `dotnet build` runs `npm run build` when React sources
are newer than `web/dist/`. The project embeds `dist/` in the executable, and the
page serves it from memory at `kumiko://localhost/`. You do not need to ship or
start a server, and NativeAOT publishing produces one file:

```sh
dotnet publish examples/ReactVite -c Release -r osx-arm64 -p:PublishAot=true -o artifacts/aot/react-vite
```

If `node_modules` is missing, for example on a .NET-only CI machine, the npm step
is skipped. The app then shows a page that explains how to build the bundle.

## Develop with Vite hot reload

```sh
cd examples/ReactVite/web && npm run dev          # terminal 1
dotnet run --project examples/ReactVite -c Release -- --dev-server=http://localhost:5173   # terminal 2
```

The webview loads the dev server directly, so React Fast Refresh works inside the
desktop window. The host trusts only the exact origin (scheme, host and port)
passed to `--dev-server`. `vite.config.ts` sets `strictPort` so Vite does not
silently move to another port. If 5173 is in use, run `npx vite --port 5199` and
pass that URL.

## The typed bridge

`HostServices.cs` registers the contract with explicit source-generated JSON
metadata:

| Client call | C# side | Thread |
| --- | --- | --- |
| `Host.info({})` | `Bind` → `HostInfo` record | UI thread |
| `Host.add({ by })` | `Bind` → sends `CounterMsg.Add`, runs `ProcessMessages`, returns the count | UI thread |
| `Host.watch({})` | `BindStream` → `CounterFeed.Watch`, newest value per watcher | worker |

`web/src/kumiko/client.js` and `client.d.ts` are generated from that contract and
checked in, so both Vite modes import the same module. Regenerate them after
changing `HostServices.cs`:

```sh
dotnet run --project examples/ReactVite -c Release -- --write-client=examples/ReactVite/web/src/kumiko
```

Kumiko injects `window.kumiko` only into the trusted main frame. In a plain
browser, `App.tsx` detects that the bridge is missing and shows a hint.

A stream ends when the `for await` loop breaks. A loop that is waiting for a value
exits when the next value arrives. Navigating away or disposing the page cancels
every producer.

## Verify without showing a window

```sh
dotnet run --project examples/ReactVite -c Release -- --verify=artifacts/react-vite
dotnet run --project examples/ReactVite -c Release -- \
  --kumiko-snapshot=artifacts/react-vite/shell.png --kumiko-tree=artifacts/react-vite/shell.txt
```

`--verify` uses a hidden app and window and a nonpersistent WKWebView. It checks
that the checked-in client matches the C# contract and that the bundle is
embedded. It then checks that React mounts with its hashed JS and CSS assets, that
a native message reaches React through the stream, and that a click inside React
updates the native model. The exit code is non-zero on failure, and the results
are written to `react-vite.json`. Add `--dev-server=<url>` to run the same flow
against a running Vite server. The snapshot renders the native shell through
Tester; the webview slot is a placeholder, and no browser pixels are captured.
The browser flow runs on macOS. The Windows (WebView2) and Linux (WebKitGTK)
hosts compile but have not been run here.
