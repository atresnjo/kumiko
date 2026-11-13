# Typed views: local desktop demo

One auth screen, one main screen, one edit-name dialog. Enter a display name,
sign in, edit it with save/cancel, then sign out. This is in-memory demo
authentication: no credentials, network calls, persistence or backend services.

`DemoApp.Root(model)` returns a navigation root. Its required history, host key,
screen projection and navigation-message mapping make screen ownership explicit:

```csharp
NavigationRoot<DemoMsg>.Create(model.Navigation, "app", route => route switch
{
    DemoRoute.Auth => new AuthScreen(new(model.SignInName))
        .Map<DemoMsg>(static message => new DemoMsg.Auth(message)),
    DemoRoute.Main => new MainScreen(new(model.UserName!, model.Dialog))
        .Map<DemoMsg>(static message => new DemoMsg.Main(message)),
    _ => throw new ArgumentOutOfRangeException(nameof(route))
}, static request => new DemoMsg.Navigate(request));
```

AuthScreen accepts AuthProps and emits AuthMsg; MainScreen uses MainProps/MainMsg.
They derive Screen, which cannot be passed to Ui.Child or used directly as the
program root. MainScreen composes the dialog with Ui.Child, mapping EditNameMsg
to MainMsg.Edit, then the root maps MainMsg to DemoMsg.Main. Only the root sees
DemoModel. All rendering is protected and invoked by framework internals.

Constructing a screen is a render-local declaration, not navigation. The history
entry owns identity. Sign-in replaces auth with main; sign-out resets both history
directions. Mouse Back/Forward and Command+[ / Command+] (macOS) or Alt+Left /
Alt+Right (Windows/Linux) become typed history messages automatically. A modal or
active IME composition blocks them. This two-screen auth flow deliberately has no
Back destination after sign-in; a supporting E2E fixture demonstrates real browsing
history without adding screens to this demo. The dialog remains an overlay.

C# rejects incompatible props/messages/mappings, missing navigation arguments,
screens used as components, direct screen roots and external Render calls.
It does not recognize screen-like UI deliberately drawn inside a component,
prevent deliberately choosing the whole
app model as props, establish deep purity from readonly, or prove the lifetime
of references. Analyzer capture/purity checks and runtime lifetime/key validation
remain necessary. Partial files within a cohesive view are allowed.

Use the pinned .NET 10 host. All verification below is offscreen and uses the
Tester's isolated clipboard; no real window or system input is needed:

```sh
dotnet run --project examples/TypedViews -c Release -- \
  --kumiko-snapshot=artifacts/typed-views/auth.png \
  --kumiko-tree=artifacts/typed-views/auth.txt --kumiko-dev
dotnet run --project tests/Kumiko.TypedViews.E2E -c Release -- artifacts/typed-views/navigation/demo
dotnet run --project tests/Kumiko.Analyzers.E2E -c Release -- artifacts/typed-views/navigation/analyzers
dotnet publish tests/Kumiko.TypedViews.E2E -c Release -r osx-arm64 \
  -p:PublishAot=true -o artifacts/typed-views/navigation/publish/demo
artifacts/typed-views/navigation/publish/demo/Kumiko.TypedViews.E2E artifacts/typed-views/navigation/demo-aot
```

The E2E command writes numbered PNGs, matching tree text/warnings, report.json,
and recorded/saved JSONL sessions. It covers Enter sign-in, accessible field
edits, dialog cancel/save/Escape, sign-out and replay. A separate fixture in that
host exercises two nested keyed child instances, reorder, caret/undo and removal
without cluttering the example. Another exercises Back/Forward, repeated visits,
500-frame retention, guards, IME/modal blocking, branch truncation, replacement,
reset and replay. Repeating the command overwrites the same artifacts.
`composition-language-report.txt` and `navigation-language-report.txt` record
compiler proofs with custom analyzers disabled, including an independent library.

To inspect a saved state again without a window:

```sh
dotnet run --project examples/TypedViews -c Release -- \
  --kumiko-replay=artifacts/typed-views/navigation/demo/demo-session.jsonl \
  --kumiko-snapshot=artifacts/typed-views/replayed.png \
  --kumiko-tree=artifacts/typed-views/replayed.txt --kumiko-dev
```
